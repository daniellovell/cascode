using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

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
    /// Reads an ACIR document from a text reader.
    /// </summary>
    /// <param name="reader">Text reader containing ACIR content.</param>
    /// <returns>Parsed ACIR document.</returns>
    public static ACIRDocument Read(TextReader reader)
    {
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }
        return Parse(lines);
    }

    /// <summary>
    /// Parses ACIR text content from a string.
    /// </summary>
    /// <param name="content">ACIR text content.</param>
    /// <returns>Parsed ACIR document.</returns>
    public static ACIRDocument Parse(string content)
    {
        var lines = content.Split('\n');
        return Parse(lines);
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
    /// Parses a circuit definition including all sections (fill, harness, benches).
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
        HarnessBlock? harnessBlock = null;
        BenchesBlock? benchesBlock = null;

        FillBlock? currentFill = null;
        HarnessBlock? currentHarness = null;
        BenchesBlock? currentBenches = null;

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

            // Check for section content first (4 spaces = inside a section block)
            if (currentLine.StartsWith("    "))
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
                if (currentHarness is not null) harnessBlock = currentHarness;
                if (currentBenches is not null) benchesBlock = currentBenches;
                currentFill = new FillBlock();
                currentHarness = null;
                currentBenches = null;
            }
            else if (trimmed == "harness:")
            {
                if (currentFill is not null) fillBlock = currentFill;
                if (currentBenches is not null) benchesBlock = currentBenches;
                currentHarness = new HarnessBlock();
                currentFill = null;
                currentBenches = null;
            }
            else if (trimmed == "benches:")
            {
                if (currentFill is not null) fillBlock = currentFill;
                if (currentHarness is not null) harnessBlock = currentHarness;
                currentBenches = new BenchesBlock();
                currentFill = null;
                currentHarness = null;
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

        if (currentFill is not null)
            fillBlock = currentFill;
        if (currentHarness is not null)
            harnessBlock = currentHarness;
        if (currentBenches is not null)
            benchesBlock = currentBenches;

        var circuit = new Circuit
        {
            Name = name,
            Level = level,
            Traits = traits,
            Supplies = supplies,
            Grounds = grounds,
            Ports = ports,
            Fill = fillBlock,
            Harness = harnessBlock,
            Benches = benchesBlock
        };

        circuits.Add(circuit);
        return i;
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
    /// Parses a line within a harness block (supply, load, or source).
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
