using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Parses ACIR text format into ACIRDocument objects.
/// </summary>
/// <remarks>
/// The reader supports the ACIR text format as specified in Ch03_ACIR.md.
/// It handles bundle definitions, circuit declarations at all levels (HL, ML, EL),
/// and all circuit sections including fill, harness, benches, and provenance.
/// </remarks>
public static partial class ACIRReader
{
    /// <summary>
    /// Reads an ACIR document from a text reader. Throws on parse errors.
    /// </summary>
    /// <param name="reader">Text reader containing ACIR content.</param>
    /// <param name="filePath">Optional file path for error messages.</param>
    /// <returns>Parsed ACIR document.</returns>
    /// <exception cref="ACIRParseException">Thrown when parsing fails.</exception>
    /// <remarks>
    /// For structured error handling, use <see cref="TryRead"/> instead.
    /// </remarks>
    public static ACIRDocument Read(TextReader reader, string filePath = "<unknown>")
    {
        var result = TryRead(reader, filePath);
        if (!result.Success)
        {
            var errors = result
                .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            var message = errors.Count > 0 ? errors[0].Message : "Unknown parse error";
            throw new ACIRParseException(message, result.Diagnostics);
        }
        return result.Document!;
    }

    /// <summary>
    /// Parses ACIR text content from a string. Throws on parse errors.
    /// </summary>
    /// <param name="content">ACIR text content.</param>
    /// <param name="filePath">Optional file path for error messages.</param>
    /// <returns>Parsed ACIR document.</returns>
    /// <exception cref="ACIRParseException">Thrown when parsing fails.</exception>
    /// <remarks>
    /// For structured error handling, use <see cref="TryParse"/> instead.
    /// </remarks>
    public static ACIRDocument Parse(string content, string filePath = "<unknown>")
    {
        var result = TryParse(content, filePath);
        if (!result.Success)
        {
            var errors = result
                .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            var message = errors.Count > 0 ? errors[0].Message : "Unknown parse error";
            throw new ACIRParseException(message, result.Diagnostics);
        }
        return result.Document!;
    }

    /// <summary>
    /// Reads an ACIR document from a text reader with structured error handling.
    /// </summary>
    /// <param name="reader">Text reader containing ACIR content.</param>
    /// <param name="filePath">Path of the source file for diagnostics.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    /// <remarks>
    /// This method follows the same pattern as <see cref="Cascode.Compiler.SimpleCascodeCompiler"/>
    /// by returning structured diagnostics instead of throwing exceptions.
    /// </remarks>
    public static ACIRReadResult TryRead(TextReader reader, string filePath = "<unknown>")
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lines.Add(line);
            }

            var doc = ParseWithDiagnostics(lines, filePath, diagnostics);
            return new ACIRReadResult { Document = doc, Diagnostics = diagnostics };
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0001: Failed to parse ACIR: {ex.Message}",
                    DiagnosticSeverity.Error,
                    filePath,
                    1,
                    1
                )
            );
            return new ACIRReadResult { Document = null, Diagnostics = diagnostics };
        }
    }

    /// <summary>
    /// Parses ACIR text content from a string with structured error handling.
    /// </summary>
    /// <param name="content">ACIR text content.</param>
    /// <param name="filePath">Path of the source file for diagnostics.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    public static ACIRReadResult TryParse(string content, string filePath = "<unknown>")
    {
        using var reader = new StringReader(content);
        return TryRead(reader, filePath);
    }

    /// <summary>
    /// Internal parser that processes ACIR lines into a document.
    /// </summary>
    /// <param name="lines">Lines of ACIR text.</param>
    /// <returns>Parsed ACIR document.</returns>
    private static ACIRDocument Parse(IReadOnlyList<string> lines)
    {
        var doc = new ACIRDocument();
        var i = 0;

        // Skip empty lines and comments
        while (i < lines.Count && IsEmptyOrCommentLine(lines[i]))
            i++;

        // Version line
        if (i < lines.Count && lines[i].StartsWith("ACIR"))
        {
            var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var versionStr = parts[1];
                var versionParts = versionStr.Split('.');
                if (
                    versionParts.Length == 2
                    && int.TryParse(versionParts[0], out var major)
                    && int.TryParse(versionParts[1], out var minor)
                )
                {
                    doc = new ACIRDocument { VersionMajor = major, VersionMinor = minor };
                }
            }
            i++;
        }

        // Parse remaining content
        while (i < lines.Count)
        {
            var line = lines[i].TrimEnd();
            if (IsEmptyOrCommentLine(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("bundle "))
            {
                i = ParseBundle(lines, i, doc.BundleTypes);
            }
            else if (line.StartsWith("trait "))
            {
                i = ParseTrait(lines, i, doc.Traits);
            }
            else if (line.StartsWith("circuit "))
            {
                i = ParseCircuit(lines, i, doc.Circuits);
            }
            else
            {
                i++;
            }
        }

        return doc;
    }

    /// <summary>
    /// Internal parser that processes ACIR lines into a document with diagnostic collection.
    /// </summary>
    /// <param name="lines">Lines of ACIR text.</param>
    /// <param name="filePath">Source file path for diagnostics.</param>
    /// <param name="diagnostics">List to collect diagnostics.</param>
    /// <returns>Parsed ACIR document.</returns>
    private static ACIRDocument ParseWithDiagnostics(
        IReadOnlyList<string> lines,
        string filePath,
        List<Diagnostic> diagnostics
    )
    {
        var doc = new ACIRDocument();
        var i = 0;

        // Skip empty lines and comments
        while (i < lines.Count && IsEmptyOrCommentLine(lines[i]))
            i++;

        // Version line
        if (i < lines.Count && lines[i].StartsWith("ACIR"))
        {
            var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var versionStr = parts[1];
                var versionParts = versionStr.Split('.');
                if (
                    versionParts.Length == 2
                    && int.TryParse(versionParts[0], out var major)
                    && int.TryParse(versionParts[1], out var minor)
                )
                {
                    doc = new ACIRDocument { VersionMajor = major, VersionMinor = minor };
                    if (major != ACIRVersion.Major)
                    {
                        diagnostics.Add(
                            new Diagnostic(
                                $"ACIR0007: ACIR major version {major} not supported. Expected major version {ACIRVersion.Major}.",
                                DiagnosticSeverity.Error,
                                filePath,
                                i + 1,
                                1
                            )
                        );
                    }
                    // Minor version mismatch is OK - no error
                }
                else
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"ACIR0002: Invalid version declaration '{lines[i].Trim()}' - expected 'ACIR MAJOR.MINOR'",
                            DiagnosticSeverity.Error,
                            filePath,
                            i + 1,
                            1
                        )
                    );
                }
            }
            i++;
        }
        else if (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0002: Missing version declaration - expected 'ACIR {ACIRVersion.Current}' at start of file",
                    DiagnosticSeverity.Warning,
                    filePath,
                    1,
                    1
                )
            );
        }

        // Parse remaining content
        while (i < lines.Count)
        {
            var line = lines[i].TrimEnd();
            if (IsEmptyOrCommentLine(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("bundle "))
            {
                i = ParseBundleWithDiagnostics(lines, i, doc.BundleTypes, filePath, diagnostics);
            }
            else if (line.StartsWith("trait "))
            {
                i = ParseTraitWithDiagnostics(lines, i, doc.Traits, filePath, diagnostics);
            }
            else if (line.StartsWith("circuit "))
            {
                i = ParseCircuitWithDiagnostics(lines, i, doc.Circuits, filePath, diagnostics);
            }
            else
            {
                i++;
            }
        }

        return doc;
    }

    /// <summary>
    /// Parses a bundle type definition with diagnostic collection.
    /// </summary>
    private static int ParseBundleWithDiagnostics(
        IReadOnlyList<string> lines,
        int start,
        List<BundleType> bundles,
        string filePath,
        List<Diagnostic> diagnostics
    )
    {
        var line = lines[start].Trim();
        var match = BundleDeclarationPattern().Match(line);
        if (!match.Success)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0003: Malformed bundle declaration '{line}'",
                    DiagnosticSeverity.Error,
                    filePath,
                    start + 1,
                    1
                )
            );
            return start + 1;
        }

        var bundle = new BundleType { Name = match.Groups[1].Value };
        var i = start + 1;

        while (i < lines.Count)
        {
            var fieldLine = lines[i];
            if (!fieldLine.StartsWith("  ") || string.IsNullOrWhiteSpace(fieldLine))
                break;

            var fieldMatch = BundleFieldPattern().Match(fieldLine.Trim());
            if (fieldMatch.Success)
            {
                bundle.Fields[fieldMatch.Groups[1].Value] = fieldMatch.Groups[2].Value;
            }
            i++;
        }

        bundles.Add(bundle);
        return i;
    }

    /// <summary>
    /// Parses a trait definition (non-diagnostic version).
    /// </summary>
    private static int ParseTrait(
        IReadOnlyList<string> lines,
        int start,
        List<TraitDefinition> traits
    )
    {
        var line = lines[start].Trim();
        var match = TraitDeclarationPattern().Match(line);
        if (!match.Success)
            return start + 1;

        var trait = new TraitDefinition { Name = match.Groups[1].Value };
        var i = start + 1;
        TraitConnector? currentConnector = null;

        while (i < lines.Count)
        {
            var currentLine = lines[i];
            if (!currentLine.StartsWith("  ") || string.IsNullOrWhiteSpace(currentLine))
                break;

            var trimmed = currentLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
            {
                i++;
                continue;
            }

            // Check for connectors: block (2 spaces)
            if (currentLine.StartsWith("  ") && trimmed == "connectors:")
            {
                i++;
                continue;
            }

            // Check for connector target (4 spaces): "to TargetTrait:"
            if (
                currentLine.StartsWith("    ")
                && trimmed.StartsWith("to ")
                && trimmed.EndsWith(":")
            )
            {
                if (currentConnector is not null)
                    trait.Connectors.Add(currentConnector);

                var targetName = trimmed[3..^1].Trim();
                currentConnector = new TraitConnector { TargetTrait = targetName };
                i++;
                continue;
            }

            // Check for connector mapping (6 spaces): "SOURCE -> TARGET"
            if (currentLine.StartsWith("      ") && currentConnector is not null)
            {
                var mappingMatch = ConnectorMappingPattern().Match(trimmed);
                if (mappingMatch.Success)
                {
                    currentConnector.Mappings.Add(
                        new ConnectorMapping
                        {
                            SourcePort = mappingMatch.Groups[1].Value,
                            TargetPort = mappingMatch.Groups[2].Value,
                        }
                    );
                }
                i++;
                continue;
            }

            // Port declaration (2 spaces)
            if (trimmed.StartsWith("port "))
            {
                var portMatch = PortDeclarationPattern().Match(trimmed);
                if (portMatch.Success)
                {
                    trait.Ports.Add(
                        new PortDeclaration
                        {
                            Name = portMatch.Groups[1].Value,
                            Type = portMatch.Groups[2].Value,
                        }
                    );
                }
            }

            i++;
        }

        if (currentConnector is not null)
            trait.Connectors.Add(currentConnector);

        traits.Add(trait);
        return i;
    }

    /// <summary>
    /// Parses a trait definition with diagnostic collection.
    /// </summary>
    private static int ParseTraitWithDiagnostics(
        IReadOnlyList<string> lines,
        int start,
        List<TraitDefinition> traits,
        string filePath,
        List<Diagnostic> diagnostics
    )
    {
        var line = lines[start].Trim();
        var match = TraitDeclarationPattern().Match(line);
        if (!match.Success)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0003: Malformed trait declaration '{line}'",
                    DiagnosticSeverity.Error,
                    filePath,
                    start + 1,
                    1
                )
            );
            return start + 1;
        }

        var trait = new TraitDefinition { Name = match.Groups[1].Value };
        var i = start + 1;
        TraitConnector? currentConnector = null;

        while (i < lines.Count)
        {
            var currentLine = lines[i];
            if (!currentLine.StartsWith("  ") || string.IsNullOrWhiteSpace(currentLine))
                break;

            var trimmed = currentLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
            {
                i++;
                continue;
            }

            // Check for connectors: block (2 spaces)
            if (currentLine.StartsWith("  ") && trimmed == "connectors:")
            {
                i++;
                continue;
            }

            // Check for connector target (4 spaces): "to TargetTrait:"
            if (
                currentLine.StartsWith("    ")
                && trimmed.StartsWith("to ")
                && trimmed.EndsWith(":")
            )
            {
                if (currentConnector is not null)
                    trait.Connectors.Add(currentConnector);

                var targetName = trimmed[3..^1].Trim();
                currentConnector = new TraitConnector { TargetTrait = targetName };
                i++;
                continue;
            }

            // Check for connector mapping (6 spaces): "SOURCE -> TARGET"
            if (currentLine.StartsWith("      ") && currentConnector is not null)
            {
                var mappingMatch = ConnectorMappingPattern().Match(trimmed);
                if (mappingMatch.Success)
                {
                    currentConnector.Mappings.Add(
                        new ConnectorMapping
                        {
                            SourcePort = mappingMatch.Groups[1].Value,
                            TargetPort = mappingMatch.Groups[2].Value,
                        }
                    );
                }
                i++;
                continue;
            }

            // Port declaration (2 spaces)
            if (trimmed.StartsWith("port "))
            {
                var portMatch = PortDeclarationPattern().Match(trimmed);
                if (portMatch.Success)
                {
                    trait.Ports.Add(
                        new PortDeclaration
                        {
                            Name = portMatch.Groups[1].Value,
                            Type = portMatch.Groups[2].Value,
                        }
                    );
                }
            }

            i++;
        }

        if (currentConnector is not null)
            trait.Connectors.Add(currentConnector);

        traits.Add(trait);
        return i;
    }

    /// <summary>
    /// Parses a circuit definition with diagnostic collection.
    /// </summary>
    private static int ParseCircuitWithDiagnostics(
        IReadOnlyList<string> lines,
        int start,
        List<Circuit> circuits,
        string filePath,
        List<Diagnostic> diagnostics
    )
    {
        var line = lines[start].Trim();
        var match = CircuitDeclarationPattern().Match(line);
        if (!match.Success)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0003: Malformed circuit declaration '{line}'",
                    DiagnosticSeverity.Error,
                    filePath,
                    start + 1,
                    1
                )
            );
            return start + 1;
        }

        var name = match.Groups[1].Value;
        var traits = match.Groups[2].Success
            ? match.Groups[2].Value.Split(',').Select(t => t.Trim()).ToList()
            : null;

        var i = start + 1;
        var level = ACIRLevel.ML;
        var supplies = new List<string>();
        var grounds = new List<string>();
        var ports = new List<PortDeclaration>();
        FillBlock? fillBlock = null;
        ConstraintsBlock? constraintsBlock = null;
        HarnessBlock? harnessBlock = null;
        BenchesBlock? benchesBlock = null;

        var isInline = false;
        var parameters = new List<CircuitParameter>();
        FillBlock? currentFill = null;
        ConstraintsBlock? currentConstraints = null;
        HarnessBlock? currentHarness = null;
        BenchesBlock? currentBenches = null;
        string? constraintSubSection = null;

        // State for multi-line attach blocks with overrides
        AttachStatement? pendingAttach = null;

        while (i < lines.Count)
        {
            var currentLine = lines[i];
            if (!currentLine.StartsWith("  ") && !string.IsNullOrWhiteSpace(currentLine))
                break;

            var trimmed = currentLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
            {
                i++;
                continue;
            }

            var contentLine = currentLine;
            var commentIndex = contentLine.IndexOf("//");
            if (commentIndex >= 0)
                contentLine = contentLine[..commentIndex];
            var contentTrimmed = contentLine.Trim();

            // Check for constraint subsection headers
            if (
                currentLine.StartsWith("    ")
                && !currentLine.StartsWith("      ")
                && currentConstraints is not null
            )
            {
                if (contentTrimmed == "numeric:")
                {
                    constraintSubSection = "numeric";
                    i++;
                    continue;
                }
                else if (contentTrimmed == "tech:")
                {
                    constraintSubSection = "tech";
                    i++;
                    continue;
                }
                else if (contentTrimmed == "measure:")
                {
                    constraintSubSection = "measure";
                    i++;
                    continue;
                }
                else if (contentTrimmed == "graph:")
                {
                    constraintSubSection = "graph";
                    i++;
                    continue;
                }
            }

            // Check for constraint content
            if (
                currentLine.StartsWith("      ")
                && currentConstraints is not null
                && constraintSubSection is not null
            )
            {
                ParseConstraintContent(contentTrimmed, currentConstraints, constraintSubSection);
            }
            // Check for attach override lines (6 spaces inside fill block)
            else if (
                currentLine.StartsWith("      ")
                && currentFill is not null
                && pendingAttach is not null
            )
            {
                // Parse as override mapping or closing brace
                if (contentTrimmed == "}")
                {
                    // Finalize the pending attach
                    currentFill.Attaches.Add(pendingAttach);
                    pendingAttach = null;
                }
                else
                {
                    // Parse as override mapping (use ConnectorMappingPattern which allows dots and spaces)
                    var mappingMatch = ConnectorMappingPattern().Match(contentTrimmed);
                    if (mappingMatch.Success)
                    {
                        pendingAttach.Overrides ??= new List<ConnectorMapping>();
                        pendingAttach.Overrides.Add(
                            new ConnectorMapping
                            {
                                SourcePort = mappingMatch.Groups[1].Value,
                                TargetPort = mappingMatch.Groups[2].Value,
                            }
                        );
                    }
                }
            }
            // Check for section content
            else if (currentLine.StartsWith("    "))
            {
                // If we have a pending attach and we're now at a non-indented fill line,
                // finalize the pending attach (handles case where } is missing)
                if (pendingAttach is not null && currentFill is not null)
                {
                    currentFill.Attaches.Add(pendingAttach);
                    pendingAttach = null;
                }

                if (currentFill is not null)
                {
                    pendingAttach = ParseFillContentWithDiagnosticsAndGetAttach(
                        contentTrimmed,
                        currentFill,
                        filePath,
                        i + 1,
                        diagnostics
                    );
                }
                else if (currentHarness is not null)
                {
                    ParseHarnessContentWithDiagnostics(
                        contentTrimmed,
                        currentHarness,
                        filePath,
                        i + 1,
                        diagnostics
                    );
                }
                else if (currentBenches is not null)
                {
                    ParseBenchContent(contentTrimmed, currentBenches);
                }
            }
            // Section headers
            else if (contentTrimmed == "fill:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentFill = new FillBlock();
                currentConstraints = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (contentTrimmed == "constraints:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentConstraints = new ConstraintsBlock();
                currentFill = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (contentTrimmed == "harness:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentHarness = new HarnessBlock();
                currentFill = null;
                currentConstraints = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (contentTrimmed == "benches:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentBenches = new BenchesBlock();
                currentFill = null;
                currentConstraints = null;
                currentHarness = null;
                constraintSubSection = null;
            }
            // Top-level declarations
            else if (contentTrimmed.StartsWith("level "))
            {
                var levelStr = contentTrimmed[6..].Trim();
                var parsedLevel = TryParseLevel(levelStr);
                if (parsedLevel.HasValue)
                {
                    level = parsedLevel.Value;
                }
                else
                {
                    // No fallback: the error diagnostic causes Success=false, so callers
                    // must not use the document. The initialized value of 'level' is irrelevant.
                    diagnostics.Add(
                        new Diagnostic(
                            $"ACIR0008: Invalid level '{levelStr}' - expected HL, ML, or EL",
                            DiagnosticSeverity.Error,
                            filePath,
                            i + 1,
                            1
                        )
                    );
                }
            }
            else if (contentTrimmed.StartsWith("supply "))
            {
                supplies.Add(contentTrimmed[7..].Trim());
            }
            else if (contentTrimmed.StartsWith("ground "))
            {
                grounds.Add(contentTrimmed[7..].Trim());
            }
            else if (contentTrimmed.StartsWith("port "))
            {
                var portMatch = PortDeclarationPattern().Match(contentTrimmed);
                if (portMatch.Success)
                {
                    ports.Add(
                        new PortDeclaration
                        {
                            Name = portMatch.Groups[1].Value,
                            Type = portMatch.Groups[2].Value,
                        }
                    );
                }
            }
            else if (contentTrimmed == "inline")
            {
                isInline = true;
            }
            else if (contentTrimmed.StartsWith("param "))
            {
                var paramMatch = CircuitParameterPattern().Match(contentTrimmed);
                if (paramMatch.Success)
                {
                    var paramName = paramMatch.Groups[1].Value;
                    var paramType = paramMatch.Groups[2].Value;
                    ParamValue? defaultValue = null;

                    if (paramMatch.Groups[3].Success)
                    {
                        var defaultStr = paramMatch.Groups[3].Value.Trim();
                        defaultValue = ParamValueParser.Parse(defaultStr);
                    }

                    parameters.Add(
                        new CircuitParameter
                        {
                            Name = paramName,
                            Type = paramType,
                            Default = defaultValue,
                        }
                    );
                }
            }

            i++;
        }

        SaveCurrentSection(
            ref fillBlock,
            ref constraintsBlock,
            ref harnessBlock,
            ref benchesBlock,
            currentFill,
            currentConstraints,
            currentHarness,
            currentBenches
        );

        var circuit = new Circuit
        {
            Name = name,
            Level = level,
            Inline = isInline,
            Parameters = parameters,
            Traits = traits,
            Supplies = supplies,
            Grounds = grounds,
            Ports = ports,
            Fill = fillBlock,
            Constraints = constraintsBlock,
            Harness = harnessBlock,
            Benches = benchesBlock,
        };

        circuits.Add(circuit);
        return i;
    }

    /// <summary>
    /// Parses fill content with diagnostic collection.
    /// Returns a pending attach statement if the line starts an attach block with overrides (has `{`).
    /// Otherwise, returns null and adds the parsed content directly to the fill block.
    /// </summary>
    private static AttachStatement? ParseFillContentWithDiagnosticsAndGetAttach(
        string line,
        FillBlock fill,
        string filePath,
        int lineNumber,
        List<Diagnostic> diagnostics
    )
    {
        if (line.StartsWith("net "))
        {
            var match = NetDeclarationPattern().Match(line);
            if (match.Success)
            {
                fill.Nets.Add(
                    new NetDeclaration
                    {
                        Id = match.Groups[1].Value,
                        Domain = match.Groups[2].Value,
                    }
                );
            }
        }
        else if (
            line.StartsWith("nmos ")
            || line.StartsWith("pmos ")
            || line.StartsWith("resistor ")
            || line.StartsWith("capacitor ")
            || line.StartsWith("inductor ")
            || line.StartsWith("diode ")
        )
        {
            var device = ParseDeviceWithDiagnostics(line, filePath, lineNumber, diagnostics);
            if (device is not null)
                fill.Devices.Add(device);
        }
        else if (line.StartsWith("inst "))
        {
            var instance = ParseInstanceWithDiagnostics(line, filePath, lineNumber, diagnostics);
            if (instance is not null)
                fill.Instances.Add(instance);
        }
        else if (line.StartsWith("attach "))
        {
            var (attach, hasBrace) = ParseAttachWithDiagnostics(
                line,
                filePath,
                lineNumber,
                diagnostics
            );
            if (attach is not null)
            {
                if (hasBrace)
                {
                    // Return as pending attach - caller will collect override lines
                    return attach;
                }
                else
                {
                    // No brace - add directly to fill
                    fill.Attaches.Add(attach);
                }
            }
        }
        else if (line.StartsWith("connect "))
        {
            // Parse connection statement
            var connectMatch = ConnectionPattern().Match(line.Substring("connect ".Length));
            if (connectMatch.Success)
            {
                fill.Connections.Add(
                    new ConnectionStatement
                    {
                        From = connectMatch.Groups[1].Value,
                        To = connectMatch.Groups[2].Value,
                    }
                );
            }
        }

        return null;
    }

    /// <summary>
    /// Parses an instance declaration with diagnostic collection.
    /// </summary>
    private static InstanceDeclaration? ParseInstanceWithDiagnostics(
        string line,
        string filePath,
        int lineNumber,
        List<Diagnostic> diagnostics
    )
    {
        var match = InstanceDeclarationPattern().Match(line);
        if (!match.Success)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0015: Invalid instance declaration syntax '{line}'",
                    DiagnosticSeverity.Error,
                    filePath,
                    lineNumber,
                    1
                )
            );
            return null;
        }

        var id = match.Groups[1].Value;
        var type = match.Groups[3].Value;
        var bindings = new Dictionary<string, string>();

        if (match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
        {
            foreach (var binding in match.Groups[2].Value.Split(','))
            {
                var bindMatch = ConnectionPattern().Match(binding.Trim());
                if (bindMatch.Success)
                {
                    bindings[bindMatch.Groups[1].Value] = bindMatch.Groups[2].Value;
                }
            }
        }

        return new InstanceDeclaration
        {
            Id = id,
            Type = type,
            Bindings = bindings,
        };
    }

    /// <summary>
    /// Parses an attach statement with diagnostic collection.
    /// Returns the attach statement and whether it has an opening brace (indicating multi-line overrides).
    /// </summary>
    private static (AttachStatement? attach, bool hasBrace) ParseAttachWithDiagnostics(
        string line,
        string filePath,
        int lineNumber,
        List<Diagnostic> diagnostics
    )
    {
        var match = AttachStatementPattern().Match(line);
        if (!match.Success)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0016: Invalid attach statement syntax '{line}'",
                    DiagnosticSeverity.Error,
                    filePath,
                    lineNumber,
                    1
                )
            );
            return (null, false);
        }

        var sourceInstance = match.Groups[1].Value;
        var targetInstance = match.Groups[2].Value;
        var sourceTrait = match.Groups[3].Value;
        var targetTrait = match.Groups[4].Value;
        var anchor = match.Groups[5].Success ? match.Groups[5].Value : null;
        var hasBrace = match.Groups[6].Success; // Group 6 is the optional `{`

        var attach = new AttachStatement
        {
            SourceInstance = sourceInstance,
            TargetInstance = targetInstance,
            Via = $"{sourceTrait}::{targetTrait}",
            Anchor = anchor,
        };

        return (attach, hasBrace);
    }

    /// <summary>
    /// Parses a device declaration with diagnostic collection.
    /// </summary>
    private static DeviceDeclaration? ParseDeviceWithDiagnostics(
        string line,
        string filePath,
        int lineNumber,
        List<Diagnostic> diagnostics
    )
    {
        // Pattern: deviceType id (bindings) : params [pdkDevice]
        var deviceMatch = DeviceDeclarationPattern().Match(line);
        if (!deviceMatch.Success)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"ACIR0004: Invalid device declaration syntax '{line}'",
                    DiagnosticSeverity.Error,
                    filePath,
                    lineNumber,
                    1
                )
            );
            return null;
        }

        var bindings = new Dictionary<string, string>();
        var deviceParams = new Dictionary<string, string>();
        string? pdkDevice = null;

        // Parse bindings
        var bindingsStr = deviceMatch.Groups[3].Value;
        foreach (var binding in bindingsStr.Split(','))
        {
            var bindMatch = ConnectionPattern().Match(binding.Trim());
            if (bindMatch.Success)
            {
                bindings[bindMatch.Groups[1].Value] = bindMatch.Groups[2].Value;
            }
            else if (!string.IsNullOrWhiteSpace(binding))
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"ACIR0005: Malformed binding syntax '{binding.Trim()}' - expected 'TERMINAL->NET'",
                        DiagnosticSeverity.Warning,
                        filePath,
                        lineNumber,
                        1
                    )
                );
            }
        }

        // Parse params
        var paramsStr = deviceMatch.Groups[4].Value.Trim();
        var paramParts = paramsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in paramParts)
        {
            var eqIndex = part.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = part[..eqIndex];
                var value = part[(eqIndex + 1)..];
                deviceParams[key] = value;
            }
            else if (!string.IsNullOrEmpty(part))
            {
                pdkDevice = part;
            }
        }

        return new DeviceDeclaration
        {
            DeviceType = deviceMatch.Groups[1].Value,
            Id = deviceMatch.Groups[2].Value,
            Bindings = bindings,
            Params = deviceParams,
            PdkDevice = pdkDevice,
        };
    }

    /// <summary>
    /// Parses a bundle type definition.
    /// </summary>
    /// <param name="lines">All lines in the document.</param>
    /// <param name="start">Starting line index.</param>
    /// <param name="bundles">Output list to append the parsed bundle.</param>
    /// <returns>Index of the next line after the bundle definition.</returns>
    private static int ParseBundle(IReadOnlyList<string> lines, int start, List<BundleType> bundles)
    {
        var line = lines[start].Trim();
        var match = BundleDeclarationPattern().Match(line);
        if (!match.Success)
            return start + 1;

        var bundle = new BundleType { Name = match.Groups[1].Value };
        var i = start + 1;

        while (i < lines.Count)
        {
            var fieldLine = lines[i];
            if (!fieldLine.StartsWith("  ") || string.IsNullOrWhiteSpace(fieldLine))
                break;

            var fieldMatch = BundleFieldPattern().Match(fieldLine.Trim());
            if (fieldMatch.Success)
            {
                bundle.Fields[fieldMatch.Groups[1].Value] = fieldMatch.Groups[2].Value;
            }
            i++;
        }

        bundles.Add(bundle);
        return i;
    }

    /// <summary>
    /// Parses a circuit definition including all sections (fill, constraints, harness, benches).
    /// </summary>
    /// <param name="lines">All lines in the document.</param>
    /// <param name="start">Starting line index.</param>
    /// <param name="circuits">Output list to append the parsed circuit.</param>
    /// <returns>Index of the next line after the circuit definition.</returns>
    private static int ParseCircuit(IReadOnlyList<string> lines, int start, List<Circuit> circuits)
    {
        var line = lines[start].Trim();
        var match = CircuitDeclarationPattern().Match(line);
        if (!match.Success)
            return start + 1;

        var name = match.Groups[1].Value;
        var traits = match.Groups[2].Success
            ? match.Groups[2].Value.Split(',').Select(t => t.Trim()).ToList()
            : null;

        var i = start + 1;
        var level = ACIRLevel.ML;
        var isInline = false;
        var parameters = new List<CircuitParameter>();
        var supplies = new List<string>();
        var grounds = new List<string>();
        var ports = new List<PortDeclaration>();
        FillBlock? fillBlock = null;
        ConstraintsBlock? constraintsBlock = null;
        HarnessBlock? harnessBlock = null;
        BenchesBlock? benchesBlock = null;

        FillBlock? currentFill = null;
        ConstraintsBlock? currentConstraints = null;
        HarnessBlock? currentHarness = null;
        BenchesBlock? currentBenches = null;
        string? constraintSubSection = null; // Tracks "numeric:", "tech:", "measure:" within constraints

        while (i < lines.Count)
        {
            var currentLine = lines[i];
            if (!currentLine.StartsWith("  ") && !string.IsNullOrWhiteSpace(currentLine))
                break;

            var trimmed = currentLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
            {
                i++;
                continue;
            }

            var contentLine = currentLine;
            var commentIndex = contentLine.IndexOf("//");
            if (commentIndex >= 0)
                contentLine = contentLine[..commentIndex];
            var contentTrimmed = contentLine.Trim();

            // Check for constraint subsection headers (4 spaces, within constraints)
            if (
                currentLine.StartsWith("    ")
                && !currentLine.StartsWith("      ")
                && currentConstraints is not null
            )
            {
                if (contentTrimmed == "numeric:")
                {
                    constraintSubSection = "numeric";
                    i++;
                    continue;
                }
                else if (contentTrimmed == "tech:")
                {
                    constraintSubSection = "tech";
                    i++;
                    continue;
                }
                else if (contentTrimmed == "measure:")
                {
                    constraintSubSection = "measure";
                    i++;
                    continue;
                }
                else if (contentTrimmed == "graph:")
                {
                    constraintSubSection = "graph";
                    i++;
                    continue;
                }
            }

            // Check for constraint content (6 spaces = inside a constraint subsection)
            if (
                currentLine.StartsWith("      ")
                && currentConstraints is not null
                && constraintSubSection is not null
            )
            {
                ParseConstraintContent(contentTrimmed, currentConstraints, constraintSubSection);
            }
            // Check for section content (4 spaces = inside a section block)
            else if (currentLine.StartsWith("    "))
            {
                if (currentFill is not null)
                {
                    ParseFillContent(contentTrimmed, currentFill);
                }
                else if (currentHarness is not null)
                {
                    ParseHarnessContent(contentTrimmed, currentHarness);
                }
                else if (currentBenches is not null)
                {
                    ParseBenchContent(contentTrimmed, currentBenches);
                }
            }
            // Section headers (2 spaces) - preserve previous section before starting new one
            else if (contentTrimmed == "fill:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentFill = new FillBlock();
                currentConstraints = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (contentTrimmed == "constraints:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentConstraints = new ConstraintsBlock();
                currentFill = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (contentTrimmed == "harness:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentHarness = new HarnessBlock();
                currentFill = null;
                currentConstraints = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (contentTrimmed == "benches:")
            {
                SaveCurrentSection(
                    ref fillBlock,
                    ref constraintsBlock,
                    ref harnessBlock,
                    ref benchesBlock,
                    currentFill,
                    currentConstraints,
                    currentHarness,
                    currentBenches
                );
                currentBenches = new BenchesBlock();
                currentFill = null;
                currentConstraints = null;
                currentHarness = null;
                constraintSubSection = null;
            }
            // Top-level declarations (2 spaces, not inside a section)
            else if (contentTrimmed.StartsWith("level "))
            {
                var levelStr = contentTrimmed[6..].Trim();
                level = TryParseLevel(levelStr) ?? ACIRLevel.ML;
            }
            else if (contentTrimmed.StartsWith("supply "))
            {
                supplies.Add(contentTrimmed[7..].Trim());
            }
            else if (contentTrimmed.StartsWith("ground "))
            {
                grounds.Add(contentTrimmed[7..].Trim());
            }
            else if (contentTrimmed.StartsWith("port "))
            {
                var portMatch = PortDeclarationPattern().Match(contentTrimmed);
                if (portMatch.Success)
                {
                    ports.Add(
                        new PortDeclaration
                        {
                            Name = portMatch.Groups[1].Value,
                            Type = portMatch.Groups[2].Value,
                        }
                    );
                }
            }
            else if (contentTrimmed == "inline")
            {
                isInline = true;
            }
            else if (contentTrimmed.StartsWith("param "))
            {
                var paramMatch = CircuitParameterPattern().Match(contentTrimmed);
                if (paramMatch.Success)
                {
                    var paramName = paramMatch.Groups[1].Value;
                    var paramType = paramMatch.Groups[2].Value;
                    ParamValue? defaultValue = null;

                    if (paramMatch.Groups[3].Success)
                    {
                        var defaultStr = paramMatch.Groups[3].Value.Trim();
                        defaultValue = ParamValueParser.Parse(defaultStr);
                    }

                    parameters.Add(
                        new CircuitParameter
                        {
                            Name = paramName,
                            Type = paramType,
                            Default = defaultValue,
                        }
                    );
                }
            }

            i++;
        }

        SaveCurrentSection(
            ref fillBlock,
            ref constraintsBlock,
            ref harnessBlock,
            ref benchesBlock,
            currentFill,
            currentConstraints,
            currentHarness,
            currentBenches
        );

        var circuit = new Circuit
        {
            Name = name,
            Level = level,
            Inline = isInline,
            Parameters = parameters,
            Traits = traits,
            Supplies = supplies,
            Grounds = grounds,
            Ports = ports,
            Fill = fillBlock,
            Constraints = constraintsBlock,
            Harness = harnessBlock,
            Benches = benchesBlock,
        };

        circuits.Add(circuit);
        return i;
    }

    /// <summary>
    /// Saves current section to the appropriate block variable if not null.
    /// </summary>
    private static void SaveCurrentSection(
        ref FillBlock? fillBlock,
        ref ConstraintsBlock? constraintsBlock,
        ref HarnessBlock? harnessBlock,
        ref BenchesBlock? benchesBlock,
        FillBlock? currentFill,
        ConstraintsBlock? currentConstraints,
        HarnessBlock? currentHarness,
        BenchesBlock? currentBenches
    )
    {
        if (currentFill is not null)
            fillBlock = currentFill;
        if (currentConstraints is not null)
            constraintsBlock = currentConstraints;
        if (currentHarness is not null)
            harnessBlock = currentHarness;
        if (currentBenches is not null)
            benchesBlock = currentBenches;
    }

    /// <summary>
    /// Parses a constraint line within a constraints subsection.
    /// </summary>
    /// <param name="line">Trimmed line content.</param>
    /// <param name="constraints">ConstraintsBlock to populate.</param>
    /// <param name="subSection">Current subsection: "numeric", "tech", "measure", or "graph".</param>
    private static void ParseConstraintContent(
        string line,
        ConstraintsBlock constraints,
        string subSection
    )
    {
        switch (subSection)
        {
            case "numeric":
                ParseNumericConstraint(line, constraints);
                break;
            case "tech":
                ParseTechConstraint(line, constraints);
                break;
            case "measure":
                ParseMeasureIntent(line, constraints);
                break;
            case "graph":
                // Graph constraints not yet implemented
                break;
        }
    }

    /// <summary>
    /// Parses a numeric constraint line.
    /// Format: id : Metric @ Node op value unit
    /// Example: c_gbw : GainBandwidth @ OUT >= 100M Hz
    /// </summary>
    private static void ParseNumericConstraint(string line, ConstraintsBlock constraints)
    {
        // Pattern: id : Metric @ Node op value unit  OR  id : Metric op value unit (no node)
        var match = ConstraintPattern().Match(line);
        if (!match.Success)
            return;

        constraints.Numeric.Add(
            new NumericConstraint
            {
                Id = match.Groups[1].Value,
                Metric = match.Groups[2].Value,
                Node = match.Groups[3].Success ? match.Groups[3].Value : null,
                Op = match.Groups[4].Value,
                Value = match.Groups[5].Value,
                Unit = match.Groups[6].Value,
            }
        );
    }

    /// <summary>
    /// Parses a tech constraint line.
    /// Format: id : Param op value unit on scope
    /// Example: t_lmin : L >= 180n m on *
    /// </summary>
    private static void ParseTechConstraint(string line, ConstraintsBlock constraints)
    {
        // Pattern: id : Param op value unit on scope
        var match = ConstraintOnConditionPattern().Match(line);
        if (!match.Success)
            return;

        constraints.Tech.Add(
            new TechConstraint
            {
                Id = match.Groups[1].Value,
                Param = match.Groups[2].Value,
                Op = match.Groups[3].Value,
                Value = match.Groups[4].Value,
                Unit = match.Groups[5].Value,
                Scope = match.Groups[6].Value,
            }
        );
    }

    /// <summary>
    /// Parses a measure intent line.
    /// Format: id : BenchName Metric @ Node
    /// Example: m_gbw : SEOpAmpACBench GainBandwidth @ OUT
    /// </summary>
    private static void ParseMeasureIntent(string line, ConstraintsBlock constraints)
    {
        // Pattern: id : BenchName Metric @ Node  OR  id : BenchName Metric (no node)
        var match = MetricDeclarationWithCornerPattern().Match(line);
        if (!match.Success)
            return;

        constraints.Measure.Add(
            new MeasureIntent
            {
                Id = match.Groups[1].Value,
                Bench = match.Groups[2].Value,
                Metric = match.Groups[3].Value,
                Node = match.Groups[4].Success ? match.Groups[4].Value : null,
            }
        );
    }

    /// <summary>
    /// Parses a line within a fill block (net or device declaration).
    /// </summary>
    /// <param name="line">Trimmed line content.</param>
    /// <param name="fill">Fill block to populate.</param>
    private static void ParseFillContent(string line, FillBlock fill)
    {
        if (line.StartsWith("net "))
        {
            var match = NetDeclarationPattern().Match(line);
            if (match.Success)
            {
                fill.Nets.Add(
                    new NetDeclaration
                    {
                        Id = match.Groups[1].Value,
                        Domain = match.Groups[2].Value,
                    }
                );
            }
        }
        else if (
            line.StartsWith("nmos ")
            || line.StartsWith("pmos ")
            || line.StartsWith("resistor ")
            || line.StartsWith("capacitor ")
            || line.StartsWith("inductor ")
            || line.StartsWith("diode ")
        )
        {
            var device = ParseDevice(line);
            if (device is not null)
                fill.Devices.Add(device);
        }
        else if (line.StartsWith("inst "))
        {
            var instance = ParseInstance(line);
            if (instance is not null)
                fill.Instances.Add(instance);
        }
        else if (line.StartsWith("attach "))
        {
            var attach = ParseAttach(line);
            if (attach is not null)
                fill.Attaches.Add(attach);
        }
    }

    /// <summary>
    /// Parses a device declaration line (nmos, pmos, resistor, capacitor, etc.).
    /// </summary>
    /// <param name="line">Device declaration line.</param>
    /// <returns>Parsed device declaration, or null if the line doesn't match the expected format.</returns>
    private static DeviceDeclaration? ParseDevice(string line)
    {
        // Pattern: deviceType id (bindings) : params [pdkDevice]
        var deviceMatch = DeviceDeclarationPattern().Match(line);
        if (!deviceMatch.Success)
            return null;

        var bindings = new Dictionary<string, string>();
        var deviceParams = new Dictionary<string, string>();
        string? pdkDevice = null;

        // Parse bindings: B->GND, D->OUT, G->IN, S->tnode
        var bindingsStr = deviceMatch.Groups[3].Value;
        foreach (var binding in bindingsStr.Split(','))
        {
            var bindMatch = ConnectionPattern().Match(binding.Trim());
            if (bindMatch.Success)
            {
                bindings[bindMatch.Groups[1].Value] = bindMatch.Groups[2].Value;
            }
        }

        // Parse params: L=180n M=1 W=2u pdkDevice
        var paramsStr = deviceMatch.Groups[4].Value.Trim();
        var paramParts = paramsStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in paramParts)
        {
            var eqIndex = part.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = part[..eqIndex];
                var value = part[(eqIndex + 1)..];
                deviceParams[key] = value;
            }
            else if (!string.IsNullOrEmpty(part))
            {
                // Last non-param part is PDK device name
                pdkDevice = part;
            }
        }

        return new DeviceDeclaration
        {
            DeviceType = deviceMatch.Groups[1].Value,
            Id = deviceMatch.Groups[2].Value,
            Bindings = bindings,
            Params = deviceParams,
            PdkDevice = pdkDevice,
        };
    }

    /// <summary>
    /// Parses an instance declaration line.
    /// </summary>
    /// <param name="line">Instance declaration line.</param>
    /// <returns>Parsed instance declaration, or null if the line doesn't match.</returns>
    private static InstanceDeclaration? ParseInstance(string line)
    {
        var match = InstanceDeclarationPattern().Match(line);
        if (!match.Success)
            return null;

        var id = match.Groups[1].Value;
        var type = match.Groups[3].Value;
        var bindings = new Dictionary<string, string>();

        if (match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
        {
            foreach (var binding in match.Groups[2].Value.Split(','))
            {
                var bindMatch = ConnectionPattern().Match(binding.Trim());
                if (bindMatch.Success)
                {
                    bindings[bindMatch.Groups[1].Value] = bindMatch.Groups[2].Value;
                }
            }
        }

        return new InstanceDeclaration
        {
            Id = id,
            Type = type,
            Bindings = bindings,
        };
    }

    /// <summary>
    /// Parses an attach statement line.
    /// </summary>
    /// <param name="line">Attach statement line.</param>
    /// <returns>Parsed attach statement, or null if the line doesn't match.</returns>
    private static AttachStatement? ParseAttach(string line)
    {
        var match = AttachStatementPattern().Match(line);
        if (!match.Success)
            return null;

        var sourceInstance = match.Groups[1].Value;
        var targetInstance = match.Groups[2].Value;
        var sourceTrait = match.Groups[3].Value;
        var targetTrait = match.Groups[4].Value;
        var anchor = match.Groups[5].Success ? match.Groups[5].Value : null;

        return new AttachStatement
        {
            SourceInstance = sourceInstance,
            TargetInstance = targetInstance,
            Via = $"{sourceTrait}::{targetTrait}",
            Anchor = anchor,
        };
    }

    /// <summary>
    /// Parses a line within a harness block (supply, load, source, bias, or sweep) with diagnostic collection.
    /// </summary>
    /// <param name="line">Trimmed line content.</param>
    /// <param name="harness">Harness block to populate.</param>
    /// <param name="filePath">Source file path for diagnostics.</param>
    /// <param name="lineNumber">Line number for diagnostics.</param>
    /// <param name="diagnostics">List to collect diagnostics.</param>
    private static void ParseHarnessContentWithDiagnostics(
        string line,
        HarnessBlock harness,
        string filePath,
        int lineNumber,
        List<Diagnostic> diagnostics
    )
    {
        // Preserve existing parsing behavior, but emit diagnostics for malformed constructs.
        ParseHarnessContent(line, harness);

        if (line.StartsWith("load "))
        {
            bool hasOpenParen = line.Contains('(');
            bool hasCloseParen = line.Contains(')');
            bool hasPipe = line.Contains("||");

            if (hasOpenParen || hasCloseParen || hasPipe)
            {
                if (!hasOpenParen || !hasCloseParen)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"ACIR0010: Parallel load specification missing parentheses: '{line}'",
                            DiagnosticSeverity.Error,
                            filePath,
                            lineNumber,
                            1
                        )
                    );
                }
                else if (!hasPipe)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"ACIR0011: Parallel load specification missing '||' operator: '{line}'",
                            DiagnosticSeverity.Error,
                            filePath,
                            lineNumber,
                            1
                        )
                    );
                }
                else
                {
                    var contentMatch = ParenthesizedContentPattern().Match(line);
                    var content = contentMatch.Groups[1].Value;
                    var parts = content.Split("||");

                    if (parts.Length >= 1 && string.IsNullOrWhiteSpace(parts[0]))
                    {
                        diagnostics.Add(
                            new Diagnostic(
                                $"ACIR0012: Parallel load specification missing first element: '{line}'",
                                DiagnosticSeverity.Error,
                                filePath,
                                lineNumber,
                                1
                            )
                        );
                    }

                    if (parts.Length >= 2 && string.IsNullOrWhiteSpace(parts[1]))
                    {
                        diagnostics.Add(
                            new Diagnostic(
                                $"ACIR0013: Parallel load specification missing second element: '{line}'",
                                DiagnosticSeverity.Error,
                                filePath,
                                lineNumber,
                                1
                            )
                        );
                    }

                    foreach (var part in parts)
                    {
                        var trimmed = part.Trim();
                        if (
                            !string.IsNullOrWhiteSpace(trimmed)
                            && (trimmed == "C=" || trimmed == "R=")
                        )
                        {
                            diagnostics.Add(
                                new Diagnostic(
                                    $"ACIR0014: Parallel load element missing value: '{line}'",
                                    DiagnosticSeverity.Error,
                                    filePath,
                                    lineNumber,
                                    1
                                )
                            );
                        }
                    }
                }
            }
        }

        // Specifically: sweep lines previously silently ignored unrecognized range specs.
        if (line.StartsWith("sweep "))
        {
            // Allow empty brackets here so we can report [] as invalid rather than silently ignoring it.
            // Pattern: sweep ConditionName [start:step:stop] or [start:stop] or [Auto]
            var match = SweepDeclarationPattern().Match(line);
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var rangeSpec = match.Groups[2].Value.Trim();
                var sweep = ParseSweepRange(name, rangeSpec);
                if (sweep == null)
                {
                    diagnostics.Add(
                        new Diagnostic(
                            $"ACIR0006: Invalid sweep range specification '{rangeSpec}' in line '{line}'",
                            DiagnosticSeverity.Error,
                            filePath,
                            lineNumber,
                            1
                        )
                    );
                }
            }
        }
    }

    /// <summary>
    /// Parses a line within a harness block (supply, load, source, bias, or sweep).
    /// </summary>
    /// <param name="line">Trimmed line content.</param>
    /// <param name="harness">Harness block to populate.</param>
    private static void ParseHarnessContent(string line, HarnessBlock harness)
    {
        if (line.StartsWith("supply "))
        {
            var match = SupplyDeclarationPattern().Match(line);
            if (match.Success)
            {
                harness.Supplies.Add(
                    new SupplyValue
                    {
                        Net = match.Groups[1].Value,
                        Value = NormalizeQuantity(match.Groups[2].Value.Trim(), "V"),
                    }
                );
            }
        }
        else if (line.StartsWith("load "))
        {
            // Try parallel syntax first: load NET (C=... || R=...) or (R=... || C=...)
            var parallelMatch = LoadWithPortsPattern().Match(line);
            if (parallelMatch.Success)
            {
                var net = parallelMatch.Groups[1].Value;
                var content = parallelMatch.Groups[2].Value;
                var parts = content.Split("||", StringSplitOptions.RemoveEmptyEntries);

                var elements = new List<LoadElement>();

                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedPart))
                        continue;

                    if (trimmedPart.StartsWith("C="))
                    {
                        var value = NormalizeQuantity(trimmedPart[2..].Trim(), "F");
                        elements.Add(new LoadElement("C", value));
                    }
                    else if (trimmedPart.StartsWith("R="))
                    {
                        var value = NormalizeQuantity(trimmedPart[2..].Trim(), "Ohm");
                        elements.Add(new LoadElement("R", value));
                    }
                }

                harness.Loads.Add(new LoadValue { Net = net, Elements = elements });
            }
            else
            {
                // Single-element syntax
                var match = LoadWithComponentPattern().Match(line);
                if (match.Success)
                {
                    var net = match.Groups[1].Value;
                    var elementType = match.Groups[2].Value;
                    var value = match.Groups[3].Value.Trim();

                    var normalizedValue =
                        elementType == "C"
                            ? NormalizeQuantity(value, "F")
                            : NormalizeQuantity(value, "Ohm");

                    harness.Loads.Add(
                        new LoadValue
                        {
                            Net = net,
                            Elements = new List<LoadElement>
                            {
                                new LoadElement(elementType, normalizedValue),
                            },
                        }
                    );
                }
            }
        }
        else if (line.StartsWith("source "))
        {
            var match = SourceImpedancePattern().Match(line);
            if (match.Success)
            {
                harness.Sources.Add(
                    new SourceValue
                    {
                        Net = match.Groups[1].Value,
                        Z = NormalizeQuantity(match.Groups[2].Value.Trim(), "Ohm"),
                    }
                );
            }
        }
        else if (line.StartsWith("bias "))
        {
            var match = BiasDeclarationPattern().Match(line);
            if (match.Success)
            {
                harness.Biases.Add(
                    new BiasValue
                    {
                        Net = match.Groups[1].Value,
                        Value = NormalizeQuantity(match.Groups[2].Value.Trim(), "V"),
                    }
                );
            }
        }
        else if (line.StartsWith("sweep "))
        {
            // Pattern: sweep ConditionName [start:step:stop] or [start:stop] or [Auto]
            var match = SweepWithRangePattern().Match(line);
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var rangeSpec = match.Groups[2].Value.Trim();
                var sweep = ParseSweepRange(name, rangeSpec);
                if (sweep != null)
                {
                    harness.Sweeps.Add(sweep);
                }
            }
        }
    }

    /// <summary>
    /// Normalizes a quantity string to compact SPICE-compatible format: "valuePrefixUnit".
    /// Examples: "1.8V", "1pF", "1MOhm", "100mV"
    /// </summary>
    private static string NormalizeQuantity(string value, string defaultUnit = "")
    {
        // Regex pattern for quantity: (\d+\.?\d*)\s*([fpnumkMGT]?)\s*([A-Za-z]+)?
        var match = UnitValuePattern().Match(value.Trim());
        if (!match.Success)
            return value;

        var numeric = match.Groups[1].Value;
        var prefix = match.Groups[2].Value;
        var unit = match.Groups[3].Value;

        if (string.IsNullOrEmpty(unit))
            unit = defaultUnit;

        // Normalize Ohm
        if (unit.Equals("ohm", StringComparison.OrdinalIgnoreCase))
            unit = "Ohm";

        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(unit))
            return numeric;

        // Compact format: no spaces (consistent with device params like L=180n, W=2u)
        return $"{numeric}{prefix}{unit}";
    }

    /// <summary>
    /// Parses a sweep range specification into a SweepCondition.
    /// Supports formats: [start:step:stop], [start:stop], or [Auto].
    /// </summary>
    private static SweepCondition? ParseSweepRange(string name, string rangeSpec)
    {
        if (rangeSpec.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return new SweepCondition
            {
                Name = name,
                IsAuto = true,
                Start = string.Empty,
                Stop = string.Empty,
                Step = null,
            };
        }

        // Parse [start:step:stop] or [start:stop]
        var parts = rangeSpec.Split(':');
        if (parts.Length == 2)
        {
            // Auto-step format: [start:stop]
            return new SweepCondition
            {
                Name = name,
                Start = NormalizeQuantity(parts[0].Trim(), "V"),
                Stop = NormalizeQuantity(parts[1].Trim(), "V"),
                Step = null,
                IsAuto = false,
            };
        }
        else if (parts.Length == 3)
        {
            // Explicit step format: [start:step:stop]
            return new SweepCondition
            {
                Name = name,
                Start = NormalizeQuantity(parts[0].Trim(), "V"),
                Step = NormalizeQuantity(parts[1].Trim(), "V"),
                Stop = NormalizeQuantity(parts[2].Trim(), "V"),
                IsAuto = false,
            };
        }

        return null;
    }

    /// <summary>
    /// Parses a line within a benches block (bench name or configuration).
    /// </summary>
    /// <param name="line">Trimmed line content.</param>
    /// <param name="benches">Benches block to populate.</param>
    private static void ParseBenchContent(string line, BenchesBlock benches)
    {
        // Simple bench name (no config)
        if (!line.Contains(':') && !line.Contains('='))
        {
            benches.Benches.Add(new BenchConfig { Name = line.Trim() });
        }
    }

    /// <summary>
    /// Parses an ACIR level string (HL, ML, or EL) into the corresponding enum value.
    /// </summary>
    /// <param name="level">Level string.</param>
    /// <returns>Parsed ACIR level, or null if unrecognized.</returns>
    private static ACIRLevel? TryParseLevel(string level)
    {
        return level.ToUpperInvariant() switch
        {
            "HL" => ACIRLevel.HL,
            "ML" => ACIRLevel.ML,
            "EL" => ACIRLevel.EL,
            _ => null,
        };
    }

    /// <summary>
    /// Determines if a line is empty or a comment (starts with '//').
    /// </summary>
    /// <param name="line">Line to check.</param>
    /// <returns>True if the line should be skipped.</returns>
    private static bool IsEmptyOrCommentLine(string line)
    {
        var trimmed = line.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//");
    }

    [GeneratedRegex(@"^bundle\s+(\w+)\s*:")]
    private static partial Regex BundleDeclarationPattern();

    [GeneratedRegex(@"^(\w+)\s*:\s*(\w+)")]
    private static partial Regex BundleFieldPattern();

    [GeneratedRegex(@"^circuit\s+(\w+)(?:\s*:\s*(.+))?$")]
    private static partial Regex CircuitDeclarationPattern();

    [GeneratedRegex(@"^port\s+(\w+)\s*:\s*(\w+)")]
    private static partial Regex PortDeclarationPattern();

    [GeneratedRegex(@"^net\s+(\w+)\s*:\s*(\w+)")]
    private static partial Regex NetDeclarationPattern();

    [GeneratedRegex(
        @"^(nmos|pmos|resistor|capacitor|inductor|diode)\s+([^\s(]+)\s*\(([^)]+)\)\s*:\s*(.+)$"
    )]
    private static partial Regex DeviceDeclarationPattern();

    [GeneratedRegex(@"(\w+)->(\w+)")]
    private static partial Regex ConnectionPattern();

    [GeneratedRegex(@"^(\w+)\s*:\s*(\w+)(?:\s*@\s*(\w+))?\s*(>=|<=|==|>|<)\s*(\S+)\s+(\w+)$")]
    private static partial Regex ConstraintPattern();

    [GeneratedRegex(@"^(\w+)\s*:\s*(\w+)\s*(>=|<=|==|>|<)\s*(\S+)\s+(\w+)\s+on\s+(\S+)$")]
    private static partial Regex ConstraintOnConditionPattern();

    [GeneratedRegex(@"^(\w+)\s*:\s*(\w+)\s+(\w+)(?:\s*@\s*(\w+))?$")]
    private static partial Regex MetricDeclarationWithCornerPattern();

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex ParenthesizedContentPattern();

    [GeneratedRegex(@"^sweep\s+(\w+)\s+\[([^\]]*)\]$")]
    private static partial Regex SweepDeclarationPattern();

    [GeneratedRegex(@"^supply\s+(\w+)\s*=\s*(.+)$")]
    private static partial Regex SupplyDeclarationPattern();

    [GeneratedRegex(@"^load\s+(\w+)\s+\(([^)]+)\)$")]
    private static partial Regex LoadWithPortsPattern();

    [GeneratedRegex(@"^load\s+(\w+)\s+(C|R)=([^;]+)")]
    private static partial Regex LoadWithComponentPattern();

    [GeneratedRegex(@"^source\s+(\w+)\s+Z=([^;]+)")]
    private static partial Regex SourceImpedancePattern();

    [GeneratedRegex(@"^bias\s+(\w+)\s*=\s*(.+)$")]
    private static partial Regex BiasDeclarationPattern();

    [GeneratedRegex(@"^sweep\s+(\w+)\s+\[([^\]]+)\]$")]
    private static partial Regex SweepWithRangePattern();

    [GeneratedRegex(@"^(\d+\.?\d*)\s*([fpnumkMGT]?)\s*([A-Za-z]+)?$")]
    private static partial Regex UnitValuePattern();

    [GeneratedRegex(@"^trait\s+(\w+)\s*:")]
    private static partial Regex TraitDeclarationPattern();

    [GeneratedRegex(@"^([\w.\[\]]+)\s*->\s*([\w.\[\]]+)$")]
    private static partial Regex ConnectorMappingPattern();

    [GeneratedRegex(@"^param\s+(\w+)\s*:\s*(\w+)(?:\s*=\s*(.+))?$")]
    private static partial Regex CircuitParameterPattern();

    [GeneratedRegex(@"^inst\s+(\w+)\s*(?:\(([^)]*)\))?\s*:\s*(\w+)")]
    private static partial Regex InstanceDeclarationPattern();

    [GeneratedRegex(
        @"^attach\s+(\w+)\s+to\s+(\w+)\s+via\s+(\w+)::(\w+)(?:\s+as\s+(\w+))?(?:\s*(\{))?$"
    )]
    private static partial Regex AttachStatementPattern();
}
