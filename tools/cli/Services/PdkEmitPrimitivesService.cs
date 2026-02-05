using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Cascode.Workspace;

namespace Cascode.Cli.Services;

internal static class PdkEmitPrimitivesService
{
    public sealed record EmitArgs(string PdkName, string DbPath, string OutputPath);

    public sealed record EmitResult(bool Succeeded, int PrimitivesWritten, string Message);

    public static EmitResult Emit(EmitArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!File.Exists(args.DbPath))
        {
            return new EmitResult(
                Succeeded: false,
                PrimitivesWritten: 0,
                Message: $"No PDK database found at '{args.DbPath}'. Run 'pdk scan' first."
            );
        }

        var models = PdkDatabaseReader.LoadModels(args.DbPath);
        if (models.Count == 0)
        {
            return new EmitResult(
                Succeeded: false,
                PrimitivesWritten: 0,
                Message: "No models found in pdk.db. Run 'pdk scan' first."
            );
        }

        var candidates = models.Where(m => m.DeviceClass is DeviceClass.Nmos or DeviceClass.Pmos);
        var chosenModels = candidates
            .GroupBy(m => PrimitiveNameFromModelName(m.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
                g.OrderBy(m => PreferModelTypeRank(m.ModelType))
                    .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .First()
            )
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chosenModels.Count == 0)
        {
            return new EmitResult(
                Succeeded: false,
                PrimitivesWritten: 0,
                Message: "No NMOS/PMOS models found in pdk.db."
            );
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args.OutputPath))!);

        var libraryName = $"lib.pdk.{SanitizeLibrarySegment(args.PdkName)}.prim";
        var sb = new StringBuilder();
        sb.AppendLine("VERSION 3.0");
        sb.AppendLine();
        sb.AppendLine($"library {libraryName}");
        sb.AppendLine();

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = 0;

        foreach (var model in chosenModels)
        {
            var primitiveKind = model.DeviceClass == DeviceClass.Nmos ? "NMOS" : "PMOS";
            var primitiveName = MakeUniquePrimitiveName(
                PrimitiveNameFromModelName(model.Name),
                usedNames
            );
            usedNames.Add(primitiveName);

            var geom = PdkDatabaseReader.LoadGeometryForModel(args.DbPath, model.Name);
            if (geom is not null)
            {
                sb.AppendLine(
                    $"// geometry: W=[{FormatNullable(geom.WMin)}..{FormatNullable(geom.WMax)}] L=[{FormatNullable(geom.LMin)}..{FormatNullable(geom.LMax)}] NF=[{FormatNullable(geom.NfMin)}..{FormatNullable(geom.NfMax)}]"
                );
            }

            sb.AppendLine($"primitive {primitiveKind} {primitiveName}(size primSize) {{");
            sb.AppendLine($"  device \"{model.Name}\"");
            sb.AppendLine("  params {");

            // Most PDK-provided transistor wrappers are subckts with parameters: w, l, mult, nf.
            // For raw MOS models, W/L/m are more portable.
            var isSubckt = string.Equals(
                model.ModelType,
                "subckt",
                StringComparison.OrdinalIgnoreCase
            );
            if (isSubckt)
            {
                sb.AppendLine("    w = primSize.W");
                sb.AppendLine("    l = primSize.L");
                sb.AppendLine("    mult = primSize.M");
                sb.AppendLine("    nf = primSize.NF");
            }
            else
            {
                sb.AppendLine("    W = primSize.W");
                sb.AppendLine("    L = primSize.L");
                sb.AppendLine("    m = primSize.M");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();
            written++;
        }

        File.WriteAllText(args.OutputPath, sb.ToString());

        return new EmitResult(
            Succeeded: true,
            PrimitivesWritten: written,
            Message: $"Wrote {written.ToString(CultureInfo.InvariantCulture)} primitive(s) to '{args.OutputPath}'."
        );
    }

    private static string PrimitiveNameFromModelName(string modelName)
    {
        // Keep names readable while remaining deterministic.
        // Example: "sky130_fd_pr__nfet_01v8__model.0" -> "nfet_01v8"
        var name = modelName ?? string.Empty;
        var modelMarker = name.IndexOf("__model", StringComparison.OrdinalIgnoreCase);
        if (modelMarker >= 0)
        {
            name = name.Substring(0, modelMarker);
        }

        var lastSep = name.LastIndexOf("__", StringComparison.Ordinal);
        if (lastSep >= 0 && lastSep + 2 < name.Length)
        {
            name = name[(lastSep + 2)..];
        }

        name = name.Replace('.', '_');
        name = SanitizeIdentifier(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Primitive";
        }

        return name;
    }

    private static int PreferModelTypeRank(string? modelType)
    {
        // Prefer raw MOS models ("model") over wrapper subckts ("subckt") for op_param stability.
        return string.Equals(modelType, "model", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static string MakeUniquePrimitiveName(string baseName, HashSet<string> used)
    {
        var name = baseName;
        var i = 2;
        while (used.Contains(name))
        {
            name = $"{baseName}_{i.ToString(CultureInfo.InvariantCulture)}";
            i++;
        }
        return name;
    }

    private static string SanitizeLibrarySegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "pdk";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c == '-' || c == '.')
            {
                sb.Append('_');
            }
        }

        var s = sb.ToString();
        return string.IsNullOrWhiteSpace(s) ? "pdk" : s;
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        var s = sb.ToString();
        if (s.Length > 0 && !char.IsLetter(s[0]) && s[0] != '_')
        {
            s = "_" + s;
        }

        return s;
    }

    private static string FormatNullable(double? v)
    {
        return v is null ? "-" : v.Value.ToString("G6", CultureInfo.InvariantCulture);
    }

    private static string FormatNullable(int? v)
    {
        return v is null ? "-" : v.Value.ToString(CultureInfo.InvariantCulture);
    }
}
