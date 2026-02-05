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
    public sealed record EmitArgs(
        string PdkName,
        string DbPath,
        string OutputPath,
        bool IncludeFixed
    );

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

        var candidates = models
            .Where(m => m.DeviceClass is DeviceClass.Nmos or DeviceClass.Pmos)
            .Select(m => new ModelCandidate(
                Model: m,
                PrimitiveName: PdkPrimitiveNaming.PrimitiveNameFromModelName(m.Name),
                FamilyName: PdkPrimitiveNaming.PrimitiveFamilyNameFromModelName(m.Name)
            ))
            .ToList();

        var skippedFixedOnlyFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chosenModels = args.IncludeFixed
            ? candidates
                .GroupBy(
                    c => new PrimitiveKey(c.Model.DeviceClass, c.PrimitiveName),
                    PrimitiveKeyComparer.Instance
                )
                .Select(g =>
                    g.OrderBy(c => PdkPrimitiveNaming.PreferModelTypeRank(c.Model.ModelType))
                        .ThenBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
                        .First()
                )
                .OrderBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : candidates
                .GroupBy(
                    c => new FamilyKey(c.Model.DeviceClass, c.FamilyName),
                    FamilyKeyComparer.Instance
                )
                .Select(group =>
                {
                    var familyRepresentative = group
                        .Where(c =>
                            c.PrimitiveName.Equals(c.FamilyName, StringComparison.OrdinalIgnoreCase)
                        )
                        .OrderBy(c => PdkPrimitiveNaming.PreferModelTypeRank(c.Model.ModelType))
                        .ThenBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (familyRepresentative is not null)
                    {
                        return familyRepresentative;
                    }

                    skippedFixedOnlyFamilies.Add(group.Key.FamilyName);
                    return null;
                })
                .Where(c => c is not null)
                .Select(c => c!)
                .OrderBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (chosenModels.Count == 0)
        {
            return new EmitResult(
                Succeeded: false,
                PrimitivesWritten: 0,
                Message: "No NMOS/PMOS models found in pdk.db."
            );
        }

        var subcktBodiesByName = SpiceSubcktOpPathResolver.IndexSubcktBodies(
            chosenModels
                .Where(m => IsSubcktModel(m.Model.ModelType))
                .SelectMany(m => m.Model.SourceFiles ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(TryGetFullPathIfExists)
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList()
        );

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
            var primitiveKind = model.Model.DeviceClass == DeviceClass.Nmos ? "NMOS" : "PMOS";
            var primitiveNameBase = args.IncludeFixed ? model.PrimitiveName : model.FamilyName;
            var primitiveName = MakeUniquePrimitiveName(primitiveNameBase, usedNames);
            usedNames.Add(primitiveName);

            var geom = PdkDatabaseReader.LoadGeometryForModel(args.DbPath, model.Model.Name);
            if (geom is not null)
            {
                sb.AppendLine(
                    $"// geometry: W=[{FormatNullable(geom.WMin)}..{FormatNullable(geom.WMax)}] L=[{FormatNullable(geom.LMin)}..{FormatNullable(geom.LMax)}] NF=[{FormatNullable(geom.NfMin)}..{FormatNullable(geom.NfMax)}]"
                );
            }

            sb.AppendLine($"primitive {primitiveKind} {primitiveName}(size primSize) {{");
            sb.AppendLine($"  device \"{model.Model.Name}\"");
            sb.AppendLine("  params {");

            // Most PDK-provided transistor wrappers are subckts with parameters: w, l, mult, nf.
            // For raw MOS models, W/L/m are more portable.
            var isSubckt = string.Equals(
                model.Model.ModelType,
                "subckt",
                StringComparison.OrdinalIgnoreCase
            );
            if (isSubckt)
            {
                sb.AppendLine("    w = primSize.W");
                sb.AppendLine("    l = primSize.L");
                sb.AppendLine("    mult = primSize.M");
                sb.AppendLine("    nf = primSize.NF");

                var opSegments = SpiceSubcktOpPathResolver.TryResolveUniqueOpSegments(
                    model.Model.Name,
                    subcktBodiesByName
                );
                if (opSegments is not null && opSegments.Count > 0)
                {
                    for (var i = 0; i < opSegments.Count; i++)
                    {
                        sb.AppendLine($"    __op_path{i} = {opSegments[i]}");
                    }
                }
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

        var skippedMessage =
            skippedFixedOnlyFamilies.Count == 0
                ? string.Empty
                : $" Skipped {skippedFixedOnlyFamilies.Count.ToString(CultureInfo.InvariantCulture)} fixed-only family/families (use '--include-fixed' to include them).";

        return new EmitResult(
            Succeeded: true,
            PrimitivesWritten: written,
            Message: $"Wrote {written.ToString(CultureInfo.InvariantCulture)} primitive(s) to '{args.OutputPath}'.{skippedMessage}"
        );
    }

    private static bool IsSubcktModel(string? modelType) =>
        string.Equals(modelType, "subckt", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetFullPathIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
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

    private sealed record ModelCandidate(
        SpectreModel Model,
        string PrimitiveName,
        string FamilyName
    );

    private sealed record PrimitiveKey(DeviceClass DeviceClass, string PrimitiveName);

    private sealed record FamilyKey(DeviceClass DeviceClass, string FamilyName);

    private sealed class PrimitiveKeyComparer : IEqualityComparer<PrimitiveKey>
    {
        public static readonly PrimitiveKeyComparer Instance = new();

        public bool Equals(PrimitiveKey? x, PrimitiveKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.DeviceClass == y.DeviceClass
                && string.Equals(
                    x.PrimitiveName,
                    y.PrimitiveName,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public int GetHashCode(PrimitiveKey obj)
        {
            return HashCode.Combine(
                obj.DeviceClass,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PrimitiveName)
            );
        }
    }

    private sealed class FamilyKeyComparer : IEqualityComparer<FamilyKey>
    {
        public static readonly FamilyKeyComparer Instance = new();

        public bool Equals(FamilyKey? x, FamilyKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.DeviceClass == y.DeviceClass
                && string.Equals(x.FamilyName, y.FamilyName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(FamilyKey obj)
        {
            return HashCode.Combine(
                obj.DeviceClass,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FamilyName)
            );
        }
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
