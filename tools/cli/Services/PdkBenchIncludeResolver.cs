using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cascode.ACIR;
using Cascode.Bench;
using Cascode.Workspace;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Services;

internal sealed class PdkBenchIncludeResolver : IBenchIncludeResolver
{
    private const string DefaultCorner = "tt";
    private readonly string _dbPath;
    private readonly ILogger? _logger;
    private readonly string? _corner;
    private IReadOnlyDictionary<string, SpectreModel>? _models;
    private bool _loggedMissingDb;

    public static PdkBenchIncludeResolver Create(string pdkRoot, ILogger? logger)
    {
        var corner = Environment.GetEnvironmentVariable("CASCODE_PDK_CORNER");
        if (string.IsNullOrWhiteSpace(corner))
        {
            corner = DefaultCorner;
        }

        return new PdkBenchIncludeResolver(pdkRoot, logger, corner);
    }

    public PdkBenchIncludeResolver(string pdkRoot, ILogger? logger, string? corner)
    {
        _dbPath = WorkspacePaths.GetDatabasePath(pdkRoot);
        _logger = logger;
        _corner = string.IsNullOrWhiteSpace(corner) ? null : corner;
    }

    public BenchIncludeResolution Resolve(Circuit circuit, BenchBackendType backend)
    {
        var pdkDevices =
            circuit
                .Fill?.Devices.Select(d => d.PdkDevice)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            ?? Array.Empty<string>();

        if (pdkDevices.Length == 0)
        {
            return new BenchIncludeResolution(Array.Empty<string>(), Array.Empty<string>(), null);
        }

        if (!File.Exists(_dbPath))
        {
            if (!_loggedMissingDb)
            {
                _logger?.LogWarning(
                    "No PDK database found at {Path}. Run 'pdk scan' to enable PDK-backed includes.",
                    _dbPath
                );
                _loggedMissingDb = true;
            }
            return new BenchIncludeResolution(Array.Empty<string>(), Array.Empty<string>(), null);
        }

        _models ??= PdkDatabaseReader
            .LoadModels(_dbPath)
            .ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

        if (_models.Count == 0)
        {
            _logger?.LogWarning("PDK database contains no models; skipping include resolution.");
            return new BenchIncludeResolution(Array.Empty<string>(), Array.Empty<string>(), null);
        }

        var matches = DeviceModelMatcher.Match(
            pdkDevices.Select(BuildDevice).ToList(),
            _models.Values.ToList()
        );

        var bestMatches = matches
            .Where(m => m.Rank == 0)
            .GroupBy(m => m.DeviceCanonicalName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(
                m => m.DeviceCanonicalName,
                m => m.ModelName,
                StringComparer.OrdinalIgnoreCase
            );

        var includesWithSection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includesWithoutSection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deviceModelMap = new Dictionary<string, DeviceModelResolution>(
            StringComparer.OrdinalIgnoreCase
        );
        string? resolvedSection = null;

        foreach (var device in pdkDevices)
        {
            if (!bestMatches.TryGetValue(device, out var modelName))
            {
                _logger?.LogWarning("No model match found for PDK device '{Device}'.", device);
                continue;
            }

            if (!_models.TryGetValue(modelName, out var model))
            {
                _logger?.LogWarning("Model '{Model}' not found in PDK database.", modelName);
                continue;
            }

            deviceModelMap[device] = new DeviceModelResolution(
                modelName,
                string.Equals(model.ModelType, "subckt", StringComparison.OrdinalIgnoreCase)
            );

            var includeSet = PdkIncludeResolver.ResolveModelIncludes(_dbPath, model, _corner);
            foreach (var path in includeSet.IncludePathsWithSection)
                includesWithSection.Add(path);
            foreach (var path in includeSet.IncludePathsWithoutSection)
                includesWithoutSection.Add(path);

            if (!string.IsNullOrWhiteSpace(includeSet.Section))
            {
                if (resolvedSection is null)
                {
                    resolvedSection = includeSet.Section;
                }
                else if (
                    !resolvedSection.Equals(includeSet.Section, StringComparison.OrdinalIgnoreCase)
                )
                {
                    _logger?.LogWarning(
                        "Multiple PDK sections resolved ({First}, {Second}). Using {Chosen}.",
                        resolvedSection,
                        includeSet.Section,
                        resolvedSection
                    );
                }
            }
        }

        return new BenchIncludeResolution(
            includesWithSection.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
            includesWithoutSection.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
            resolvedSection
        )
        {
            DeviceModelMap = deviceModelMap,
        };
    }

    private static Device BuildDevice(string name)
    {
        return new Device
        {
            CellName = name,
            Class = DeviceClass.Unknown,
            Subclass = DeviceSubclass.Unknown,
            HasLayout = true,
            HasSymbol = true,
            VtTags = Array.Empty<string>(),
            VddTags = Array.Empty<string>(),
            Tags = Array.Empty<string>(),
        };
    }
}
