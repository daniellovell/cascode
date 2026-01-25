using System.Collections.Generic;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

internal sealed partial class ACIRAstBuilder
{
    /// <summary>Builds a circuit block from its parse context.</summary>
    private Circuit BuildCircuit(ACIRParser.CircuitContext ctx)
    {
        var memberState = ProcessCircuitMembers(ctx);

        return new Circuit
        {
            Name = ctx.IDENT().GetText(),
            Traits = ctx.traitList()?.IDENT().Select(i => i.GetText()).ToList(),
            Level = memberState.Level,
            Inline = memberState.IsInline,
            Package = memberState.Package,
            Supplies = memberState.Supplies,
            Grounds = memberState.Grounds,
            Ports = memberState.Ports,
            Parameters = memberState.Parameters,
            Sizes = memberState.Sizes,
            Fill = memberState.Fill,
            Constraints = memberState.Constraints,
            Harness = memberState.Harness,
            Provenance = memberState.Provenance,
        };
    }

    private CircuitMemberState ProcessCircuitMembers(ACIRParser.CircuitContext ctx)
    {
        var state = new CircuitMemberState();
        foreach (var memberCtx in ctx.circuitMember())
        {
            switch (memberCtx)
            {
                case ACIRParser.LevelDeclContext levelCtx:
                    state.Level = ParseLevel(levelCtx.levelValue());
                    break;

                case ACIRParser.InlineDeclContext:
                    state.IsInline = true;
                    break;

                case ACIRParser.PackageDeclContext pkgCtx:
                    state.Package = BuildQualifiedName(pkgCtx.qualifiedName());
                    break;

                case ACIRParser.SupplyDeclContext supplyCtx:
                    state.Supplies.Add(supplyCtx.IDENT().GetText());
                    break;

                case ACIRParser.GroundDeclContext groundCtx:
                    state.Grounds.Add(groundCtx.IDENT().GetText());
                    break;

                case ACIRParser.PortDeclContext portCtx:
                    state.Ports.Add(
                        new PortDeclaration
                        {
                            Name = BuildPortName(portCtx.portName()),
                            Type = BuildPortType(portCtx.portType()),
                        }
                    );
                    break;

                case ACIRParser.ParamDeclContext paramCtx:
                    state.Parameters.Add(BuildCircuitParameter(paramCtx));
                    break;

                case ACIRParser.SizeDeclContext sizeCtx:
                    state.Sizes.Add(BuildSizeDeclaration(sizeCtx));
                    break;

                case ACIRParser.FillSectionContext fillCtx:
                    state.Fill = BuildFillBlock(fillCtx);
                    break;

                case ACIRParser.ConstraintsSectionContext constraintsCtx:
                    state.Constraints = BuildConstraintsBlock(constraintsCtx);
                    break;

                case ACIRParser.HarnessSectionContext harnessCtx:
                    state.Harness = BuildHarnessBlock(harnessCtx);
                    break;

                case ACIRParser.ProvenanceSectionContext provCtx:
                    state.Provenance = BuildProvenanceBlock(provCtx);
                    break;
            }
        }

        return state;
    }

    private static string BuildQualifiedName(ACIRParser.QualifiedNameContext ctx)
    {
        return string.Join(".", ctx.IDENT().Select(i => i.GetText()));
    }

    private sealed class CircuitMemberState
    {
        public FillBlock? Fill { get; set; }
        public ConstraintsBlock? Constraints { get; set; }
        public HarnessBlock? Harness { get; set; }
        public ProvenanceBlock? Provenance { get; set; }
        public ACIRLevel Level { get; set; } = ACIRLevel.ML;
        public bool IsInline { get; set; }
        public string? Package { get; set; }
        public List<string> Supplies { get; } = new();
        public List<string> Grounds { get; } = new();
        public List<PortDeclaration> Ports { get; } = new();
        public List<CircuitParameter> Parameters { get; } = new();
        public List<SizeDeclaration> Sizes { get; } = new();
    }

    /// <summary>Builds a circuit parameter declaration.</summary>
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

    /// <summary>Builds a named size declaration.</summary>
    private SizeDeclaration BuildSizeDeclaration(ACIRParser.SizeDeclContext ctx)
    {
        SizePack? defaultPack = null;
        if (ctx.sizeLiteral() != null)
        {
            defaultPack = BuildSizeLiteral(ctx.sizeLiteral(), ctx);
        }

        return new SizeDeclaration { Name = ctx.IDENT().GetText(), Default = defaultPack };
    }

    /// <summary>Builds a size pack and reports duplicate keys.</summary>
    private SizePack BuildSizeLiteral(
        ACIRParser.SizeLiteralContext ctx,
        Antlr4.Runtime.ParserRuleContext? diagnosticCtx = null
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
                // Use the provided context for diagnostic location, or fall back to the entry context.
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

    /// <summary>Renders a size literal like "(W=10u, L=180n, M=1)".</summary>
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

    /// <summary>Builds a parameter value from a literal, numeric, or symbolic token.</summary>
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
            // Remove quotes.
            var str = ctx.STRING().GetText();
            return new ParamValue { Literal = str[1..^1] };
        }
        if (ctx.IDENT() != null)
        {
            return new ParamValue { Symbolic = ctx.IDENT().GetText() };
        }
        return new ParamValue();
    }

    /// <summary>Builds the fill block containing nets, devices, instances, and connects.</summary>
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

    /// <summary>Builds a device declaration from its parse context.</summary>
    private DeviceDeclaration BuildDevice(ACIRParser.DeviceDeclContext ctx)
    {
        var deviceType = ctx.DEVICE_TYPE().GetText();
        var deviceId = BuildDeviceId(ctx.deviceId());

        var bindings =
            ctx.bindingList() != null
                ? BuildBindings(ctx.bindingList())
                : new Dictionary<string, string>();

        var deviceParams = new Dictionary<string, string>();

        var hasBodyBindings = false;
        foreach (var itemCtx in ctx.deviceBodyItem())
        {
            switch (itemCtx)
            {
                case ACIRParser.DeviceSizeStmtContext sizeCtx:
                    if (deviceType is "resistor" or "capacitor" or "inductor" or "diode")
                    {
                        AddDiagnostic(
                            sizeCtx,
                            DiagnosticSeverity.Error,
                            $"ACIR0034: '{deviceType}' devices do not support size statements"
                        );
                        break;
                    }

                    if (deviceParams.ContainsKey("size"))
                    {
                        AddDiagnostic(
                            sizeCtx,
                            DiagnosticSeverity.Error,
                            "ACIR0031: Duplicate device size statement"
                        );
                        break;
                    }

                    if (sizeCtx.sizeLiteral() != null)
                    {
                        BuildSizeLiteral(sizeCtx.sizeLiteral(), sizeCtx);
                        deviceParams["size"] = RenderSizeLiteral(sizeCtx.sizeLiteral());
                    }
                    else if (sizeCtx.IDENT() != null)
                    {
                        deviceParams["size"] = sizeCtx.IDENT().GetText();
                    }
                    break;

                case ACIRParser.DeviceParamLineContext paramLineCtx:
                    var (key, value) = BuildDeviceParamAssign(paramLineCtx.deviceParamAssign());
                    if (deviceParams.ContainsKey(key))
                    {
                        AddDiagnostic(
                            paramLineCtx,
                            DiagnosticSeverity.Error,
                            $"ACIR0032: Duplicate device parameter '{key}'"
                        );
                        break;
                    }

                    deviceParams[key] = value;
                    break;

                case ACIRParser.DeviceBindingContext bindingCtx:
                    hasBodyBindings = true;
                    var pins = bindingCtx.binding().pinRef();
                    bindings[BuildPinRef(pins[0])] = BuildPinRef(pins[1]);
                    break;
            }
        }

        if (ctx.bindingList() != null && hasBodyBindings)
        {
            AddDiagnostic(
                ctx,
                DiagnosticSeverity.Error,
                "ACIR0030: Cannot mix inline and body bindings in a single declaration."
            );
        }

        return new DeviceDeclaration
        {
            DeviceType = deviceType,
            Id = deviceId,
            Bindings = bindings,
            Params = deviceParams,
            PdkDevice = ctx.pdkDeviceName().GetText(),
        };
    }

    private static (string Key, string Value) BuildDeviceParamAssign(
        ACIRParser.DeviceParamAssignContext ctx
    )
    {
        var key = ctx.IDENT()?.GetText() ?? ctx.LOAD_TYPE()?.GetText() ?? string.Empty;
        var valueCtx = ctx.deviceParamValue();
        var value =
            valueCtx.NUMBER()?.GetText()
            ?? valueCtx.QUANTITY()?.GetText()
            ?? valueCtx.SYMBOLIC()?.GetText()
            ?? string.Empty;

        return (key, value);
    }

    private static string BuildDeviceId(ACIRParser.DeviceIdContext ctx)
    {
        return string.Join(".", ctx.idPart().Select(p => p.GetText()));
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

    /// <summary>Builds an instance declaration with parameters and bindings.</summary>
    private InstanceDeclaration BuildInstance(ACIRParser.InstanceDeclContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var type = ctx.IDENT(1).GetText();

        var bindings =
            ctx.bindingList() != null
                ? BuildBindings(ctx.bindingList())
                : new Dictionary<string, string>();
        if (ctx.bindingList() != null)
        {
            var prefix = $"{id}.";
            var invalidKeys = bindings
                .Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var key in invalidKeys)
            {
                bindings.Remove(key);
                AddDiagnostic(
                    ctx.bindingList(),
                    DiagnosticSeverity.Error,
                    $"ACIR0033: Instance bindings must not be instance-qualified; use '.PORT--net' not '.{id}.PORT--net'"
                );
            }
        }

        var hasBodyBindings = false;
        var instanceParams = new Dictionary<string, ParamValue>();
        var sizes = new Dictionary<string, SizePack>();

        foreach (var memberCtx in ctx.instanceBodyItem())
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

                case ACIRParser.InstanceBindingContext bindingCtx:
                    hasBodyBindings = true;
                    var pins = bindingCtx.binding().pinRef();
                    var from = BuildPinRef(pins[0]);
                    if (from.StartsWith($"{id}.", StringComparison.Ordinal))
                    {
                        AddDiagnostic(
                            bindingCtx,
                            DiagnosticSeverity.Error,
                            $"ACIR0033: Instance bindings must not be instance-qualified; use '.PORT--net' not '.{id}.PORT--net'"
                        );
                        break;
                    }

                    bindings[from] = BuildPinRef(pins[1]);
                    break;
            }
        }

        if (ctx.bindingList() != null && hasBodyBindings)
        {
            AddDiagnostic(
                ctx,
                DiagnosticSeverity.Error,
                "ACIR0030: Cannot mix inline and body bindings in a single declaration."
            );
        }

        return new InstanceDeclaration
        {
            Id = id,
            Type = type,
            Bindings = bindings,
            Params = instanceParams,
            Sizes = sizes,
            Connects = new List<ConnectionStatement>(),
        };
    }

    /// <summary>Builds an attach statement and any connector overrides.</summary>
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
}
