using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

public sealed record DeviceCharPlannerOptions(
    string Backend,
    string Corner,
    int Limit,
    DeviceFilterOptions Filters
)
{
    public static DeviceCharPlannerOptions Create(
        string backend,
        string corner,
        int limit,
        DeviceFilterOptions filters
    ) =>
        new(
            backend ?? "spectre",
            corner ?? "tt",
            Math.Max(0, limit),
            filters ?? DeviceFilterOptions.Empty
        );
}

public sealed record DeviceCharPlan(
    string DeviceName,
    string DeviceDisplayName,
    string ModelName,
    DeviceClass DeviceClass,
    bool IsSubckt,
    double Width,
    double Length,
    int Nf,
    double Vds,
    double VgsStop,
    double Vsb,
    string Backend,
    string Corner,
    IReadOnlyList<string> IncludePaths,
    IReadOnlyList<string> IncludePathsWithSection,
    IReadOnlyList<string> IncludePathsWithoutSection,
    string? Section
);

public static partial class DeviceCharPlanner
{
    public static IReadOnlyList<DeviceCharPlan> Plan(
        string dbPath,
        DeviceCharPlannerOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("PDK database not found", dbPath);

        var devices = PdkDatabaseReader.LoadDevices(dbPath);
        if (devices.Count == 0)
            return Array.Empty<DeviceCharPlan>();

        HashSet<string>? matchedKeys = null;
        if (options.Filters.Matched.HasValue)
        {
            matchedKeys = PdkDatabaseReader.LoadMatchedDeviceKeys(dbPath);
        }

        var bestMatch = PdkDatabaseReader.LoadBestMatchByDevice(dbPath);
        var models = PdkDatabaseReader
            .LoadModels(dbPath)
            .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

        var filteredDevices = devices
            .Where(d => DeviceFilterEvaluator.Matches(d, options.Filters, matchedKeys))
            .OrderBy(d => d.CanonicalName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Limit > 0 && filteredDevices.Count > options.Limit)
        {
            filteredDevices = filteredDevices.Take(options.Limit).ToList();
        }

        var plans = new List<DeviceCharPlan>(filteredDevices.Count);
        foreach (var device in filteredDevices)
        {
            var model = ResolveModelForDevice(device, bestMatch, models);
            if (model is null)
                continue;

            var includes = PdkIncludeResolver.ResolveModelIncludes(dbPath, model, options.Corner);
            var geometry = ResolveGeometry(dbPath, device.CanonicalName);
            var voltages = ResolveVoltages(model.VoltageDomain);

            plans.Add(
                new DeviceCharPlan(
                    DeviceName: device.CanonicalName,
                    DeviceDisplayName: device.DisplayName,
                    ModelName: model.Name,
                    DeviceClass: model.DeviceClass,
                    IsSubckt: string.Equals(
                        model.ModelType,
                        "subckt",
                        StringComparison.OrdinalIgnoreCase
                    ),
                    Width: geometry.Width,
                    Length: geometry.Length,
                    Nf: geometry.Nf,
                    Vds: voltages.Vds,
                    VgsStop: voltages.VgsStop,
                    Vsb: 0.0,
                    Backend: options.Backend,
                    Corner: options.Corner,
                    IncludePaths: includes.IncludePaths,
                    IncludePathsWithSection: includes.IncludePathsWithSection,
                    IncludePathsWithoutSection: includes.IncludePathsWithoutSection,
                    Section: includes.Section
                )
            );
        }

        return plans;
    }

    private static SpectreModel? ResolveModelForDevice(
        Device device,
        IReadOnlyDictionary<string, string> bestMatch,
        IReadOnlyDictionary<string, SpectreModel> models
    )
    {
        if (
            bestMatch.TryGetValue(device.CanonicalName, out var modelName)
            && models.TryGetValue(modelName, out var matched)
        )
        {
            return matched;
        }

        // If strict match failed, check if we should fall back to class-based matching.
        // For characterization, we prefer skipping over guessing wrong models.
        if (!bestMatch.TryGetValue(device.CanonicalName, out _))
        {
            // SKIP the aggressive class-based fallback that might pick any random model of the same class.
            // This prevents characterizing nfet_01v8 using nfet_g5v0d10v5 or nfet_native if the exact match is missing.
            return null;
        }

        return null;
    }

    private static List<SpectreModel> FilterByVdd(
        IEnumerable<SpectreModel> models,
        HashSet<string> vddTags
    )
    {
        if (vddTags.Count == 0)
            return models.ToList();

        var matchingConfig = PdkMatchingConfigManager.Load();
        var list = new List<SpectreModel>();
        foreach (var m in models)
        {
            var tok = VddFormatting.ExtractTokenFromVoltageDomain(m.VoltageDomain, matchingConfig);
            if (
                DeviceFilterEvaluator.TryNormalizeVddFilter(tok, out var normalized)
                && vddTags.Contains(normalized)
            )
            {
                list.Add(m);
            }
        }
        return list;
    }

    private static Geometry ResolveGeometry(string dbPath, string deviceCanonicalName)
    {
        const double defaultWidth = 1e-6;
        const double defaultLength = 0.18e-6;
        const int defaultNf = 1;

        var geom = PdkDatabaseReader.LoadGeometryForDevice(dbPath, deviceCanonicalName);
        if (geom is null)
        {
            return new Geometry(defaultWidth, defaultLength, defaultNf);
        }

        static double Clamp(double val, double? min, double? max)
        {
            if (min.HasValue && val < min.Value)
                val = min.Value;
            if (max.HasValue && val > max.Value)
                val = max.Value;
            return val;
        }

        var width = geom.WDefault ?? defaultWidth;
        var length = geom.LDefault ?? defaultLength;
        var nf = geom.NfDefault ?? defaultNf;

        width = Clamp(width, geom.WMin, geom.WMax);
        length = Clamp(length, geom.LMin, geom.LMax);
        if (nf <= 0)
            nf = defaultNf;

        return new Geometry(width, length, nf);
    }

    private static Voltages ResolveVoltages(string? voltageDomain)
    {
        double vdsVal = 0.9;
        double vgsStop = 1.2;

        if (!string.IsNullOrWhiteSpace(voltageDomain))
        {
            var vd = voltageDomain.Trim().ToLowerInvariant();
            var m = VoltageValuePattern().Match(vd);
            if (m.Success)
            {
                var nn = int.Parse(m.Groups["n"].Value);
                var ff = m.Groups["f"].Success ? int.Parse(m.Groups["f"].Value) : 0;
                var volts = nn + (ff > 0 ? ff / Math.Pow(10, m.Groups["f"].Value.Length) : 0.0);
                if (volts > 0)
                {
                    vdsVal = Math.Max(0.1, Math.Min(volts, volts * 0.6));
                    vgsStop = Math.Max(vgsStop, volts);
                }
            }
        }

        return new Voltages(vdsVal, vgsStop);
    }

    private sealed record Geometry(double Width, double Length, int Nf);

    private sealed record Voltages(double Vds, double VgsStop);

    [GeneratedRegex(@"(?<n>\d+)(?:\.(?<f>\d+))?v")]
    private static partial Regex VoltageValuePattern();
}
