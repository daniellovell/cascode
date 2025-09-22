using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cascode.Workspace;

public sealed class WorkspaceScanner
{
    private readonly CdsLibParser _cdsLibParser = new();
    private readonly CdsInitScanner _cdsInitScanner = new();
    private readonly SpectreDeckInspector _deckInspector = new();
    private readonly SpectreModelExtractor _modelExtractor = new();

    public WorkspaceScanResult Scan(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root must be provided", nameof(workspaceRoot));
        }

        var root = Path.GetFullPath(workspaceRoot);
        var warnings = new List<string>();

        var libraries = _cdsLibParser.Parse(root, warnings);

        var deckPaths = _cdsInitScanner.FindModelDecks(root, warnings);
        var deckRecords = new List<ModelDeckRecord>(deckPaths.Count);
        var models = new List<SpectreModel>();

        foreach (var deckPath in deckPaths)
        {
            var record = _deckInspector.Inspect(root, deckPath, warnings);
            var modelsForDeck = _modelExtractor.Extract(root, deckPath, warnings);
            record = record with { Models = modelsForDeck };
            deckRecords.Add(record);

            foreach (var model in modelsForDeck)
            {
                models.Add(model);
            }
        }

        var consolidated = ConsolidateModels(models);

        return new WorkspaceScanResult(root, libraries, deckRecords, consolidated, warnings);
    }

    private static IReadOnlyList<SpectreModel> ConsolidateModels(IEnumerable<SpectreModel> models)
    {
        var aggregator = new Dictionary<string, SpectreModelAggregator>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in models)
        {
            if (!aggregator.TryGetValue(model.Name, out var aggregate))
            {
                aggregate = new SpectreModelAggregator(model.Name);
                aggregator[model.Name] = aggregate;
            }

            aggregate.Add(model);
        }

        return aggregator.Values
            .Select(a => a.Build())
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class SpectreModelAggregator
    {
        private readonly HashSet<string> _modelTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<SpectreModelDeviceClass> _classes = new();
        private readonly HashSet<string> _voltageDomains = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _thresholdFlavors = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _corners = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _cornerDetails = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sections = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _decks = new(StringComparer.OrdinalIgnoreCase);

        public SpectreModelAggregator(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public void Add(SpectreModel model)
        {
            if (!string.IsNullOrWhiteSpace(model.ModelType))
            {
                _modelTypes.Add(model.ModelType);
            }

            if (model.DeviceClass != SpectreModelDeviceClass.Unknown)
            {
                _classes.Add(model.DeviceClass);
            }

            if (!string.IsNullOrWhiteSpace(model.VoltageDomain))
            {
                _voltageDomains.Add(model.VoltageDomain);
            }

            if (!string.IsNullOrWhiteSpace(model.ThresholdFlavor))
            {
                _thresholdFlavors.Add(model.ThresholdFlavor);
            }

            foreach (var corner in model.Corners)
            {
                _corners.Add(corner);
            }

            foreach (var detail in model.CornerDetails)
            {
                _cornerDetails.Add(detail);
            }

            foreach (var section in model.Sections)
            {
                _sections.Add(section);
            }

            foreach (var source in model.SourceFiles)
            {
                _sources.Add(source);
            }

            foreach (var deck in model.Decks)
            {
                _decks.Add(deck);
            }
        }

        public SpectreModel Build()
        {
            var modelType = _modelTypes.FirstOrDefault() ?? string.Empty;
            var deviceClass = _classes.FirstOrDefault();
            var voltageDomain = _voltageDomains.FirstOrDefault();
            var threshold = _thresholdFlavors.FirstOrDefault();

            var corners = _corners.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var cornerDetails = _cornerDetails.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var sections = _sections.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var sources = _sources.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var decks = _decks.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();

            return new SpectreModel(
                Name,
                modelType,
                deviceClass,
                voltageDomain,
                threshold,
                corners.Length == 0 ? SpectreModel.EmptyStringList : corners,
                cornerDetails.Length == 0 ? SpectreModel.EmptyStringList : cornerDetails,
                sections.Length == 0 ? SpectreModel.EmptyStringList : sections,
                sources.Length == 0 ? SpectreModel.EmptyStringList : sources,
                decks.Length == 0 ? SpectreModel.EmptyStringList : decks);
        }
    }
}
