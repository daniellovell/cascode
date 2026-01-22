using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    public ACIRAstBuilder(string filePath, List<Diagnostic> diagnostics)
    {
        _filePath = filePath;
        _diagnostics = diagnostics;
    }

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
            Circuits = ctx.circuit().Select(BuildCircuit).ToList(),
        };
    }

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

    private Circuit BuildCircuit(ACIRParser.CircuitContext ctx)
    {
        var circuit = new Circuit
        {
            Name = ctx.IDENT().GetText(),
            Traits = ctx.traitList()?.IDENT().Select(i => i.GetText()).ToList(),
        };

        // Process circuit members
        FillBlock? fill = null;
        ConstraintsBlock? constraints = null;
        HarnessBlock? harness = null;
        BenchesBlock? benches = null;
        ProvenanceBlock? provenance = null;
        var level = ACIRLevel.ML;
        var isInline = false;
        string? package = null;
        var supplies = new List<string>();
        var grounds = new List<string>();
        var ports = new List<PortDeclaration>();
        var parameters = new List<CircuitParameter>();
        var sizes = new List<SizeDeclaration>();

        foreach (var memberCtx in ctx.circuitMember())
        {
            switch (memberCtx)
            {
                case ACIRParser.LevelDeclContext levelCtx:
                    level = ParseLevel(levelCtx.levelValue());
                    break;

                case ACIRParser.InlineDeclContext:
                    isInline = true;
                    break;

                case ACIRParser.PackageDeclContext pkgCtx:
                    package = BuildQualifiedName(pkgCtx.qualifiedName());
                    break;

                case ACIRParser.SupplyDeclContext supplyCtx:
                    supplies.Add(supplyCtx.IDENT().GetText());
                    break;

                case ACIRParser.GroundDeclContext groundCtx:
                    grounds.Add(groundCtx.IDENT().GetText());
                    break;

                case ACIRParser.PortDeclContext portCtx:
                    ports.Add(
                        new PortDeclaration
                        {
                            Name = BuildPortName(portCtx.portName()),
                            Type = BuildPortType(portCtx.portType()),
                        }
                    );
                    break;

                case ACIRParser.ParamDeclContext paramCtx:
                    parameters.Add(BuildCircuitParameter(paramCtx));
                    break;

                case ACIRParser.SizeDeclContext sizeCtx:
                    sizes.Add(BuildSizeDeclaration(sizeCtx));
                    break;

                case ACIRParser.FillSectionContext fillCtx:
                    fill = BuildFillBlock(fillCtx);
                    break;

                case ACIRParser.ConstraintsSectionContext constraintsCtx:
                    constraints = BuildConstraintsBlock(constraintsCtx);
                    break;

                case ACIRParser.HarnessSectionContext harnessCtx:
                    harness = BuildHarnessBlock(harnessCtx);
                    break;

                case ACIRParser.BenchesSectionContext benchesCtx:
                    benches = BuildBenchesBlock(benchesCtx);
                    break;

                case ACIRParser.ProvenanceSectionContext provCtx:
                    provenance = BuildProvenanceBlock(provCtx);
                    break;
            }
        }

        return new Circuit
        {
            Name = circuit.Name,
            Traits = circuit.Traits,
            Level = level,
            Inline = isInline,
            Package = package,
            Supplies = supplies,
            Grounds = grounds,
            Ports = ports,
            Parameters = parameters,
            Sizes = sizes,
            Fill = fill,
            Constraints = constraints,
            Harness = harness,
            Benches = benches,
            Provenance = provenance,
        };
    }

    private static ACIRLevel ParseLevel(ACIRParser.LevelValueContext ctx)
    {
        if (ctx.HL_KW() != null)
            return ACIRLevel.HL;
        if (ctx.ML_KW() != null)
            return ACIRLevel.ML;
        return ACIRLevel.EL;
    }

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

    private static string BuildQualifiedName(ACIRParser.QualifiedNameContext ctx)
    {
        return string.Join(".", ctx.IDENT().Select(i => i.GetText()));
    }

    private CircuitParameter BuildCircuitParameter(ACIRParser.ParamDeclContext ctx)
    {
        ParamValue? defaultValue = null;
        if (ctx.paramValue() != null)
        {
            defaultValue = BuildParamValue(ctx.paramValue());
        }

        return new CircuitParameter
        {
            Name = ctx.IDENT().GetText(),
            Type = ctx.paramType().GetText(),
            Default = defaultValue,
        };
    }

    private SizeDeclaration BuildSizeDeclaration(ACIRParser.SizeDeclContext ctx)
    {
        SizePack? defaultPack = null;
        if (ctx.sizeLiteral() != null)
        {
            defaultPack = BuildSizeLiteral(ctx.sizeLiteral(), ctx);
        }

        return new SizeDeclaration { Name = ctx.IDENT().GetText(), Default = defaultPack };
    }

    private SizePack BuildSizeLiteral(
        ACIRParser.SizeLiteralContext ctx,
        ParserRuleContext? diagnosticCtx = null
    )
    {
        var entries = new Dictionary<string, string>();
        foreach (var entryCtx in ctx.sizeEntry())
        {
            var key = entryCtx.IDENT().GetText();
            var value =
                entryCtx.NUMBER()?.GetText()
                ?? entryCtx.QUANTITY()?.GetText()
                ?? entryCtx.SYMBOLIC()?.GetText()
                ?? entryCtx.UNSIZED()?.GetText()
                ?? string.Empty;

            if (entries.ContainsKey(key))
            {
                // Use the provided context for diagnostic location, or fall back to the entry context
                AddDiagnostic(
                    diagnosticCtx ?? entryCtx,
                    DiagnosticSeverity.Error,
                    $"Duplicate size key '{key}'"
                );
            }
            else
            {
                entries[key] = value;
            }
        }
        return new SizePack { Entries = entries };
    }

    /// <summary>
    /// Renders a size literal context to a string like "(W=10u, L=180n, M=1)".
    /// </summary>
    private static string RenderSizeLiteral(ACIRParser.SizeLiteralContext ctx)
    {
        var entries = ctx.sizeEntry()
            .Select(e =>
            {
                var key = e.IDENT().GetText();
                var value =
                    e.NUMBER()?.GetText()
                    ?? e.QUANTITY()?.GetText()
                    ?? e.SYMBOLIC()?.GetText()
                    ?? e.UNSIZED()?.GetText()
                    ?? string.Empty;
                return $"{key}={value}";
            });
        return $"({string.Join(", ", entries)})";
    }

    private static ParamValue BuildParamValue(ACIRParser.ParamValueContext ctx)
    {
        if (ctx.SYMBOLIC() != null)
        {
            return new ParamValue { Symbolic = ctx.SYMBOLIC().GetText() };
        }
        if (ctx.QUANTITY() != null)
        {
            return new ParamValue { Numeric = ctx.QUANTITY().GetText() };
        }
        if (ctx.NUMBER() != null)
        {
            return new ParamValue { Numeric = ctx.NUMBER().GetText() };
        }
        if (ctx.STRING() != null)
        {
            // Remove quotes
            var str = ctx.STRING().GetText();
            return new ParamValue { Literal = str[1..^1] };
        }
        if (ctx.IDENT() != null)
        {
            return new ParamValue { Symbolic = ctx.IDENT().GetText() };
        }
        return new ParamValue();
    }

    private FillBlock BuildFillBlock(ACIRParser.FillSectionContext ctx)
    {
        var fill = new FillBlock();

        foreach (var stmtCtx in ctx.fillStatement())
        {
            switch (stmtCtx)
            {
                case ACIRParser.NetDeclContext netCtx:
                    fill.Nets.Add(
                        new NetDeclaration
                        {
                            Id = netCtx.IDENT().GetText(),
                            Domain = BuildPortType(netCtx.portType()),
                        }
                    );
                    break;

                case ACIRParser.DeviceDeclContext deviceCtx:
                    fill.Devices.Add(BuildDevice(deviceCtx));
                    break;

                case ACIRParser.InstanceDeclContext instanceCtx:
                    fill.Instances.Add(BuildInstance(instanceCtx));
                    break;

                case ACIRParser.AttachDeclContext attachCtx:
                    fill.Attaches.Add(BuildAttach(attachCtx));
                    break;

                case ACIRParser.ConnectDeclContext connectCtx:
                    var pins = connectCtx.pinRef();
                    fill.Connections.Add(
                        new ConnectionStatement
                        {
                            From = BuildPinRef(pins[0]),
                            To = BuildPinRef(pins[1]),
                        }
                    );
                    break;
            }
        }

        return fill;
    }

    private DeviceDeclaration BuildDevice(ACIRParser.DeviceDeclContext ctx)
    {
        var deviceType = ctx.DEVICE_TYPE().GetText();
        var deviceId = BuildDeviceId(ctx.deviceId());
        var bindings = BuildBindings(ctx.bindingList());
        var (deviceParams, pdkDevice) = BuildDeviceParams(ctx.deviceParams(), ctx.pdkDeviceName());

        return new DeviceDeclaration
        {
            DeviceType = deviceType,
            Id = deviceId,
            Bindings = bindings,
            Params = deviceParams,
            PdkDevice = pdkDevice,
        };
    }

    private static string BuildDeviceId(ACIRParser.DeviceIdContext ctx)
    {
        // deviceId uses idPart which can be IDENT or various keywords
        return string.Join(".", ctx.idPart().Select(p => p.GetText()));
    }

    // Helper to extract text from idPart (which can be IDENT or keywords)
    private static string BuildIdPart(ACIRParser.IdPartContext ctx)
    {
        return ctx.GetText();
    }

    private static Dictionary<string, string> BuildBindings(ACIRParser.BindingListContext ctx)
    {
        var bindings = new Dictionary<string, string>();
        foreach (var bindingCtx in ctx.binding())
        {
            var pins = bindingCtx.pinRef();
            var from = BuildPinRef(pins[0]);
            var to = BuildPinRef(pins[1]);
            bindings[from] = to;
        }
        return bindings;
    }

    // Build bindings for instance declarations, stripping instance prefix from left side
    // e.g., "dp.VDD->VDD" becomes binding["VDD"] = "VDD"
    private static Dictionary<string, string> BuildBindingsForInstance(
        ACIRParser.BindingListContext ctx,
        string instanceId
    )
    {
        var bindings = new Dictionary<string, string>();
        var prefix = instanceId + ".";
        foreach (var bindingCtx in ctx.binding())
        {
            var pins = bindingCtx.pinRef();
            var from = BuildPinRef(pins[0]);
            var to = BuildPinRef(pins[1]);
            // Strip instance prefix if present (e.g., "dp.VDD" -> "VDD")
            if (from.StartsWith(prefix))
            {
                from = from.Substring(prefix.Length);
            }
            bindings[from] = to;
        }
        return bindings;
    }

    private static string BuildPinRef(ACIRParser.PinRefContext ctx)
    {
        // pinRef uses idPart which can be IDENT or various keywords (e.g., load.D)
        var parts = ctx.idPart().Select(p => p.GetText()).ToList();
        var result = string.Join(".", parts);
        if (ctx.NUMBER() != null)
        {
            result += $"[{ctx.NUMBER().GetText()}]";
        }
        return result;
    }

    private (Dictionary<string, string> Params, string? PdkDevice) BuildDeviceParams(
        ACIRParser.DeviceParamsContext? ctx,
        ACIRParser.PdkDeviceNameContext? pdkCtx
    )
    {
        var deviceParams = new Dictionary<string, string>();
        string? pdkDevice = pdkCtx?.GetText();

        if (ctx == null)
        {
            return (deviceParams, pdkDevice);
        }

        foreach (var paramCtx in ctx.deviceParam())
        {
            if (paramCtx.SIZE_KW() != null)
            {
                // Handle size=(...) or size=SizeName
                if (paramCtx.sizeLiteral() != null)
                {
                    // Validate the size literal (for duplicate key checking)
                    BuildSizeLiteral(paramCtx.sizeLiteral(), paramCtx);
                    // Store as packed string for EmissionValidator compatibility
                    deviceParams["size"] = RenderSizeLiteral(paramCtx.sizeLiteral());
                }
                else if (paramCtx.IDENT() != null)
                {
                    deviceParams["size"] = paramCtx.IDENT().GetText();
                }
            }
            else if (paramCtx.IDENT() != null)
            {
                var key = paramCtx.IDENT().GetText();
                var valueCtx = paramCtx.deviceParamValue();
                var value =
                    valueCtx.NUMBER()?.GetText()
                    ?? valueCtx.QUANTITY()?.GetText()
                    ?? valueCtx.SYMBOLIC()?.GetText()
                    ?? string.Empty;
                deviceParams[key] = value;
            }
            else if (paramCtx.LOAD_TYPE() != null)
            {
                // Handle R/C as param names (e.g., R=10k for resistor params)
                var key = paramCtx.LOAD_TYPE().GetText();
                var valueCtx = paramCtx.deviceParamValue();
                var value =
                    valueCtx.NUMBER()?.GetText()
                    ?? valueCtx.QUANTITY()?.GetText()
                    ?? valueCtx.SYMBOLIC()?.GetText()
                    ?? string.Empty;
                deviceParams[key] = value;
            }
        }

        return (deviceParams, pdkDevice);
    }

    private InstanceDeclaration BuildInstance(ACIRParser.InstanceDeclContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var type = ctx.IDENT(1).GetText();
        var bindings =
            ctx.bindingList() != null
                ? BuildBindingsForInstance(ctx.bindingList(), id)
                : new Dictionary<string, string>();
        var instanceParams = new Dictionary<string, ParamValue>();
        var sizes = new Dictionary<string, SizePack>();
        var connects = new List<ConnectionStatement>();
        var prefix = id + ".";

        foreach (var memberCtx in ctx.instanceMember())
        {
            switch (memberCtx)
            {
                case ACIRParser.InstanceParamContext paramCtx:
                    instanceParams[paramCtx.IDENT().GetText()] = BuildParamValue(
                        paramCtx.paramValue()
                    );
                    break;

                case ACIRParser.InstanceSizeContext sizeCtx:
                    sizes[sizeCtx.IDENT().GetText()] = BuildSizeLiteral(
                        sizeCtx.sizeLiteral(),
                        sizeCtx
                    );
                    break;

                case ACIRParser.InstanceConnectContext connectCtx:
                    var connPins = connectCtx.pinRef();
                    var from = BuildPinRef(connPins[0]);
                    var to = BuildPinRef(connPins[1]);

                    // ACIR0028: Validate that at least one side references the instance
                    if (!from.StartsWith(prefix) && !to.StartsWith(prefix))
                    {
                        AddDiagnostic(
                            connectCtx,
                            DiagnosticSeverity.Error,
                            $"ACIR0028: Instance connect statement must reference '{id}' on at least one side"
                        );
                    }

                    connects.Add(new ConnectionStatement { From = from, To = to });
                    break;

                case ACIRParser.InstanceBindingContext bindingCtx:
                    var bindPins = bindingCtx.binding().pinRef();
                    bindings[BuildPinRef(bindPins[0])] = BuildPinRef(bindPins[1]);
                    break;
            }
        }

        return new InstanceDeclaration
        {
            Id = id,
            Type = type,
            Bindings = bindings,
            Params = instanceParams,
            Sizes = sizes,
            Connects = connects,
        };
    }

    private AttachStatement BuildAttach(ACIRParser.AttachDeclContext ctx)
    {
        var sourceInstance = ctx.IDENT(0).GetText();
        var targetList = ctx.attachTargetList();
        var targetInstances = targetList.IDENT().Select(i => i.GetText()).ToList();
        var sourceTrait = ctx.IDENT(1).GetText();
        var targetTrait = ctx.IDENT(2).GetText();
        string? anchor = ctx.IDENT().Length > 3 ? ctx.IDENT(3).GetText() : null;

        List<ConnectorMapping>? overrides = null;
        if (ctx.attachOverrides() != null)
        {
            overrides = new List<ConnectorMapping>();
            foreach (var bindingCtx in ctx.attachOverrides().binding())
            {
                var pins = bindingCtx.pinRef();
                overrides.Add(
                    new ConnectorMapping
                    {
                        SourcePort = BuildPinRef(pins[0]),
                        TargetPort = BuildPinRef(pins[1]),
                    }
                );
            }
        }

        return new AttachStatement
        {
            SourceInstance = sourceInstance,
            TargetInstances = targetInstances,
            Via = $"{sourceTrait}::{targetTrait}",
            Anchor = anchor,
            Overrides = overrides,
        };
    }

    private ConstraintsBlock BuildConstraintsBlock(ACIRParser.ConstraintsSectionContext ctx)
    {
        var constraints = new ConstraintsBlock();

        foreach (var sectionCtx in ctx.constraintSection())
        {
            switch (sectionCtx)
            {
                case ACIRParser.NumericSectionContext numericCtx:
                    foreach (var constraintCtx in numericCtx.numericConstraint())
                    {
                        constraints.Numeric.Add(BuildNumericConstraint(constraintCtx));
                    }
                    break;

                case ACIRParser.TechSectionContext techCtx:
                    foreach (var constraintCtx in techCtx.techConstraint())
                    {
                        constraints.Tech.Add(BuildTechConstraint(constraintCtx));
                    }
                    break;

                case ACIRParser.GraphSectionContext graphCtx:
                    foreach (var constraintCtx in graphCtx.graphConstraint())
                    {
                        constraints.Graph.Add(BuildGraphConstraint(constraintCtx));
                    }
                    break;

                case ACIRParser.MeasureSectionContext measureCtx:
                    foreach (var intentCtx in measureCtx.measureIntent())
                    {
                        constraints.Measure.Add(BuildMeasureIntent(intentCtx));
                    }
                    break;
            }
        }

        return constraints;
    }

    private static NumericConstraint BuildNumericConstraint(ACIRParser.NumericConstraintContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var metric = ctx.IDENT(1).GetText();
        var node = ctx.IDENT().Length > 2 ? ctx.IDENT(2).GetText() : null;
        var op = ctx.COMPARISON_OP().GetText();
        var quantity = ctx.QUANTITY().GetText();
        var (value, unit) = ParseQuantity(quantity);

        return new NumericConstraint
        {
            Id = id,
            Metric = metric,
            Node = node,
            Op = op,
            Value = value,
            Unit = unit,
        };
    }

    private static TechConstraint BuildTechConstraint(ACIRParser.TechConstraintContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var param = ctx.IDENT(1).GetText();
        var scope = ctx.techConstraintScope().GetText();
        var op = ctx.COMPARISON_OP().GetText();
        var quantity = ctx.QUANTITY().GetText();
        var (value, unit) = ParseQuantity(quantity);

        return new TechConstraint
        {
            Id = id,
            Param = param,
            Op = op,
            Value = value,
            Unit = unit,
            Scope = scope,
        };
    }

    private static GraphConstraint BuildGraphConstraint(ACIRParser.GraphConstraintContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var rule = ctx.IDENT(1).GetText();
        var props = new Dictionary<string, string>();

        if (ctx.graphProps() != null)
        {
            foreach (var propCtx in ctx.graphProps().graphProp())
            {
                var key = propCtx.IDENT(0).GetText();
                var value =
                    propCtx.IDENT().Length > 1
                        ? propCtx.IDENT(1).GetText()
                        : propCtx.NUMBER()?.GetText()
                            ?? propCtx.QUANTITY()?.GetText()
                            ?? propCtx.STRING()?.GetText()
                            ?? string.Empty;
                props[key] = value;
            }
        }

        return new GraphConstraint
        {
            Id = id,
            Rule = rule,
            Properties = props,
        };
    }

    private static MeasureIntent BuildMeasureIntent(ACIRParser.MeasureIntentContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var bench = ctx.IDENT(1).GetText();
        var metric = ctx.IDENT(2).GetText();
        var node = ctx.IDENT().Length > 3 ? ctx.IDENT(3).GetText() : null;

        return new MeasureIntent
        {
            Id = id,
            Bench = bench,
            Metric = metric,
            Node = node,
        };
    }

    private HarnessBlock BuildHarnessBlock(ACIRParser.HarnessSectionContext ctx)
    {
        var supplies = new List<SupplyValue>();
        var biases = new List<BiasValue>();
        var sources = new List<SourceValue>();
        var loads = new List<LoadValue>();
        var sweeps = new List<SweepCondition>();
        var pvt = new List<string>();
        IcmrRange? icmr = null;

        foreach (var stmtCtx in ctx.harnessStatement())
        {
            switch (stmtCtx)
            {
                case ACIRParser.HarnessSupplyContext supplyCtx:
                    supplies.Add(
                        new SupplyValue
                        {
                            Net = supplyCtx.IDENT().GetText(),
                            Value = BuildHarnessValue(supplyCtx.harnessValue()),
                        }
                    );
                    break;

                case ACIRParser.HarnessBiasContext biasCtx:
                    biases.Add(
                        new BiasValue
                        {
                            Net = biasCtx.IDENT().GetText(),
                            Value = BuildHarnessValue(biasCtx.harnessValue()),
                        }
                    );
                    break;

                case ACIRParser.HarnessLoadContext loadCtx:
                    loads.Add(BuildLoad(loadCtx));
                    break;

                case ACIRParser.HarnessSourceContext sourceCtx:
                    var sourceSpec = sourceCtx.sourceSpec();
                    var zValue =
                        sourceSpec.QUANTITY()?.GetText()
                        ?? sourceSpec.NUMBER()?.GetText()
                        ?? string.Empty;
                    // Normalize: if no unit, add "Ohm"
                    if (sourceSpec.NUMBER() != null)
                    {
                        zValue = zValue + "Ohm";
                    }
                    sources.Add(new SourceValue { Net = sourceCtx.IDENT().GetText(), Z = zValue });
                    break;

                case ACIRParser.HarnessSweepContext sweepCtx:
                    sweeps.Add(BuildSweep(sweepCtx));
                    break;

                case ACIRParser.HarnessIcmrContext icmrCtx:
                    icmr = new IcmrRange
                    {
                        Min = icmrCtx.QUANTITY(0).GetText(),
                        Max = icmrCtx.QUANTITY(1).GetText(),
                    };
                    break;

                case ACIRParser.HarnessPvtContext pvtCtx:
                    pvt.AddRange(pvtCtx.pvtList().IDENT().Select(i => i.GetText()));
                    break;
            }
        }

        return new HarnessBlock
        {
            Supplies = supplies,
            Biases = biases,
            Sources = sources,
            Loads = loads,
            Sweeps = sweeps,
            Icmr = icmr,
            Pvt = pvt,
        };
    }

    private static LoadValue BuildLoad(ACIRParser.HarnessLoadContext ctx)
    {
        var net = ctx.IDENT().GetText();
        var elements = new List<LoadElement>();

        var loadSpec = ctx.loadSpec();
        switch (loadSpec)
        {
            case ACIRParser.SimpleLoadSpecContext simpleCtx:
                foreach (var elemCtx in simpleCtx.loadElement())
                {
                    elements.Add(BuildLoadElement(elemCtx));
                }
                break;

            case ACIRParser.ParenLoadSpecContext parenCtx:
                foreach (var elemCtx in parenCtx.loadElement())
                {
                    elements.Add(BuildLoadElement(elemCtx));
                }
                break;
        }

        return new LoadValue { Net = net, Elements = elements };
    }

    private static LoadElement BuildLoadElement(ACIRParser.LoadElementContext ctx)
    {
        var type = ctx.LOAD_TYPE().GetText();
        var value = ctx.QUANTITY()?.GetText() ?? ctx.NUMBER()?.GetText() ?? string.Empty;
        var unit = ctx.IDENT()?.GetText();

        // Normalize legacy format: combine value and unit (e.g., "1p" + "F" -> "1pF")
        if (unit != null)
        {
            value = value + unit;
        }

        return new LoadElement(type, value);
    }

    private static SweepCondition BuildSweep(ACIRParser.HarnessSweepContext ctx)
    {
        var name = ctx.IDENT().GetText();
        var sweepSpec = ctx.sweepSpec();

        if (sweepSpec.AUTO_KW() != null)
        {
            return new SweepCondition
            {
                Name = name,
                IsAuto = true,
                Start = string.Empty,
                Stop = string.Empty,
            };
        }

        var rangeCtx = sweepSpec.sweepRange();
        switch (rangeCtx)
        {
            case ACIRParser.ExplicitSweepContext explicitCtx:
                return new SweepCondition
                {
                    Name = name,
                    Start = BuildSweepValue(explicitCtx.sweepValue(0)),
                    Step = BuildSweepValue(explicitCtx.sweepValue(1)),
                    Stop = BuildSweepValue(explicitCtx.sweepValue(2)),
                    IsAuto = false,
                };

            case ACIRParser.AutoStepSweepContext autoCtx:
                return new SweepCondition
                {
                    Name = name,
                    Start = BuildSweepValue(autoCtx.sweepValue(0)),
                    Stop = BuildSweepValue(autoCtx.sweepValue(1)),
                    IsAuto = false,
                };

            default:
                return new SweepCondition { Name = name };
        }
    }

    private static string BuildSweepValue(ACIRParser.SweepValueContext ctx)
    {
        if (ctx.QUANTITY() != null)
        {
            return ctx.QUANTITY().GetText();
        }
        // Normalize: combine NUMBER and optional IDENT unit (e.g., "0.3" + "V" -> "0.3V")
        var value = ctx.NUMBER()?.GetText() ?? string.Empty;
        var unit = ctx.IDENT()?.GetText();
        if (unit != null)
        {
            return value + unit;
        }
        return value;
    }

    private static string BuildHarnessValue(ACIRParser.HarnessValueContext ctx)
    {
        if (ctx.QUANTITY() != null)
        {
            return ctx.QUANTITY().GetText();
        }
        // Normalize: combine NUMBER and optional IDENT unit (e.g., "1.8" + "V" -> "1.8V")
        var value = ctx.NUMBER()?.GetText() ?? string.Empty;
        var unit = ctx.IDENT()?.GetText();
        if (unit != null)
        {
            return value + unit;
        }
        return value;
    }

    private static BenchesBlock BuildBenchesBlock(ACIRParser.BenchesSectionContext ctx)
    {
        var benches = new BenchesBlock();

        foreach (var entryCtx in ctx.benchEntry())
        {
            var config = new Dictionary<string, string>();
            if (entryCtx.benchConfig() != null)
            {
                foreach (var configCtx in entryCtx.benchConfig().benchConfigEntry())
                {
                    var key = configCtx.IDENT(0).GetText();
                    var value =
                        configCtx.IDENT().Length > 1
                            ? configCtx.IDENT(1).GetText()
                            : configCtx.NUMBER()?.GetText()
                                ?? configCtx.QUANTITY()?.GetText()
                                ?? configCtx.STRING()?.GetText()
                                ?? string.Empty;
                    config[key] = value;
                }
            }

            benches.Benches.Add(
                new BenchConfig { Name = entryCtx.IDENT().GetText(), Config = config }
            );
        }

        return benches;
    }

    private static ProvenanceBlock BuildProvenanceBlock(ACIRParser.ProvenanceSectionContext ctx)
    {
        var provenance = new ProvenanceBlock();

        foreach (var entryCtx in ctx.provenanceEntry())
        {
            switch (entryCtx)
            {
                case ACIRParser.ProvenanceSourceContext sourceCtx:
                    var file = sourceCtx.STRING().GetText()[1..^1]; // Remove quotes
                    int? fromLine = null;
                    int? toLine = null;
                    if (sourceCtx.NUMBER().Length >= 2)
                    {
                        fromLine = int.Parse(sourceCtx.NUMBER(0).GetText());
                        toLine = int.Parse(sourceCtx.NUMBER(1).GetText());
                    }
                    provenance.Sources.Add(
                        new SourceReference
                        {
                            File = file,
                            FromLine = fromLine,
                            ToLine = toLine,
                        }
                    );
                    break;

                case ACIRParser.ProvenanceTransformContext transformCtx:
                    provenance.Transforms.Add(transformCtx.STRING().GetText()[1..^1]);
                    break;

                case ACIRParser.ProvenanceAliasContext aliasCtx:
                    provenance.Aliases[aliasCtx.IDENT(0).GetText()] = aliasCtx.IDENT(1).GetText();
                    break;
            }
        }

        return provenance;
    }

    private static (string Value, string Unit) ParseQuantity(string quantity)
    {
        // Match patterns like "50MHz", "30dB", "60deg", "1.8V"
        var match = QuantityPattern().Match(quantity);
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }
        return (quantity, string.Empty);
    }

    [GeneratedRegex(@"^(-?[0-9][0-9.eE+-]*[fpnumkMGT]?)([A-Za-z]+)$")]
    private static partial Regex QuantityPattern();

    private void AddDiagnostic(ParserRuleContext ctx, DiagnosticSeverity severity, string message)
    {
        var line = ctx.Start?.Line ?? 1;
        var column = (ctx.Start?.Column ?? 0) + 1;
        _diagnostics.Add(new Diagnostic(message, severity, _filePath, line, column));
    }

    private void AddDiagnostic(int line, int column, DiagnosticSeverity severity, string message)
    {
        _diagnostics.Add(new Diagnostic(message, severity, _filePath, line, column));
    }
}
