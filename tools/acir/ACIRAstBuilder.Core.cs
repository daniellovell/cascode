using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;
using Cascode.Parser;

namespace Cascode.ACIR;

/// <summary>
/// Transforms ANTLR parse tree into an ACIRDocument AST.
/// </summary>
internal sealed partial class ACIRAstBuilder
{
    private readonly string _filePath;
    private readonly List<Diagnostic> _diagnostics;

    /// <summary>Creates a builder for a specific source file.</summary>
    /// <param name="filePath">Path to the parsed document.</param>
    /// <param name="diagnostics">Diagnostic sink to populate.</param>
    public ACIRAstBuilder(string filePath, List<Diagnostic> diagnostics)
    {
        _filePath = filePath;
        _diagnostics = diagnostics;
    }

    /// <summary>Builds an ACIR document from the parsed root context.</summary>
    /// <param name="ctx">Root document context.</param>
    /// <returns>The constructed ACIR document.</returns>
    public ACIRDocument Build(ACIRParser.DocumentContext ctx)
    {
        var versionCtx = ctx.versionDecl();
        int major,
            minor;

        if (versionCtx != null)
        {
            var versionText = versionCtx.NUMBER().GetText();
            var versionParts = versionText.Split('.');
            major = int.Parse(versionParts[0]);
            minor = versionParts.Length > 1 ? int.Parse(versionParts[1]) : 0;

            // Validate version
            if (major != ACIRVersion.Major)
            {
                AddDiagnostic(
                    versionCtx,
                    DiagnosticSeverity.Error,
                    $"ACIR0007: ACIR major version {major} not supported. Expected major version {ACIRVersion.Major}."
                );
            }
        }
        else
        {
            // Empty document - use current version as default
            major = ACIRVersion.Major;
            minor = ACIRVersion.Minor;

            // Warn about missing version if document has content
            if (ctx.circuit().Length > 0 || ctx.traitDef().Length > 0 || ctx.bundleDef().Length > 0)
            {
                AddDiagnostic(
                    1,
                    1,
                    DiagnosticSeverity.Warning,
                    "ACIR0002: Missing version declaration; assuming current version"
                );
            }
        }

        return new ACIRDocument
        {
            VersionMajor = major,
            VersionMinor = minor,
            BundleTypes = ctx.bundleDef().Select(BuildBundle).ToList(),
            Traits = ctx.traitDef().Select(BuildTrait).ToList(),
            BenchDefinitions = ctx.benchDef().Select(BuildBenchDefinition).ToList(),
            Circuits = ctx.circuit().Select(BuildCircuit).ToList(),
        };
    }

    /// <summary>Builds a bundle type definition.</summary>
    /// <param name="ctx">Bundle definition context.</param>
    /// <returns>Bundle type definition.</returns>
    private BundleType BuildBundle(ACIRParser.BundleDefContext ctx)
    {
        var fields = new Dictionary<string, string>();
        foreach (var fieldCtx in ctx.bundleField())
        {
            var fieldName = fieldCtx.IDENT(0).GetText();
            var fieldType = fieldCtx.IDENT(1).GetText();
            fields[fieldName] = fieldType;
        }

        return new BundleType { Name = ctx.IDENT().GetText(), Fields = fields };
    }

    /// <summary>Builds a trait definition including ports and connectors.</summary>
    /// <param name="ctx">Trait definition context.</param>
    /// <returns>Trait definition.</returns>
    private TraitDefinition BuildTrait(ACIRParser.TraitDefContext ctx)
    {
        var trait = new TraitDefinition { Name = ctx.IDENT().GetText() };

        foreach (var memberCtx in ctx.traitMember())
        {
            switch (memberCtx)
            {
                case ACIRParser.TraitPortContext portCtx:
                    trait.Ports.Add(
                        new PortDeclaration
                        {
                            Name = BuildPortName(portCtx.portName()),
                            Type = BuildPortType(portCtx.portType()),
                        }
                    );
                    break;

                case ACIRParser.TraitConnectorsContext connectorsCtx:
                    foreach (var connDefCtx in connectorsCtx.connectorDef())
                    {
                        var connector = new TraitConnector
                        {
                            TargetTrait = connDefCtx.IDENT().GetText(),
                        };
                        foreach (var mappingCtx in connDefCtx.connectorMapping())
                        {
                            var pins = mappingCtx.pinRef();
                            connector.Mappings.Add(
                                new ConnectorMapping
                                {
                                    SourcePort = BuildPinRef(pins[0]),
                                    TargetPort = BuildPinRef(pins[1]),
                                }
                            );
                        }
                        trait.Connectors.Add(connector);
                    }
                    break;
            }
        }

        return trait;
    }

    /// <summary>Builds a bench definition from its parse context.</summary>
    private BenchDefinition BuildBenchDefinition(ACIRParser.BenchDefContext ctx)
    {
        var name = ctx.IDENT(0).GetText();
        var trait = ctx.IDENT(1).GetText();
        string? builtin = null;
        var config = new Dictionary<string, string>();
        var outputs = new List<string>();

        foreach (var memberCtx in ctx.benchMember())
        {
            if (memberCtx.BUILTIN_KW() != null)
            {
                builtin = memberCtx.IDENT().GetText();
                continue;
            }

            if (memberCtx.CONFIG_KW() != null)
            {
                foreach (var entryCtx in memberCtx.benchConfigEntry())
                {
                    var key = entryCtx.IDENT(0).GetText();
                    var value = BuildBenchConfigValue(entryCtx);
                    if (config.ContainsKey(key))
                    {
                        AddDiagnostic(
                            entryCtx,
                            DiagnosticSeverity.Error,
                            $"Duplicate bench config key '{key}'"
                        );
                        continue;
                    }
                    config[key] = value;
                }
                continue;
            }

            if (memberCtx.OUTPUTS_KW() != null)
            {
                foreach (var outputCtx in memberCtx.benchOutput())
                {
                    outputs.Add(outputCtx.IDENT().GetText());
                }
            }
        }

        if (string.IsNullOrWhiteSpace(builtin))
        {
            AddDiagnostic(
                ctx,
                DiagnosticSeverity.Error,
                $"Bench '{name}' must declare a builtin template."
            );
        }

        return new BenchDefinition
        {
            Name = name,
            Trait = trait,
            Builtin = builtin,
            Config = config,
            Outputs = outputs,
        };
    }

    private static string BuildBenchConfigValue(ACIRParser.BenchConfigEntryContext ctx)
    {
        if (ctx.STRING() != null)
        {
            return Unquote(ctx.STRING().GetText());
        }

        var identNodes = ctx.IDENT();
        if (identNodes.Length > 1)
        {
            return identNodes[1].GetText();
        }

        return ctx.NUMBER()?.GetText() ?? ctx.QUANTITY()?.GetText() ?? string.Empty;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }

    /// <summary>Parses a level keyword into the ACIR level enum.</summary>
    /// <param name="ctx">Level value context.</param>
    /// <returns>Parsed ACIR level.</returns>
    private static ACIRLevel ParseLevel(ACIRParser.LevelValueContext ctx)
    {
        if (ctx.HL_KW() != null)
            return ACIRLevel.HL;
        if (ctx.ML_KW() != null)
            return ACIRLevel.ML;
        return ACIRLevel.EL;
    }

    /// <summary>Builds a port name, including dotted and indexed forms.</summary>
    /// <param name="ctx">Port name context.</param>
    /// <returns>Normalized port name.</returns>
    private static string BuildPortName(ACIRParser.PortNameContext ctx)
    {
        // Port names can be dotted (e.g., OUT.P)
        var name = string.Join(".", ctx.IDENT().Select(i => i.GetText()));
        if (ctx.NUMBER() != null)
        {
            return $"{name}[{ctx.NUMBER().GetText()}]";
        }
        if (ctx.STAR() != null)
        {
            return $"{name}[*]";
        }
        return name;
    }

    /// <summary>Resolves the port type token into its textual form.</summary>
    /// <param name="ctx">Port type context.</param>
    /// <returns>Port type name.</returns>
    private static string BuildPortType(ACIRParser.PortTypeContext ctx)
    {
        // portType can be IDENT or a keyword (BIAS_KW, SUPPLY_KW, GROUND_KW)
        if (ctx.IDENT() != null)
        {
            return ctx.IDENT().GetText();
        }
        // For keywords, just get the text
        return ctx.GetText();
    }

    /// <summary>Builds a pin reference string from identifiers and indexers.</summary>
    /// <param name="ctx">Pin reference context.</param>
    /// <returns>Normalized pin reference.</returns>
    private static string BuildPinRef(ACIRParser.PinRefContext ctx)
    {
        return ctx.GetText();
    }

    /// <summary>Adds a diagnostic anchored to a parse context.</summary>
    /// <param name="ctx">Context to derive line/column from.</param>
    /// <param name="severity">Diagnostic severity.</param>
    /// <param name="message">Diagnostic message.</param>
    private void AddDiagnostic(ParserRuleContext ctx, DiagnosticSeverity severity, string message)
    {
        var line = ctx.Start?.Line ?? 1;
        var column = (ctx.Start?.Column ?? 0) + 1;
        _diagnostics.Add(new Diagnostic(message, severity, _filePath, line, column));
    }

    /// <summary>Adds a diagnostic anchored to a specific line and column.</summary>
    /// <param name="line">1-based line number.</param>
    /// <param name="column">1-based column number.</param>
    /// <param name="severity">Diagnostic severity.</param>
    /// <param name="message">Diagnostic message.</param>
    private void AddDiagnostic(int line, int column, DiagnosticSeverity severity, string message)
    {
        _diagnostics.Add(new Diagnostic(message, severity, _filePath, line, column));
    }
}
