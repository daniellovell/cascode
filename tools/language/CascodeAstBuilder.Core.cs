using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;

namespace Cascode.Language;

/// <summary>
/// Transforms ANTLR parse tree into an CascodeDocument AST.
/// </summary>
internal sealed partial class CascodeAstBuilder
{
    private readonly string _filePath;
    private readonly List<Diagnostic> _diagnostics;

    /// <summary>Creates a builder for a specific source file.</summary>
    /// <param name="filePath">Path to the parsed document.</param>
    /// <param name="diagnostics">Diagnostic sink to populate.</param>
    public CascodeAstBuilder(string filePath, List<Diagnostic> diagnostics)
    {
        _filePath = filePath;
        _diagnostics = diagnostics;
    }

    /// <summary>Builds an Cascode document from the parsed root context.</summary>
    /// <param name="ctx">Root document context.</param>
    /// <returns>The constructed Cascode document.</returns>
    public CascodeDocument Build(CascodeParser.DocumentContext ctx)
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
            if (major != CascodeVersion.Major)
            {
                AddDiagnostic(
                    versionCtx,
                    DiagnosticSeverity.Error,
                    $"CAS0007: Cascode major version {major} not supported. Expected major version {CascodeVersion.Major}."
                );
            }
        }
        else
        {
            // Empty document - use current version as default
            major = CascodeVersion.Major;
            minor = CascodeVersion.Minor;

            // Warn about missing version if document has content
            if (ctx.topLevelDecl().Length > 0)
            {
                AddDiagnostic(
                    1,
                    1,
                    DiagnosticSeverity.Warning,
                    "CAS0002: Missing version declaration; assuming current version"
                );
            }
        }

        var bundles = new List<BundleType>();
        var traits = new List<TraitDefinition>();
        var benches = new List<BenchDefinition>();
        var primitives = new List<PrimitiveDefinition>();
        var circuits = new List<Circuit>();

        foreach (var decl in ctx.topLevelDecl())
        {
            if (decl.bundleDef() is not null)
            {
                bundles.Add(BuildBundle(decl.bundleDef()));
                continue;
            }

            if (decl.interfaceDef() is not null)
            {
                traits.Add(BuildTrait(decl.interfaceDef()));
                continue;
            }

            if (decl.benchDef() is not null)
            {
                benches.Add(BuildBenchDefinition(decl.benchDef()));
                continue;
            }

            if (decl.primitiveDef() is not null)
            {
                primitives.Add(BuildPrimitive(decl.primitiveDef()));
                continue;
            }

            if (decl.circuit() is not null)
            {
                circuits.Add(BuildCircuit(decl.circuit()));
            }
        }

        return new CascodeDocument
        {
            VersionMajor = major,
            VersionMinor = minor,
            BundleTypes = bundles,
            Traits = traits,
            BenchDefinitions = benches,
            Primitives = primitives,
            Circuits = circuits,
        };
    }

    /// <summary>Builds a bundle type definition.</summary>
    /// <param name="ctx">Bundle definition context.</param>
    /// <returns>Bundle type definition.</returns>
    private BundleType BuildBundle(CascodeParser.BundleDefContext ctx)
    {
        var fields = new Dictionary<string, string>();
        foreach (var fieldCtx in ctx.bundleField())
        {
            var fieldName = fieldCtx.IDENT(0).GetText();
            var fieldType = fieldCtx.IDENT(1).GetText();
            fields[fieldName] = fieldType;
        }

        return new BundleType { Name = ctx.name.Text, Fields = fields };
    }

    /// <summary>Builds an interface definition including ports and connectors.</summary>
    /// <param name="ctx">Interface definition context.</param>
    /// <returns>Trait definition.</returns>
    private TraitDefinition BuildTrait(CascodeParser.InterfaceDefContext ctx)
    {
        var trait = new TraitDefinition { Name = ctx.name.Text };

        foreach (var memberCtx in ctx.interfaceMember())
        {
            switch (memberCtx)
            {
                case CascodeParser.InterfacePortContext portCtx:
                    trait.Ports.Add(
                        new PortDeclaration
                        {
                            Direction = BuildPortDirection(portCtx.direction()),
                            Name = BuildPortName(portCtx.portName()),
                            Type = BuildPortType(portCtx.portType()),
                        }
                    );
                    break;

                case CascodeParser.InterfaceConnectorsContext connectorsCtx:
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
    private BenchDefinition BuildBenchDefinition(CascodeParser.BenchDefContext ctx)
    {
        var name = ctx.name.Text;
        var interfaceName = ctx.@interface.Text;
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
            Trait = interfaceName,
            Builtin = builtin,
            Config = config,
            Outputs = outputs,
        };
    }

    /// <summary>Builds a primitive definition from its parse context.</summary>
    private PrimitiveDefinition BuildPrimitive(CascodeParser.PrimitiveDefContext ctx)
    {
        var kind = ctx.DEVICE_TYPE().GetText();
        var name = ctx.name.Text;

        var sizeParam = string.Empty;
        if (ctx.paramList() != null)
        {
            foreach (var paramCtx in ctx.paramList().paramDecl())
            {
                if (paramCtx.SIZE_KW() is null)
                {
                    AddDiagnostic(
                        paramCtx,
                        DiagnosticSeverity.Error,
                        $"Primitive '{name}' may only declare size parameters."
                    );
                    continue;
                }

                if (!string.IsNullOrEmpty(sizeParam))
                {
                    AddDiagnostic(
                        paramCtx,
                        DiagnosticSeverity.Error,
                        $"Primitive '{name}' must declare exactly one size parameter."
                    );
                }

                sizeParam = paramCtx.sizeName.Text;
            }
        }

        if (string.IsNullOrEmpty(sizeParam))
        {
            AddDiagnostic(
                ctx,
                DiagnosticSeverity.Error,
                $"Primitive '{name}' must declare a size parameter."
            );
        }

        var deviceDirective = ctx.primitiveBody().deviceDirective();
        var deviceKey = Unquote(deviceDirective.STRING().GetText());

        var mappings = new Dictionary<string, string>();
        foreach (var mappingCtx in ctx.primitiveBody().paramsBlock().paramMapping())
        {
            var key = mappingCtx.IDENT().GetText();
            var value = mappingCtx.paramExpr().GetText();
            if (mappings.ContainsKey(key))
            {
                AddDiagnostic(
                    mappingCtx,
                    DiagnosticSeverity.Error,
                    $"Duplicate primitive param mapping '{key}'"
                );
                continue;
            }

            mappings[key] = value;
        }

        return new PrimitiveDefinition
        {
            Kind = kind,
            Name = name,
            Device = deviceKey,
            SizeParameter = sizeParam,
            Params = mappings,
        };
    }

    private static string BuildBenchConfigValue(CascodeParser.BenchConfigEntryContext ctx)
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

    /// <summary>Parses a level keyword into the Cascode level enum.</summary>
    /// <param name="ctx">Level value context.</param>
    /// <returns>Parsed Cascode level.</returns>
    private static CascodeLevel ParseLevel(CascodeParser.LevelValueContext ctx)
    {
        if (ctx.HL_KW() != null)
            return CascodeLevel.HL;
        if (ctx.ML_KW() != null)
            return CascodeLevel.ML;
        return CascodeLevel.EL;
    }

    /// <summary>Builds a port name, including dotted and indexed forms.</summary>
    /// <param name="ctx">Port name context.</param>
    /// <returns>Normalized port name.</returns>
    private static string BuildPortName(CascodeParser.PortNameContext ctx)
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

    private static PortDirection BuildPortDirection(CascodeParser.DirectionContext ctx)
    {
        if (ctx.INPUT_KW() != null)
        {
            return PortDirection.Input;
        }

        if (ctx.OUTPUT_KW() != null)
        {
            return PortDirection.Output;
        }

        return PortDirection.Io;
    }

    /// <summary>Resolves the port type token into its textual form.</summary>
    /// <param name="ctx">Port type context.</param>
    /// <returns>Port type name.</returns>
    private static string BuildPortType(CascodeParser.PortTypeContext ctx)
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
    private static string BuildPinRef(CascodeParser.PinRefContext ctx)
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
