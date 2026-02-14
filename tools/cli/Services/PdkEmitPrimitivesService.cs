using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Cascode.Workspace;

namespace Cascode.Cli.Services;

internal static class PdkEmitPrimitivesService
{
    public sealed record EmitArgs(
        string PdkName,
        string DbPath,
        string OutputDirectory,
        bool IncludeFixed
    );

    public sealed record EmitResult(bool Succeeded, int PrimitivesWritten, string Message);

    private enum PrimitiveCategory
    {
        Devices,
        Resistors,
        Capacitors,
        Diodes,
    }

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
            .Select(TryBuildModelCandidate)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        if (candidates.Count == 0)
        {
            return new EmitResult(
                Succeeded: false,
                PrimitivesWritten: 0,
                Message: "No supported models (NMOS/PMOS/Resistor/Capacitor/Diode) found in pdk.db."
            );
        }

        var subcktDefinitions = SpiceSubcktOpPathResolver.IndexSubcktDefinitions(
            candidates
                .Where(c => IsSubcktModel(c.Model.ModelType))
                .SelectMany(c => c.Model.SourceFiles ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(TryGetFullPathIfExists)
                .Where(path => path is not null)
                .Select(path => path!)
                .ToList()
        );

        var subcktBodiesByName = subcktDefinitions.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.BodyLines,
            StringComparer.OrdinalIgnoreCase
        );

        var skippedFixedOnlyFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedModels = SelectModels(candidates, args.IncludeFixed, skippedFixedOnlyFamilies);

        if (selectedModels.Count == 0)
        {
            return new EmitResult(
                Succeeded: false,
                PrimitivesWritten: 0,
                Message: "No compatible models selected for primitive emission."
            );
        }

        var skippedIncompatiblePassiveWrappers = 0;
        var filteredModels = selectedModels
            .Where(candidate =>
            {
                if (!RequiresPassiveCompatibilityCheck(candidate))
                {
                    return true;
                }

                if (!IsPassiveWrapperCompatible(candidate, subcktDefinitions))
                {
                    skippedIncompatiblePassiveWrappers++;
                    return false;
                }

                return true;
            })
            .ToList();

        if (filteredModels.Count == 0)
        {
            return new EmitResult(
                Succeeded: false,
                PrimitivesWritten: 0,
                Message: "No compatible models remained after passive wrapper filtering."
            );
        }

        var outputDirectory = Path.GetFullPath(args.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var libraryNamespace = PdkPrimitiveLibraryLayout.GetLibraryNamespace(args.PdkName);
        var fileBuilders = CreateCategoryBuilders(libraryNamespace);

        var usedPrimitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perCategoryCounts = new Dictionary<PrimitiveCategory, int>
        {
            [PrimitiveCategory.Devices] = 0,
            [PrimitiveCategory.Resistors] = 0,
            [PrimitiveCategory.Capacitors] = 0,
            [PrimitiveCategory.Diodes] = 0,
        };

        var written = 0;
        foreach (
            var candidate in filteredModels
                .OrderBy(c => c.Category)
                .ThenBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
        )
        {
            var primitiveNameBase = args.IncludeFixed
                ? candidate.PrimitiveName
                : candidate.FamilyName;
            var primitiveName = MakeUniquePrimitiveName(primitiveNameBase, usedPrimitiveNames);
            usedPrimitiveNames.Add(primitiveName);

            var geom = PdkDatabaseReader.LoadGeometryForModel(args.DbPath, candidate.Model.Name);
            AppendPrimitive(
                fileBuilders[candidate.Category],
                candidate,
                primitiveName,
                geom,
                subcktDefinitions,
                subcktBodiesByName
            );

            perCategoryCounts[candidate.Category] = perCategoryCounts[candidate.Category] + 1;
            written++;
        }

        WriteCategoryFiles(outputDirectory, fileBuilders);

        var summary =
            $"devices={perCategoryCounts[PrimitiveCategory.Devices]}, resistors={perCategoryCounts[PrimitiveCategory.Resistors]}, capacitors={perCategoryCounts[PrimitiveCategory.Capacitors]}, diodes={perCategoryCounts[PrimitiveCategory.Diodes]}";
        var skippedFixedMessage =
            skippedFixedOnlyFamilies.Count == 0
                ? string.Empty
                : $" Skipped {skippedFixedOnlyFamilies.Count.ToString(CultureInfo.InvariantCulture)} fixed-only family/families (use '--include-fixed' to include them).";
        var skippedPassiveMessage =
            skippedIncompatiblePassiveWrappers == 0
                ? string.Empty
                : $" Skipped {skippedIncompatiblePassiveWrappers.ToString(CultureInfo.InvariantCulture)} incompatible passive wrapper(s).";

        return new EmitResult(
            Succeeded: true,
            PrimitivesWritten: written,
            Message: $"Wrote {written.ToString(CultureInfo.InvariantCulture)} primitive(s) to '{outputDirectory}' ({summary}).{skippedFixedMessage}{skippedPassiveMessage}"
        );
    }

    private static ModelCandidate? TryBuildModelCandidate(SpectreModel model)
    {
        if (!TryMapCategory(model.DeviceClass, out var category, out var primitiveKind))
        {
            return null;
        }

        return new ModelCandidate(
            Model: model,
            PrimitiveKind: primitiveKind,
            Category: category,
            PrimitiveName: PdkPrimitiveNaming.PrimitiveNameFromModelName(model.Name),
            FamilyName: PdkPrimitiveNaming.PrimitiveFamilyNameFromModelName(model.Name)
        );
    }

    private static bool TryMapCategory(
        DeviceClass deviceClass,
        out PrimitiveCategory category,
        out string primitiveKind
    )
    {
        switch (deviceClass)
        {
            case DeviceClass.Nmos:
                category = PrimitiveCategory.Devices;
                primitiveKind = "NMOS";
                return true;
            case DeviceClass.Pmos:
                category = PrimitiveCategory.Devices;
                primitiveKind = "PMOS";
                return true;
            case DeviceClass.Resistor:
                category = PrimitiveCategory.Resistors;
                primitiveKind = "Resistor";
                return true;
            case DeviceClass.Capacitor:
                category = PrimitiveCategory.Capacitors;
                primitiveKind = "Capacitor";
                return true;
            case DeviceClass.Diode:
                category = PrimitiveCategory.Diodes;
                primitiveKind = "Diode";
                return true;
            default:
                category = default;
                primitiveKind = string.Empty;
                return false;
        }
    }

    private static List<ModelCandidate> SelectModels(
        IReadOnlyList<ModelCandidate> candidates,
        bool includeFixed,
        HashSet<string> skippedFixedOnlyFamilies
    )
    {
        if (includeFixed)
        {
            return candidates
                .GroupBy(
                    c => new PrimitiveKey(c.Model.DeviceClass, c.PrimitiveName),
                    PrimitiveKeyComparer.Instance
                )
                .Select(group =>
                    group
                        .OrderBy(c => PreferModelRank(c.Model.DeviceClass, c.Model.ModelType))
                        .ThenBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
                        .First()
                )
                .OrderBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return candidates
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
                    .OrderBy(c => PreferModelRank(c.Model.DeviceClass, c.Model.ModelType))
                    .ThenBy(c => c.Model.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (familyRepresentative is not null)
                {
                    return familyRepresentative;
                }

                skippedFixedOnlyFamilies.Add($"{group.Key.DeviceClass}:{group.Key.FamilyName}");
                return null;
            })
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderBy(candidate => candidate.Model.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int PreferModelRank(DeviceClass deviceClass, string? modelType)
    {
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return 20;
        }

        var normalized = modelType.Trim().ToLowerInvariant();
        return deviceClass switch
        {
            DeviceClass.Resistor when normalized == "r" => 0,
            DeviceClass.Capacitor when normalized == "c" => 0,
            DeviceClass.Diode when normalized == "d" => 0,
            _ => normalized switch
            {
                "model" => 1,
                "subckt" => 2,
                _ => 10,
            },
        };
    }

    private static bool RequiresPassiveCompatibilityCheck(ModelCandidate candidate)
    {
        return candidate.Category
                is PrimitiveCategory.Resistors
                    or PrimitiveCategory.Capacitors
                    or PrimitiveCategory.Diodes
            && IsSubcktModel(candidate.Model.ModelType);
    }

    private static bool IsPassiveWrapperCompatible(
        ModelCandidate candidate,
        IReadOnlyDictionary<string, SpiceSubcktOpPathResolver.SubcktDefinition> subcktDefinitions
    )
    {
        if (!subcktDefinitions.TryGetValue(candidate.Model.Name, out var definition))
        {
            return false;
        }

        var terminals = definition
            .Terminals.Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
        if (terminals.Length != 2)
        {
            return false;
        }

        var normalized = terminals.Select(t => t.ToLowerInvariant()).ToArray();
        if (normalized[0].Equals(normalized[1], StringComparison.Ordinal))
        {
            return false;
        }

        if (ContainsPowerTerminal(normalized))
        {
            return false;
        }

        if (candidate.Category == PrimitiveCategory.Diodes)
        {
            return LooksLikeDiodeTerminalPair(normalized);
        }

        return true;
    }

    private static bool ContainsPowerTerminal(IReadOnlyList<string> terminals)
    {
        for (var i = 0; i < terminals.Count; i++)
        {
            if (
                terminals[i]
                is "vdd"
                    or "vss"
                    or "gnd"
                    or "vcc"
                    or "vpwr"
                    or "vgnd"
                    or "vnb"
                    or "vpb"
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeDiodeTerminalPair(IReadOnlyList<string> terminals)
    {
        var first = terminals[0];
        var second = terminals[1];

        static bool IsAnodeLike(string name)
        {
            return name.Equals("a", StringComparison.Ordinal)
                || name.StartsWith("anode", StringComparison.Ordinal)
                || name.Equals("p", StringComparison.Ordinal)
                || name.Equals("plus", StringComparison.Ordinal);
        }

        static bool IsCathodeLike(string name)
        {
            return name.Equals("k", StringComparison.Ordinal)
                || name.StartsWith("cath", StringComparison.Ordinal)
                || name.Equals("n", StringComparison.Ordinal)
                || name.Equals("minus", StringComparison.Ordinal);
        }

        return (IsAnodeLike(first) && IsCathodeLike(second))
            || (IsAnodeLike(second) && IsCathodeLike(first));
    }

    private static Dictionary<PrimitiveCategory, StringBuilder> CreateCategoryBuilders(
        string libraryNamespace
    )
    {
        return new Dictionary<PrimitiveCategory, StringBuilder>
        {
            [PrimitiveCategory.Devices] = CreateCategoryBuilder(
                libraryNamespace,
                categorySuffix: "devices"
            ),
            [PrimitiveCategory.Resistors] = CreateCategoryBuilder(
                libraryNamespace,
                categorySuffix: "resistors"
            ),
            [PrimitiveCategory.Capacitors] = CreateCategoryBuilder(
                libraryNamespace,
                categorySuffix: "capacitors"
            ),
            [PrimitiveCategory.Diodes] = CreateCategoryBuilder(
                libraryNamespace,
                categorySuffix: "diodes"
            ),
        };
    }

    private static StringBuilder CreateCategoryBuilder(
        string libraryNamespace,
        string categorySuffix
    )
    {
        var builder = new StringBuilder();
        builder.AppendLine("VERSION 3.0");
        builder.AppendLine();
        builder.AppendLine($"library {libraryNamespace}.{categorySuffix}");
        builder.AppendLine();
        return builder;
    }

    private static void WriteCategoryFiles(
        string outputDirectory,
        IReadOnlyDictionary<PrimitiveCategory, StringBuilder> builders
    )
    {
        WriteAllTextAtomicWithRetry(
            Path.Combine(outputDirectory, PdkPrimitiveLibraryLayout.DevicesFileName),
            builders[PrimitiveCategory.Devices].ToString()
        );
        WriteAllTextAtomicWithRetry(
            Path.Combine(outputDirectory, PdkPrimitiveLibraryLayout.ResistorsFileName),
            builders[PrimitiveCategory.Resistors].ToString()
        );
        WriteAllTextAtomicWithRetry(
            Path.Combine(outputDirectory, PdkPrimitiveLibraryLayout.CapacitorsFileName),
            builders[PrimitiveCategory.Capacitors].ToString()
        );
        WriteAllTextAtomicWithRetry(
            Path.Combine(outputDirectory, PdkPrimitiveLibraryLayout.DiodesFileName),
            builders[PrimitiveCategory.Diodes].ToString()
        );
    }

    private static void WriteAllTextAtomicWithRetry(string path, string content)
    {
        const int maxAttempts = 40;
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? ".";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp"
            );
            try
            {
                File.WriteAllText(tempPath, content);
                File.Move(tempPath, fullPath, overwrite: true);
                return;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < maxAttempts)
            {
                TryDeleteFile(tempPath);
                Thread.Sleep(Math.Min(25 * attempt, 250));
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        throw new IOException(
            $"Failed to write '{fullPath}' after multiple retries due to file locking."
        );
    }

    private static bool IsSharingViolation(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is 32 or 33;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    private static void AppendPrimitive(
        StringBuilder builder,
        ModelCandidate candidate,
        string primitiveName,
        PdkDatabaseReader.GeometryRow? geom,
        IReadOnlyDictionary<string, SpiceSubcktOpPathResolver.SubcktDefinition> subcktDefinitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> subcktBodiesByName
    )
    {
        if (geom is not null)
        {
            builder.AppendLine(
                $"// geometry: W=[{FormatNullable(geom.WMin)}..{FormatNullable(geom.WMax)}] L=[{FormatNullable(geom.LMin)}..{FormatNullable(geom.LMax)}] M=[{FormatNullable(geom.NfMin)}..{FormatNullable(geom.NfMax)}]"
            );
        }

        builder.AppendLine(
            $"primitive {candidate.PrimitiveKind} {primitiveName}(size primSize) {{"
        );
        builder.AppendLine($"  device \"{candidate.Model.Name}\"");
        builder.AppendLine("  params {");

        switch (candidate.PrimitiveKind)
        {
            case "NMOS":
            case "PMOS":
                AppendMosParams(builder, candidate.Model, subcktDefinitions, subcktBodiesByName);
                break;
            case "Resistor":
                builder.AppendLine("    R = primSize.R");
                break;
            case "Capacitor":
                builder.AppendLine("    C = primSize.C");
                break;
            case "Diode":
                builder.AppendLine("    AREA = primSize.AREA");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported primitive kind '{candidate.PrimitiveKind}'."
                );
        }

        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendMosParams(
        StringBuilder builder,
        SpectreModel model,
        IReadOnlyDictionary<string, SpiceSubcktOpPathResolver.SubcktDefinition> subcktDefinitions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> subcktBodiesByName
    )
    {
        var isSubckt = IsSubcktModel(model.ModelType);
        if (isSubckt)
        {
            builder.AppendLine("    w = primSize.W");
            builder.AppendLine("    l = primSize.L");
            var multiplicityParam = ResolveSubcktMultiplicityParam(model.Name, subcktDefinitions);
            builder.AppendLine($"    {multiplicityParam} = primSize.M");

            var opSegments = SpiceSubcktOpPathResolver.TryResolveUniqueOpSegments(
                model.Name,
                subcktBodiesByName
            );
            if (opSegments is not null && opSegments.Count > 0)
            {
                for (var i = 0; i < opSegments.Count; i++)
                {
                    builder.AppendLine($"    __op_path{i} = {opSegments[i]}");
                }
            }

            return;
        }

        builder.AppendLine("    W = primSize.W");
        builder.AppendLine("    L = primSize.L");
        builder.AppendLine("    m = primSize.M");
    }

    private static string ResolveSubcktMultiplicityParam(
        string modelName,
        IReadOnlyDictionary<string, SpiceSubcktOpPathResolver.SubcktDefinition> subcktDefinitions
    )
    {
        if (
            !subcktDefinitions.TryGetValue(modelName, out var definition)
            || definition.ParameterNames.Count == 0
        )
        {
            return "mult";
        }

        if (HasParameter(definition.ParameterNames, "mult"))
        {
            return "mult";
        }

        if (HasParameter(definition.ParameterNames, "m"))
        {
            return "m";
        }

        if (HasParameter(definition.ParameterNames, "nf"))
        {
            return "nf";
        }

        return "mult";
    }

    private static bool HasParameter(IReadOnlyList<string> parameters, string name)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or NotSupportedException
                        or PathTooLongException
                        or IOException
            )
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

    private sealed record ModelCandidate(
        SpectreModel Model,
        string PrimitiveKind,
        PrimitiveCategory Category,
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

    private static string FormatNullable(double? value)
    {
        return value is null ? "-" : value.Value.ToString("G6", CultureInfo.InvariantCulture);
    }

    private static string FormatNullable(int? value)
    {
        return value is null ? "-" : value.Value.ToString(CultureInfo.InvariantCulture);
    }
}
