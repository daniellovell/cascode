using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cascode.Workspace;

internal sealed class SpectreModelExtractor
{
    private readonly struct CornerInfo
    {
        public CornerInfo(string original, string primary, string? detail)
        {
            Original = original;
            Primary = primary;
            Detail = detail;
        }

        public string Original { get; }
        public string Primary { get; }
        public string? Detail { get; }
    }

    private enum CornerFrameType
    {
        LibBlock,
        SectionBlock,
        IncludeSection
    }

    private readonly struct CornerFrame
    {
        public CornerFrame(CornerFrameType type, CornerInfo info)
        {
            Type = type;
            Info = info;
        }

        public CornerFrameType Type { get; }
        public CornerInfo Info { get; }
    }

    private readonly struct VisitKey : IEquatable<VisitKey>
    {
        public VisitKey(string path, string? section)
        {
            Path = path;
            Section = section ?? string.Empty;
        }

        public string Path { get; }
        public string Section { get; }

        public bool Equals(VisitKey other)
        {
            return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Section, other.Section, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
            => obj is VisitKey other && Equals(other);

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Path) * 397
                ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Section);
        }
    }

    private sealed class SpectreModelBuilder
    {
        private readonly HashSet<string> _corners = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _cornerDetails = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sections = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _decks = new(StringComparer.OrdinalIgnoreCase);

        public SpectreModelBuilder(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public string? ModelType { get; private set; }
        public DeviceClass DeviceClass { get; private set; } = DeviceClass.Unknown;
        public string? VoltageDomain { get; private set; }
        public string? ThresholdFlavor { get; private set; }

        public void SetModelType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ModelType))
            {
                ModelType = type;
            }
        }

        public void SetDeviceClass(DeviceClass deviceClass)
        {
            if (deviceClass == DeviceClass.Unknown)
            {
                return;
            }

            if (DeviceClass == DeviceClass.Unknown)
            {
                DeviceClass = deviceClass;
            }
        }

        public void SetVoltageDomain(string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain) || !string.IsNullOrWhiteSpace(VoltageDomain))
            {
                return;
            }

            VoltageDomain = domain;
        }

        public void SetThresholdFlavor(string? flavor)
        {
            if (string.IsNullOrWhiteSpace(flavor) || !string.IsNullOrWhiteSpace(ThresholdFlavor))
            {
                return;
            }

            ThresholdFlavor = flavor;
        }

        public void AddContext(CornerInfo info)
        {
            if (!string.IsNullOrWhiteSpace(info.Primary))
            {
                _corners.Add(info.Primary);
            }

            if (!string.IsNullOrWhiteSpace(info.Detail))
            {
                _cornerDetails.Add(info.Detail);
            }

            if (!string.IsNullOrWhiteSpace(info.Original))
            {
                _sections.Add(info.Original);
            }
        }

        public void AddSectionName(string section)
        {
            if (!string.IsNullOrWhiteSpace(section))
            {
                _sections.Add(section);
            }
        }

        public void AddSource(string source)
        {
            if (!string.IsNullOrWhiteSpace(source))
            {
                _sources.Add(source);
            }
        }

        public void AddDeck(string deck)
        {
            if (!string.IsNullOrWhiteSpace(deck))
            {
                _decks.Add(deck);
            }
        }

        public SpectreModel Build()
        {
            var corners = _corners.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var cornerDetails = _cornerDetails.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var sections = _sections.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var sources = _sources.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();
            var decks = _decks.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();

            return new SpectreModel(
                Name,
                ModelType ?? string.Empty,
                DeviceClass,
                VoltageDomain,
                ThresholdFlavor,
                corners,
                cornerDetails,
                sections,
                sources,
                decks);
        }
    }

    private sealed class SectionContext
    {
        public SectionContext(string? normalizedName, bool framePushed)
        {
            NormalizedName = normalizedName;
            FramePushed = framePushed;
        }

        public string? NormalizedName { get; }
        public bool FramePushed { get; }
    }

    private static readonly Regex IncludeDirectiveRegex = new(@"^(\.include|include)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LibDirectiveRegex = new(@"^\.lib\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EndLibDirectiveRegex = new(@"^\.endl\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SectionDirectiveRegex = new(@"^section\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EndSectionDirectiveRegex = new(@"^endsection\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ModelDirectiveRegex = new(@"^\.?model\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SubcktDirectiveRegex = new(@"^\.?subckt\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // Matches common bin-model naming variants at end of model name:
    //  - *_model(.N)
    //  - *__model(.N)
    //  - *_model_base(.N)
    //  - *__model_base(.N)
    //  - *__base(.N)
    private static readonly Regex BinModelNameRegex = new(
        @"(?:__|_)(?:model(?:_base)?|base)(?:\.\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public IReadOnlyList<SpectreModel> Extract(string workspaceRoot, string deckPath, ICollection<string>? warnings = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root must be provided", nameof(workspaceRoot));
        }

        if (string.IsNullOrWhiteSpace(deckPath))
        {
            throw new ArgumentException("Deck path must be provided", nameof(deckPath));
        }

        var builders = new Dictionary<string, SpectreModelBuilder>(StringComparer.OrdinalIgnoreCase);
        var frames = new Stack<CornerFrame>();
        var visited = new HashSet<VisitKey>();

        VisitFile(
            workspaceRoot,
            deckPath,
            deckPath,
            includeFrame: null,
            includeSectionFilter: null,
            frames,
            builders,
            visited,
            warnings);

        var built = builders.Values
            .Select(b => b.Build())
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Filter out bin .model entries (e.g., *_model.0, *_model.1 …). Keep subckts and non-bin models.
        var filtered = built.Where(m => string.Equals(m.ModelType, "subckt", StringComparison.OrdinalIgnoreCase)
            || !IsBinModelName(m.Name)).ToArray();

        return filtered;
    }

    private void VisitFile(
        string workspaceRoot,
        string filePath,
        string deckPath,
        CornerFrame? includeFrame,
        string? includeSectionFilter,
        Stack<CornerFrame> frames,
        Dictionary<string, SpectreModelBuilder> builders,
        HashSet<VisitKey> visited,
        ICollection<string>? warnings)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var visitKey = new VisitKey(normalizedPath, includeSectionFilter);

        if (!visited.Add(visitKey))
        {
            return;
        }

        if (!File.Exists(normalizedPath))
        {
            warnings?.Add($"Model include '{normalizedPath}' does not exist.");
            return;
        }

        if (includeFrame.HasValue)
        {
            frames.Push(includeFrame.Value);
        }

        var directory = Path.GetDirectoryName(normalizedPath) ?? Directory.GetCurrentDirectory();
        var sectionStack = new Stack<SectionContext>();

        foreach (var rawLine in File.ReadLines(normalizedPath))
        {
            var line = NormalizeLine(rawLine);
            if (line is null)
            {
                continue;
            }

            if (LibDirectiveRegex.IsMatch(line))
            {
                HandleLibDirective(line, workspaceRoot, directory, deckPath, frames, builders, visited, warnings);
                continue;
            }

            if (EndLibDirectiveRegex.IsMatch(line))
            {
                PopFrame(frames, CornerFrameType.LibBlock);
                continue;
            }

            if (SectionDirectiveRegex.IsMatch(line))
            {
                var label = ExtractDirectiveArgument(line.Substring("section".Length));
                var info = ParseCornerInfo(label);
                var normalized = NormalizeIdentifier(label);
                var framePushed = false;
                if (info is not null)
                {
                    frames.Push(new CornerFrame(CornerFrameType.SectionBlock, info.Value));
                    framePushed = true;
                }

                sectionStack.Push(new SectionContext(normalized, framePushed));
                continue;
            }

            if (EndSectionDirectiveRegex.IsMatch(line))
            {
                if (sectionStack.Count > 0)
                {
                    var context = sectionStack.Pop();
                    if (context.FramePushed)
                    {
                        PopFrame(frames, CornerFrameType.SectionBlock);
                    }
                }
                continue;
            }

            if (IncludeDirectiveRegex.IsMatch(line))
            {
                HandleIncludeDirective(
                    line,
                    workspaceRoot,
                    directory,
                    deckPath,
                    frames,
                    builders,
                    visited,
                    warnings);
                continue;
            }

            if (SubcktDirectiveRegex.IsMatch(line))
            {
                var builder = ProcessSubcktDirective(line, normalizedPath, deckPath, builders, frames);
                if (builder is not null)
                {
                    foreach (var context in sectionStack)
                    {
                        if (!string.IsNullOrWhiteSpace(context.NormalizedName))
                        {
                            builder.AddSectionName(context.NormalizedName);
                        }
                    }
                }
                continue;
            }

            if (ModelDirectiveRegex.IsMatch(line))
            {
                if (ShouldSkipForSectionFilter(includeSectionFilter, sectionStack))
                {
                    continue;
                }

                var builder = ProcessModelDirective(line, normalizedPath, deckPath, builders, frames);
                if (builder is not null)
                {
                    foreach (var context in sectionStack)
                    {
                        if (!string.IsNullOrWhiteSpace(context.NormalizedName))
                        {
                            builder.AddSectionName(context.NormalizedName);
                        }
                    }
                }
            }
        }

        if (includeFrame.HasValue)
        {
            PopFrame(frames, includeFrame.Value.Type);
        }
    }

    private void HandleLibDirective(
        string line,
        string workspaceRoot,
        string directory,
        string deckPath,
        Stack<CornerFrame> frames,
        Dictionary<string, SpectreModelBuilder> builders,
        HashSet<VisitKey> visited,
        ICollection<string>? warnings)
    {
        var args = SplitArguments(line.Substring(4));
        if (args.Count == 0)
        {
            return;
        }

        if (args.Count >= 2 && LooksLikePath(args[0]))
        {
            var path = PathUtilities.NormalizeWorkspacePath(args[0], workspaceRoot, directory);
            var cornerArg = args[1];
            var cornerInfo = ParseCornerInfo(cornerArg);
            CornerFrame? includeFrame = null;
            string? filter = null;
            if (cornerInfo is not null)
            {
                includeFrame = new CornerFrame(CornerFrameType.IncludeSection, cornerInfo.Value);
                filter = cornerInfo.Value.Original;
            }

            if (path is not null)
            {
                VisitFile(
                    workspaceRoot,
                    path,
                    deckPath,
                    includeFrame,
                    NormalizeIdentifier(filter),
                    frames,
                    builders,
                    visited,
                    warnings);
            }

            return;
        }

        var libInfo = ParseCornerInfo(args[0]);
        if (libInfo is not null)
        {
            frames.Push(new CornerFrame(CornerFrameType.LibBlock, libInfo.Value));
        }
    }

    private void HandleIncludeDirective(
        string line,
        string workspaceRoot,
        string directory,
        string deckPath,
        Stack<CornerFrame> frames,
        Dictionary<string, SpectreModelBuilder> builders,
        HashSet<VisitKey> visited,
        ICollection<string>? warnings)
    {
        var args = SplitArguments(RemoveDirectiveKeyword(line));
        if (args.Count == 0)
        {
            return;
        }

        var pathToken = args[0];
        string? section = null;

        for (var i = 1; i < args.Count; i++)
        {
            var token = args[i];
            if (token.StartsWith("section", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractAssignmentValue(token);
                if (string.IsNullOrWhiteSpace(value) && i + 1 < args.Count)
                {
                    value = args[++i];
                }

                section ??= value;
            }
        }

        var includePath = PathUtilities.NormalizeWorkspacePath(pathToken, workspaceRoot, directory);
        if (includePath is null)
        {
            warnings?.Add($"Unable to resolve include path '{pathToken}'.");
            return;
        }

        CornerFrame? includeFrame = null;
        string? sectionFilter = null;
        if (!string.IsNullOrWhiteSpace(section))
        {
            var info = ParseCornerInfo(section);
            if (info is not null)
            {
                includeFrame = new CornerFrame(CornerFrameType.IncludeSection, info.Value);
                sectionFilter = info.Value.Original;
            }
        }

        VisitFile(
            workspaceRoot,
            includePath,
            deckPath,
            includeFrame,
            NormalizeIdentifier(sectionFilter),
            frames,
            builders,
            visited,
            warnings);
    }

    private static SpectreModelBuilder? ProcessModelDirective(
        string line,
        string sourcePath,
        string deckPath,
        Dictionary<string, SpectreModelBuilder> builders,
        IEnumerable<CornerFrame> frames)
    {
        var args = SplitArguments(line.Substring(line.IndexOf("model", StringComparison.OrdinalIgnoreCase) + 5));
        if (args.Count < 2)
        {
            return null;
        }

        var name = args[0];
        var typeToken = args[1];
        var modelType = typeToken;
        var deviceClass = ClassifyModelType(typeToken);
        var voltageDomain = InferVoltageDomain(name);
        var thresholdFlavor = InferThresholdFlavor(name);

        if (!builders.TryGetValue(name, out var builder))
        {
            builder = new SpectreModelBuilder(name);
            builders[name] = builder;
        }

        builder.SetModelType(modelType);
        builder.SetDeviceClass(deviceClass);
        builder.SetVoltageDomain(voltageDomain);
        builder.SetThresholdFlavor(thresholdFlavor);
        builder.AddSource(sourcePath);
        builder.AddDeck(deckPath);

        foreach (var frame in frames.Reverse())
        {
            builder.AddContext(frame.Info);
        }

        return builder;
    }

    private static SpectreModelBuilder? ProcessSubcktDirective(
        string line,
        string sourcePath,
        string deckPath,
        Dictionary<string, SpectreModelBuilder> builders,
        IEnumerable<CornerFrame> frames)
    {
        var idx = line.IndexOf("subckt", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var args = SplitArguments(line[(idx + 6)..]);
        if (args.Count == 0)
        {
            return null;
        }

        var name = args[0].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!builders.TryGetValue(name, out var builder))
        {
            builder = new SpectreModelBuilder(name);
            builders[name] = builder;
        }

        builder.SetModelType("subckt");
        builder.SetDeviceClass(NameNormalization.ClassifyByName(name));
        builder.SetVoltageDomain(InferVoltageDomain(name));
        builder.SetThresholdFlavor(InferThresholdFlavor(name));
        builder.AddSource(sourcePath);
        builder.AddDeck(deckPath);

        foreach (var frame in frames.Reverse())
        {
            builder.AddContext(frame.Info);
        }

        return builder;
    }

    private static DeviceClass ClassifyModelType(string typeToken)
    {
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            return DeviceClass.Unknown;
        }

        var token = typeToken.Trim().ToLowerInvariant();

        if (token.Contains("nmos") || token.Contains("nfet"))
        {
            return DeviceClass.Nmos;
        }

        if (token.Contains("pmos") || token.Contains("pfet"))
        {
            return DeviceClass.Pmos;
        }

        if (token.Contains("npn") || token.Contains("pnp") || token.Contains("bjt"))
        {
            return DeviceClass.Bipolar;
        }

        if (token.Equals("d", StringComparison.OrdinalIgnoreCase) || token.Contains("diode"))
        {
            return DeviceClass.Diode;
        }

        if (token.Equals("r", StringComparison.OrdinalIgnoreCase) || token.Contains("res"))
        {
            return DeviceClass.Resistor;
        }

        if (token.Equals("c", StringComparison.OrdinalIgnoreCase) || token.Contains("cap"))
        {
            return DeviceClass.Capacitor;
        }

        if (token.Contains("ind"))
        {
            return DeviceClass.Inductor;
        }

        if (token.Contains("moscap"))
        {
            return DeviceClass.Moscap;
        }

        if (token.Contains("tline") || token.Contains("transmission"))
        {
            return DeviceClass.TransmissionLine;
        }

        return DeviceClass.Other;
    }

    private static bool IsBinModelName(string name)
        => !string.IsNullOrWhiteSpace(name) && BinModelNameRegex.IsMatch(name.Trim());

    private static string? InferVoltageDomain(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var match = Regex.Match(name, @"(?<voltage>\d+)v(?<frac>\d+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var num = match.Groups["voltage"].Value.TrimStart('0');
        if (num.Length == 0)
        {
            num = "0";
        }

        var frac = match.Groups["frac"].Value.TrimEnd('0');

        var builder = new StringBuilder();
        builder.Append(num);
        if (frac.Length > 0)
        {
            builder.Append('.');
            builder.Append(frac);
        }
        builder.Append('V');

        return builder.ToString();
    }

    private static string? InferThresholdFlavor(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var lower = name.ToLowerInvariant();
        var flavors = new[] { "ulvt", "llvt", "slvt", "lvt", "rvt", "svt", "nvt", "hvt", "mvt" };

        foreach (var flavor in flavors)
        {
            if (lower.Contains("_" + flavor) || lower.EndsWith(flavor, StringComparison.Ordinal))
            {
                return flavor.ToUpperInvariant();
            }
        }

        return null;
    }

    private static string? NormalizeLine(string? rawLine)
    {
        if (string.IsNullOrEmpty(rawLine))
        {
            return null;
        }

        var trimmed = rawLine.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

    private static string RemoveDirectiveKeyword(string line)
    {
        var match = IncludeDirectiveRegex.Match(line);
        if (!match.Success)
        {
            return line;
        }

        return line.Substring(match.Length).TrimStart();
    }

    private static IReadOnlyList<string> SplitArguments(string input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var span = input.AsSpan();
        var builder = new StringBuilder();
        var inQuote = false;
        char quoteChar = '\0';

        foreach (var c in span)
        {
            if (inQuote)
            {
                if (c == quoteChar)
                {
                    inQuote = false;
                }
                else if (c == '\\')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append(c);
                }
            }
            else
            {
                if (char.IsWhiteSpace(c))
                {
                    FlushToken(builder, result);
                }
                else if (c is '\'' or '"')
                {
                    inQuote = true;
                    quoteChar = c;
                }
                else
                {
                    builder.Append(c);
                }
            }
        }

        FlushToken(builder, result);
        return result;
    }

    private static void FlushToken(StringBuilder builder, ICollection<string> tokens)
    {
        if (builder.Length == 0)
        {
            return;
        }

        tokens.Add(builder.ToString());
        builder.Clear();
    }

    private static CornerInfo? ParseCornerInfo(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var trimmed = label.Trim().Trim('"', '\'');
        if (trimmed.Length == 0)
        {
            return null;
        }

        var normalized = trimmed.ToLowerInvariant();
        var primary = normalized;
        string? detail = null;

        var underscoreIndex = normalized.IndexOf('_');
        if (underscoreIndex > 0)
        {
            primary = normalized[..underscoreIndex];
            var detailPart = normalized[(underscoreIndex + 1)..];
            detail = string.IsNullOrWhiteSpace(detailPart) ? null : detailPart;
        }
        else
        {
            var dashIndex = normalized.IndexOf('-');
            if (dashIndex > 0)
            {
                primary = normalized[..dashIndex];
                var detailPart = normalized[(dashIndex + 1)..];
                detail = string.IsNullOrWhiteSpace(detailPart) ? null : detailPart;
            }
        }

        return new CornerInfo(trimmed, primary, detail);
    }

    private static void PopFrame(Stack<CornerFrame> frames, CornerFrameType type)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var stack = frames.ToArray();
        foreach (var frame in stack)
        {
            if (frame.Type == type)
            {
                while (frames.Count > 0)
                {
                    var popped = frames.Pop();
                    if (popped.Type == type)
                    {
                        break;
                    }
                }

                break;
            }
        }
    }

    private static string ExtractDirectiveArgument(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..].Trim();
        }

        if (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
    }

    private static string? ExtractAssignmentValue(string token)
    {
        var index = token.IndexOf('=');
        if (index < 0)
        {
            return null;
        }

        return token[(index + 1)..].Trim().Trim('"', '\'');
    }

    private static bool ShouldSkipForSectionFilter(string? includeSectionFilter, Stack<SectionContext> sectionStack)
    {
        if (string.IsNullOrWhiteSpace(includeSectionFilter))
        {
            return false;
        }

        if (sectionStack.Count == 0)
        {
            return false;
        }

        foreach (var context in sectionStack)
        {
            if (string.Equals(context.NormalizedName, includeSectionFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Trim('"', '\'').ToLowerInvariant();
    }

    private static bool LooksLikePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return input.Contains('/') || input.Contains('\\') || input.Contains('.');
    }
}
