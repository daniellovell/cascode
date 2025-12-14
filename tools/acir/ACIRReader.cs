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
public static class ACIRReader
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
            var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
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
            var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
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
            diagnostics.Add(new Diagnostic(
                $"ACIR0001: Failed to parse ACIR: {ex.Message}",
                DiagnosticSeverity.Error,
                filePath,
                1,
                1));
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
        while (i < lines.Count && IsEmptyOrComment(lines[i]))
            i++;

        // Version line
        if (i < lines.Count && lines[i].StartsWith("ACIR"))
        {
            var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[1], out var ver))
            {
                doc = new ACIRDocument { Version = ver };
            }
            i++;
        }

        // Parse remaining content
        while (i < lines.Count)
        {
            var line = lines[i].TrimEnd();
            if (IsEmptyOrComment(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("bundle "))
            {
                i = ParseBundle(lines, i, doc.BundleTypes);
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
    private static ACIRDocument ParseWithDiagnostics(IReadOnlyList<string> lines, string filePath, List<Diagnostic> diagnostics)
    {
        var doc = new ACIRDocument();
        var i = 0;

        // Skip empty lines and comments
        while (i < lines.Count && IsEmptyOrComment(lines[i]))
            i++;

        // Version line
        if (i < lines.Count && lines[i].StartsWith("ACIR"))
        {
            var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[1], out var ver))
                {
                    doc = new ACIRDocument { Version = ver };
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        $"ACIR0002: Invalid version declaration '{lines[i].Trim()}' - expected 'ACIR <number>'",
                        DiagnosticSeverity.Error,
                        filePath,
                        i + 1,
                        1));
                }
            }
            i++;
        }
        else if (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
        {
            diagnostics.Add(new Diagnostic(
                "ACIR0002: Missing version declaration - expected 'ACIR 1' at start of file",
                DiagnosticSeverity.Warning,
                filePath,
                1,
                1));
        }

        // Parse remaining content
        while (i < lines.Count)
        {
            var line = lines[i].TrimEnd();
            if (IsEmptyOrComment(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("bundle "))
            {
                i = ParseBundleWithDiagnostics(lines, i, doc.BundleTypes, filePath, diagnostics);
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
    private static int ParseBundleWithDiagnostics(IReadOnlyList<string> lines, int start, List<BundleType> bundles, string filePath, List<Diagnostic> diagnostics)
    {
        var line = lines[start].Trim();
        var match = Regex.Match(line, @"^bundle\s+(\w+)\s*:");
        if (!match.Success)
        {
            diagnostics.Add(new Diagnostic(
                $"ACIR0003: Malformed bundle declaration '{line}'",
                DiagnosticSeverity.Error,
                filePath,
                start + 1,
                1));
            return start + 1;
        }

        var bundle = new BundleType { Name = match.Groups[1].Value };
        var i = start + 1;

        while (i < lines.Count)
        {
            var fieldLine = lines[i];
            if (!fieldLine.StartsWith("  ") || string.IsNullOrWhiteSpace(fieldLine))
                break;

            var fieldMatch = Regex.Match(fieldLine.Trim(), @"^(\w+)\s*:\s*(\w+)");
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
    /// Parses a circuit definition with diagnostic collection.
    /// </summary>
    private static int ParseCircuitWithDiagnostics(IReadOnlyList<string> lines, int start, List<Circuit> circuits, string filePath, List<Diagnostic> diagnostics)
    {
        var line = lines[start].Trim();
        var match = Regex.Match(line, @"^circuit\s+(\w+)(?:\s*:\s*(.+))?$");
        if (!match.Success)
        {
            diagnostics.Add(new Diagnostic(
                $"ACIR0003: Malformed circuit declaration '{line}'",
                DiagnosticSeverity.Error,
                filePath,
                start + 1,
                1));
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

        FillBlock? currentFill = null;
        ConstraintsBlock? currentConstraints = null;
        HarnessBlock? currentHarness = null;
        BenchesBlock? currentBenches = null;
        string? constraintSubSection = null;

        while (i < lines.Count)
        {
            var currentLine = lines[i];
            if (!currentLine.StartsWith("  ") && !string.IsNullOrWhiteSpace(currentLine))
                break;

            var trimmed = currentLine.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
            {
                i++;
                continue;
            }

            // Check for constraint subsection headers
            if (currentLine.StartsWith("    ") && !currentLine.StartsWith("      ") && currentConstraints is not null)
            {
                if (trimmed == "numeric:")
                {
                    constraintSubSection = "numeric";
                    i++;
                    continue;
                }
                else if (trimmed == "tech:")
                {
                    constraintSubSection = "tech";
                    i++;
                    continue;
                }
                else if (trimmed == "measure:")
                {
                    constraintSubSection = "measure";
                    i++;
                    continue;
                }
                else if (trimmed == "graph:")
                {
                    constraintSubSection = "graph";
                    i++;
                    continue;
                }
            }

            // Check for constraint content
            if (currentLine.StartsWith("      ") && currentConstraints is not null && constraintSubSection is not null)
            {
                ParseConstraintContent(trimmed, currentConstraints, constraintSubSection);
            }
            // Check for section content
            else if (currentLine.StartsWith("    "))
            {
                if (currentFill is not null)
                {
                    ParseFillContentWithDiagnostics(trimmed, currentFill, filePath, i + 1, diagnostics);
                }
                else if (currentHarness is not null)
                {
                    ParseHarnessContentWithDiagnostics(trimmed, currentHarness, filePath, i + 1, diagnostics);
                }
                else if (currentBenches is not null)
                {
                    ParseBenchContent(trimmed, currentBenches);
                }
            }
            // Section headers
            else if (trimmed == "fill:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentFill = new FillBlock();
                currentConstraints = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (trimmed == "constraints:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentConstraints = new ConstraintsBlock();
                currentFill = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (trimmed == "harness:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentHarness = new HarnessBlock();
                currentFill = null;
                currentConstraints = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (trimmed == "benches:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentBenches = new BenchesBlock();
                currentFill = null;
                currentConstraints = null;
                currentHarness = null;
                constraintSubSection = null;
            }
            // Top-level declarations
            else if (trimmed.StartsWith("level "))
            {
                var levelStr = trimmed[6..].Trim();
                level = ParseLevel(levelStr);
            }
            else if (trimmed.StartsWith("supply "))
            {
                supplies.Add(trimmed[7..].Trim());
            }
            else if (trimmed.StartsWith("ground "))
            {
                grounds.Add(trimmed[7..].Trim());
            }
            else if (trimmed.StartsWith("port "))
            {
                var portMatch = Regex.Match(trimmed, @"^port\s+(\w+)\s*:\s*(\w+)");
                if (portMatch.Success)
                {
                    ports.Add(new PortDeclaration
                    {
                        Name = portMatch.Groups[1].Value,
                        Type = portMatch.Groups[2].Value
                    });
                }
            }

            i++;
        }

        SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
            currentFill, currentConstraints, currentHarness, currentBenches);

        var circuit = new Circuit
        {
            Name = name,
            Level = level,
            Traits = traits,
            Supplies = supplies,
            Grounds = grounds,
            Ports = ports,
            Fill = fillBlock,
            Constraints = constraintsBlock,
            Harness = harnessBlock,
            Benches = benchesBlock
        };

        circuits.Add(circuit);
        return i;
    }

    /// <summary>
    /// Parses fill content with diagnostic collection.
    /// </summary>
    private static void ParseFillContentWithDiagnostics(string line, FillBlock fill, string filePath, int lineNumber, List<Diagnostic> diagnostics)
    {
        if (line.StartsWith("net "))
        {
            var match = Regex.Match(line, @"^net\s+(\w+)\s*:\s*(\w+)");
            if (match.Success)
            {
                fill.Nets.Add(new NetDeclaration
                {
                    Id = match.Groups[1].Value,
                    Domain = match.Groups[2].Value
                });
            }
        }
        else if (line.StartsWith("nmos ") || line.StartsWith("pmos ") ||
                 line.StartsWith("resistor ") || line.StartsWith("capacitor ") ||
                 line.StartsWith("inductor ") || line.StartsWith("diode "))
        {
            var device = ParseDeviceWithDiagnostics(line, filePath, lineNumber, diagnostics);
            if (device is not null)
                fill.Devices.Add(device);
        }
    }

    /// <summary>
    /// Parses a device declaration with diagnostic collection.
    /// </summary>
    private static DeviceDeclaration? ParseDeviceWithDiagnostics(string line, string filePath, int lineNumber, List<Diagnostic> diagnostics)
    {
        // Pattern: deviceType id (bindings) : params [pdkDevice]
        var deviceMatch = Regex.Match(line,
            @"^(nmos|pmos|resistor|capacitor|inductor|diode)\s+([^\s(]+)\s*\(([^)]+)\)\s*:\s*(.+)$");
        if (!deviceMatch.Success)
        {
            diagnostics.Add(new Diagnostic(
                $"ACIR0004: Invalid device declaration syntax '{line}'",
                DiagnosticSeverity.Error,
                filePath,
                lineNumber,
                1));
            return null;
        }

        var bindings = new Dictionary<string, string>();
        var deviceParams = new Dictionary<string, string>();
        string? pdkDevice = null;

        // Parse bindings
        var bindingsStr = deviceMatch.Groups[3].Value;
        foreach (var binding in bindingsStr.Split(','))
        {
            var bindMatch = Regex.Match(binding.Trim(), @"(\w+)->(\w+)");
            if (bindMatch.Success)
            {
                bindings[bindMatch.Groups[1].Value] = bindMatch.Groups[2].Value;
            }
            else if (!string.IsNullOrWhiteSpace(binding))
            {
                diagnostics.Add(new Diagnostic(
                    $"ACIR0005: Malformed binding syntax '{binding.Trim()}' - expected 'TERMINAL->NET'",
                    DiagnosticSeverity.Warning,
                    filePath,
                    lineNumber,
                    1));
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
            PdkDevice = pdkDevice
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
        var match = Regex.Match(line, @"^bundle\s+(\w+)\s*:");
        if (!match.Success) return start + 1;

        var bundle = new BundleType { Name = match.Groups[1].Value };
        var i = start + 1;

        while (i < lines.Count)
        {
            var fieldLine = lines[i];
            if (!fieldLine.StartsWith("  ") || string.IsNullOrWhiteSpace(fieldLine))
                break;

            var fieldMatch = Regex.Match(fieldLine.Trim(), @"^(\w+)\s*:\s*(\w+)");
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
        var match = Regex.Match(line, @"^circuit\s+(\w+)(?:\s*:\s*(.+))?$");
        if (!match.Success) return start + 1;

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
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
            {
                i++;
                continue;
            }

            // Check for constraint subsection headers (4 spaces, within constraints)
            if (currentLine.StartsWith("    ") && !currentLine.StartsWith("      ") && currentConstraints is not null)
            {
                if (trimmed == "numeric:")
                {
                    constraintSubSection = "numeric";
                    i++;
                    continue;
                }
                else if (trimmed == "tech:")
                {
                    constraintSubSection = "tech";
                    i++;
                    continue;
                }
                else if (trimmed == "measure:")
                {
                    constraintSubSection = "measure";
                    i++;
                    continue;
                }
                else if (trimmed == "graph:")
                {
                    constraintSubSection = "graph";
                    i++;
                    continue;
                }
            }

            // Check for constraint content (6 spaces = inside a constraint subsection)
            if (currentLine.StartsWith("      ") && currentConstraints is not null && constraintSubSection is not null)
            {
                ParseConstraintContent(trimmed, currentConstraints, constraintSubSection);
            }
            // Check for section content (4 spaces = inside a section block)
            else if (currentLine.StartsWith("    "))
            {
                if (currentFill is not null)
                {
                    ParseFillContent(trimmed, currentFill);
                }
                else if (currentHarness is not null)
                {
                    ParseHarnessContent(trimmed, currentHarness);
                }
                else if (currentBenches is not null)
                {
                    ParseBenchContent(trimmed, currentBenches);
                }
            }
            // Section headers (2 spaces) - preserve previous section before starting new one
            else if (trimmed == "fill:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentFill = new FillBlock();
                currentConstraints = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (trimmed == "constraints:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentConstraints = new ConstraintsBlock();
                currentFill = null;
                currentHarness = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (trimmed == "harness:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentHarness = new HarnessBlock();
                currentFill = null;
                currentConstraints = null;
                currentBenches = null;
                constraintSubSection = null;
            }
            else if (trimmed == "benches:")
            {
                SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
                    currentFill, currentConstraints, currentHarness, currentBenches);
                currentBenches = new BenchesBlock();
                currentFill = null;
                currentConstraints = null;
                currentHarness = null;
                constraintSubSection = null;
            }
            // Top-level declarations (2 spaces, not inside a section)
            else if (trimmed.StartsWith("level "))
            {
                var levelStr = trimmed[6..].Trim();
                level = ParseLevel(levelStr);
            }
            else if (trimmed.StartsWith("supply "))
            {
                supplies.Add(trimmed[7..].Trim());
            }
            else if (trimmed.StartsWith("ground "))
            {
                grounds.Add(trimmed[7..].Trim());
            }
            else if (trimmed.StartsWith("port "))
            {
                var portMatch = Regex.Match(trimmed, @"^port\s+(\w+)\s*:\s*(\w+)");
                if (portMatch.Success)
                {
                    ports.Add(new PortDeclaration
                    {
                        Name = portMatch.Groups[1].Value,
                        Type = portMatch.Groups[2].Value
                    });
                }
            }

            i++;
        }

        SaveCurrentSection(ref fillBlock, ref constraintsBlock, ref harnessBlock, ref benchesBlock,
            currentFill, currentConstraints, currentHarness, currentBenches);

        var circuit = new Circuit
        {
            Name = name,
            Level = level,
            Traits = traits,
            Supplies = supplies,
            Grounds = grounds,
            Ports = ports,
            Fill = fillBlock,
            Constraints = constraintsBlock,
            Harness = harnessBlock,
            Benches = benchesBlock
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
        BenchesBlock? currentBenches)
    {
        if (currentFill is not null) fillBlock = currentFill;
        if (currentConstraints is not null) constraintsBlock = currentConstraints;
        if (currentHarness is not null) harnessBlock = currentHarness;
        if (currentBenches is not null) benchesBlock = currentBenches;
    }

    /// <summary>
    /// Parses a constraint line within a constraints subsection.
    /// </summary>
    /// <param name="line">Trimmed line content.</param>
    /// <param name="constraints">ConstraintsBlock to populate.</param>
    /// <param name="subSection">Current subsection: "numeric", "tech", "measure", or "graph".</param>
    private static void ParseConstraintContent(string line, ConstraintsBlock constraints, string subSection)
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
        var match = Regex.Match(line, @"^(\w+)\s*:\s*(\w+)(?:\s*@\s*(\w+))?\s*(>=|<=|==|>|<)\s*(\S+)\s+(\w+)$");
        if (!match.Success) return;

        constraints.Numeric.Add(new NumericConstraint
        {
            Id = match.Groups[1].Value,
            Metric = match.Groups[2].Value,
            Node = match.Groups[3].Success ? match.Groups[3].Value : null,
            Op = match.Groups[4].Value,
            Value = match.Groups[5].Value,
            Unit = match.Groups[6].Value
        });
    }

    /// <summary>
    /// Parses a tech constraint line.
    /// Format: id : Param op value unit on scope
    /// Example: t_lmin : L >= 180n m on *
    /// </summary>
    private static void ParseTechConstraint(string line, ConstraintsBlock constraints)
    {
        // Pattern: id : Param op value unit on scope
        var match = Regex.Match(line, @"^(\w+)\s*:\s*(\w+)\s*(>=|<=|==|>|<)\s*(\S+)\s+(\w+)\s+on\s+(\S+)$");
        if (!match.Success) return;

        constraints.Tech.Add(new TechConstraint
        {
            Id = match.Groups[1].Value,
            Param = match.Groups[2].Value,
            Op = match.Groups[3].Value,
            Value = match.Groups[4].Value,
            Unit = match.Groups[5].Value,
            Scope = match.Groups[6].Value
        });
    }

    /// <summary>
    /// Parses a measure intent line.
    /// Format: id : BenchName Metric @ Node
    /// Example: m_gbw : SEOpAmpACBench GainBandwidth @ OUT
    /// </summary>
    private static void ParseMeasureIntent(string line, ConstraintsBlock constraints)
    {
        // Pattern: id : BenchName Metric @ Node  OR  id : BenchName Metric (no node)
        var match = Regex.Match(line, @"^(\w+)\s*:\s*(\w+)\s+(\w+)(?:\s*@\s*(\w+))?$");
        if (!match.Success) return;

        constraints.Measure.Add(new MeasureIntent
        {
            Id = match.Groups[1].Value,
            Bench = match.Groups[2].Value,
            Metric = match.Groups[3].Value,
            Node = match.Groups[4].Success ? match.Groups[4].Value : null
        });
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
            var match = Regex.Match(line, @"^net\s+(\w+)\s*:\s*(\w+)");
            if (match.Success)
            {
                fill.Nets.Add(new NetDeclaration
                {
                    Id = match.Groups[1].Value,
                    Domain = match.Groups[2].Value
                });
            }
        }
        else if (line.StartsWith("nmos ") || line.StartsWith("pmos ") ||
                 line.StartsWith("resistor ") || line.StartsWith("capacitor ") ||
                 line.StartsWith("inductor ") || line.StartsWith("diode "))
        {
            var device = ParseDevice(line);
            if (device is not null)
                fill.Devices.Add(device);
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
        var deviceMatch = Regex.Match(line,
            @"^(nmos|pmos|resistor|capacitor|inductor|diode)\s+([^\s(]+)\s*\(([^)]+)\)\s*:\s*(.+)$");
        if (!deviceMatch.Success) return null;

        var bindings = new Dictionary<string, string>();
        var deviceParams = new Dictionary<string, string>();
        string? pdkDevice = null;

        // Parse bindings: B->GND, D->OUT, G->IN, S->tnode
        var bindingsStr = deviceMatch.Groups[3].Value;
        foreach (var binding in bindingsStr.Split(','))
        {
            var bindMatch = Regex.Match(binding.Trim(), @"(\w+)->(\w+)");
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
            PdkDevice = pdkDevice
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
    private static void ParseHarnessContentWithDiagnostics(string line, HarnessBlock harness, string filePath, int lineNumber, List<Diagnostic> diagnostics)
    {
        ParseHarnessContent(line, harness);
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
            var match = Regex.Match(line, @"^supply\s+(\w+)\s*=\s*(.+)$");
            if (match.Success)
            {
                harness.Supplies.Add(new SupplyValue
                {
                    Net = match.Groups[1].Value,
                    Value = match.Groups[2].Value.Trim()
                });
            }
        }
        else if (line.StartsWith("load "))
        {
            var match = Regex.Match(line, @"^load\s+(\w+)\s+C=([^\s]+)");
            if (match.Success)
            {
                harness.Loads.Add(new LoadValue
                {
                    Net = match.Groups[1].Value,
                    C = match.Groups[2].Value
                });
            }
        }
        else if (line.StartsWith("source "))
        {
            var match = Regex.Match(line, @"^source\s+(\w+)\s+Z=([^\s]+)");
            if (match.Success)
            {
                harness.Sources.Add(new SourceValue
                {
                    Net = match.Groups[1].Value,
                    Z = match.Groups[2].Value
                });
            }
        }
        else if (line.StartsWith("bias "))
        {
            var match = Regex.Match(line, @"^bias\s+(\w+)\s*=\s*(.+)$");
            if (match.Success)
            {
                harness.Biases.Add(new BiasValue
                {
                    Net = match.Groups[1].Value,
                    Value = match.Groups[2].Value.Trim()
                });
            }
        }
        else if (line.StartsWith("sweep "))
        {
            // Pattern: sweep ConditionName [start:step:stop] or [start:stop] or [Auto]
            var match = Regex.Match(line, @"^sweep\s+(\w+)\s+\[([^\]]+)\]$");
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
    /// Parses a sweep range specification into a SweepCondition.
    /// Supports formats: [start:step:stop], [start:stop], or [Auto].
    /// </summary>
    /// <param name="name">Sweep condition name (e.g., "InputDCBias").</param>
    /// <param name="rangeSpec">Range specification string.</param>
    /// <returns>Parsed SweepCondition, or null if parsing fails.</returns>
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
                Step = null
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
                Start = parts[0].Trim(),
                Stop = parts[1].Trim(),
                Step = null,
                IsAuto = false
            };
        }
        else if (parts.Length == 3)
        {
            // Explicit step format: [start:step:stop]
            return new SweepCondition
            {
                Name = name,
                Start = parts[0].Trim(),
                Step = parts[1].Trim(),
                Stop = parts[2].Trim(),
                IsAuto = false
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
    /// <returns>Parsed ACIR level, defaulting to ML if unrecognized.</returns>
    private static ACIRLevel ParseLevel(string level)
    {
        return level.ToUpperInvariant() switch
        {
            "HL" => ACIRLevel.HL,
            "ML" => ACIRLevel.ML,
            "EL" => ACIRLevel.EL,
            _ => ACIRLevel.ML
        };
    }

    /// <summary>
    /// Determines if a line is empty or a comment (starts with ';').
    /// </summary>
    /// <param name="line">Line to check.</param>
    /// <returns>True if the line should be skipped.</returns>
    private static bool IsEmptyOrComment(string line)
    {
        var trimmed = line.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";");
    }
}
