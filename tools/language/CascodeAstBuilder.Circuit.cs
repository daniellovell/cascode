using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
    /// <summary>Builds a circuit block from its parse context.</summary>
    private Circuit BuildCircuit(CascodeParser.CircuitContext ctx)
    {
        var memberState = ProcessCircuitMembers(ctx);
        var signature = BuildCircuitSignature(ctx.paramSignature());

        return new Circuit
        {
            Name = ctx.name.Text,
            Traits = ctx.implementsClause()
                ?.interfaceList()
                ?.IDENT()
                .Select(i => i.GetText())
                .ToList(),
            Level = memberState.Level,
            Inline = memberState.IsInline,
            Package = memberState.Package,
            Supplies = memberState.Supplies,
            Grounds = memberState.Grounds,
            Ports = memberState.Ports,
            Parameters = signature.Parameters,
            Sizes = signature.Sizes,
            Slot = memberState.Slot,
            Fill = memberState.Fill,
            Constraints = memberState.Constraints,
            Harness = memberState.Harness,
            Env = memberState.Env,
            Render = memberState.Render,
            BenchBindings = memberState.BenchBindings,
            BenchBindingExtensions = memberState.BenchBindingExtensions,
            Synth = memberState.Synth,
            Provenance = memberState.Provenance,
        };
    }

    private CircuitMemberState ProcessCircuitMembers(CascodeParser.CircuitContext ctx)
    {
        var state = new CircuitMemberState();
        foreach (var memberCtx in ctx.circuitMember())
        {
            switch (memberCtx)
            {
                case CascodeParser.LevelDeclContext levelCtx:
                    state.Level = ParseLevel(levelCtx.levelValue());
                    break;

                case CascodeParser.InlineDeclContext:
                    state.IsInline = true;
                    break;

                case CascodeParser.PackageDeclContext pkgCtx:
                    state.Package = BuildQualifiedName(pkgCtx.qualifiedName());
                    break;

                case CascodeParser.SupplyDeclContext supplyCtx:
                    state.Supplies.Add(supplyCtx.IDENT().GetText());
                    break;

                case CascodeParser.GroundDeclContext groundCtx:
                    state.Grounds.Add(groundCtx.IDENT().GetText());
                    break;

                case CascodeParser.PortDeclContext portCtx:
                    state.Ports.Add(
                        new PortDeclaration
                        {
                            Direction = BuildPortDirection(portCtx.direction()),
                            Name = BuildPortName(portCtx.portName()),
                            Type = BuildPortType(portCtx.portType()),
                        }
                    );
                    break;

                case CascodeParser.BareSlotMemberContext:
                    state.Slot = new SlotBlock();
                    break;

                case CascodeParser.SlotBlockMemberContext slotBlockCtx:
                    state.Slot = BuildSlotBlock(slotBlockCtx);
                    break;

                case CascodeParser.FillSectionContext fillCtx:
                    state.Fill = BuildFillBlock(fillCtx);
                    break;

                case CascodeParser.ConstraintsSectionContext constraintsCtx:
                    state.Constraints = BuildConstraintsBlock(constraintsCtx);
                    break;

                case CascodeParser.HarnessSectionContext harnessCtx:
                    state.Harness = BuildHarnessBlock(harnessCtx);
                    break;

                case CascodeParser.EnvSectionContext envCtx:
                    state.Env = BuildEnvBlock(envCtx);
                    break;

                case CascodeParser.RenderSectionContext renderCtx:
                    state.Render = BuildRenderBlock(renderCtx);
                    break;

                case CascodeParser.CircuitBenchesContext benchesCtx:
                    var section = benchesCtx.circuitBenchesSection();
                    state.BenchBindings.AddRange(BuildBenchBindings(section.benchBinding()));
                    state.BenchBindingExtensions.AddRange(
                        BuildBenchExtensions(section.benchExtension())
                    );
                    break;

                case CascodeParser.SynthSectionContext synthCtx:
                    state.Synth = BuildSynthBlock(synthCtx);
                    break;

                case CascodeParser.ProvenanceSectionContext provCtx:
                    state.Provenance = BuildProvenanceBlock(provCtx);
                    break;
            }
        }

        return state;
    }

    private static string BuildQualifiedName(CascodeParser.QualifiedNameContext ctx)
    {
        return string.Join(".", ctx.idPart().Select(i => i.GetText()));
    }

    private sealed class CircuitMemberState
    {
        public FillBlock? Fill { get; set; }
        public ConstraintsBlock? Constraints { get; set; }
        public HarnessBlock? Harness { get; set; }
        public EnvBlock? Env { get; set; }
        public RenderBlock? Render { get; set; }
        public List<BenchBinding> BenchBindings { get; } = new();
        public List<BenchBindingExtension> BenchBindingExtensions { get; } = new();
        public SynthBlock? Synth { get; set; }
        public ProvenanceBlock? Provenance { get; set; }
        public CascodeLevel Level { get; set; } = CascodeLevel.ML;
        public bool IsInline { get; set; }
        public string? Package { get; set; }
        public List<string> Supplies { get; } = new();
        public List<string> Grounds { get; } = new();
        public List<PortDeclaration> Ports { get; } = new();
        public SlotBlock? Slot { get; set; }
    }

    private sealed record CircuitSignature(
        List<CircuitParameter> Parameters,
        List<SizeDeclaration> Sizes
    );

    private CircuitSignature BuildCircuitSignature(CascodeParser.ParamSignatureContext? ctx)
    {
        var parameters = new List<CircuitParameter>();
        var sizes = new List<SizeDeclaration>();

        if (ctx?.paramList() == null)
        {
            return new CircuitSignature(parameters, sizes);
        }

        foreach (var paramCtx in ctx.paramList().paramDecl())
        {
            if (paramCtx.SIZE_KW() != null)
            {
                sizes.Add(BuildSizeDeclaration(paramCtx));
                continue;
            }

            parameters.Add(BuildCircuitParameter(paramCtx));
        }

        return new CircuitSignature(parameters, sizes);
    }

    /// <summary>Builds a circuit parameter declaration.</summary>
    private CircuitParameter BuildCircuitParameter(CascodeParser.ParamDeclContext ctx)
    {
        ParamValue? defaultValue = null;
        if (ctx.paramValue() != null)
        {
            defaultValue = BuildParamValue(ctx.paramValue());
        }

        return new CircuitParameter
        {
            Name = ctx.paramName.Text,
            Type = ctx.paramType().GetText(),
            Default = defaultValue,
        };
    }

    /// <summary>Builds a named size declaration.</summary>
    private SizeDeclaration BuildSizeDeclaration(CascodeParser.ParamDeclContext ctx)
    {
        SizePack? defaultPack = null;
        if (ctx.sizeExpr() != null)
        {
            defaultPack = BuildSizeExpression(ctx.sizeExpr(), ctx);
        }

        return new SizeDeclaration { Name = ctx.sizeName.Text, Default = defaultPack };
    }

    /// <summary>Builds a size pack and reports duplicate keys.</summary>
    private SizePack BuildSizeExpression(
        CascodeParser.SizeExprContext ctx,
        Antlr4.Runtime.ParserRuleContext? diagnosticCtx = null
    )
    {
        var entries = new Dictionary<string, string>();
        var body = ctx.sizeExprBody();

        if (body.sizeKvList() != null)
        {
            foreach (var pairCtx in body.sizeKvList().sizeKvPair())
            {
                var key = pairCtx.sizeKey.Text;
                var value = pairCtx.expr().GetText();

                if (entries.ContainsKey(key))
                {
                    AddDiagnostic(
                        diagnosticCtx ?? pairCtx,
                        DiagnosticSeverity.Error,
                        $"Duplicate size key '{key}'"
                    );
                }
                else
                {
                    entries[key] = value;
                }
            }
        }
        else if (body.sizeExprList() != null)
        {
            var exprs = body.sizeExprList().expr();
            var keys = new[] { "W", "L", "M" };
            if (exprs.Length > keys.Length)
            {
                AddDiagnostic(
                    diagnosticCtx ?? body,
                    DiagnosticSeverity.Error,
                    "Size expression list supports at most three entries (W, L, M)."
                );
            }

            for (var i = 0; i < exprs.Length && i < keys.Length; i++)
            {
                entries[keys[i]] = exprs[i].GetText();
            }
        }

        return new SizePack { Entries = entries };
    }

    /// <summary>Builds a parameter value from a literal, numeric, or symbolic token.</summary>
    private static ParamValue BuildParamValue(CascodeParser.ParamValueContext ctx)
    {
        return BuildScalarValue(ctx.scalarExpr());
    }

    private SlotBlock BuildSlotBlock(CascodeParser.SlotBlockMemberContext ctx)
    {
        var slot = new SlotBlock();

        foreach (var stmtCtx in ctx.slotBlockStatement())
        {
            switch (stmtCtx)
            {
                case CascodeParser.SlotNetDeclContext netCtx:
                    slot.Nets.Add(
                        new NetDeclaration
                        {
                            Id = netCtx.IDENT().GetText(),
                            Domain = BuildPortType(netCtx.portType()),
                        }
                    );
                    break;

                case CascodeParser.SlotInstanceStatementContext instanceCtx:
                    slot.Instances.Add(BuildSlotInstance(instanceCtx.slotInstanceDecl()));
                    break;

                case CascodeParser.SlotConnectDeclContext connectCtx:
                    var pins = connectCtx.pinRef();
                    slot.Connections.Add(
                        new ConnectionStatement
                        {
                            From = BuildPinRef(pins[0]),
                            To = BuildPinRef(pins[1]),
                        }
                    );
                    break;
            }
        }

        return slot;
    }

    /// <summary>Builds the fill block containing nets, devices, instances, and connects.</summary>
    private FillBlock BuildFillBlock(CascodeParser.FillSectionContext ctx)
    {
        var fill = new FillBlock();

        foreach (var stmtCtx in ctx.fillStatement())
        {
            switch (stmtCtx)
            {
                case CascodeParser.FillNetDeclContext netCtx:
                    fill.Nets.Add(
                        new NetDeclaration
                        {
                            Id = netCtx.IDENT().GetText(),
                            Domain = BuildPortType(netCtx.portType()),
                        }
                    );
                    break;

                case CascodeParser.FillSizeDeclContext sizeCtx:
                    fill.Sizes.Add(
                        new SizeDeclaration
                        {
                            Name = sizeCtx.sizeName.Text,
                            Default = BuildSizeExpression(sizeCtx.sizeExpr(), sizeCtx),
                        }
                    );
                    break;

                case CascodeParser.FillDeviceDeclContext deviceCtx:
                    fill.Devices.Add(BuildDevice(deviceCtx.deviceDecl()));
                    break;

                case CascodeParser.FillInstanceStatementContext instanceCtx:
                    fill.Instances.Add(
                        BuildInstance(instanceCtx.fillInstanceDecl().instanceDecl())
                    );
                    break;

                case CascodeParser.FillAttachDeclContext attachCtx:
                    fill.Attaches.Add(BuildAttach(attachCtx));
                    break;

                case CascodeParser.FillConnectDeclContext connectCtx:
                    var pins = connectCtx.pinRef();
                    fill.Connections.Add(
                        new ConnectionStatement
                        {
                            From = BuildPinRef(pins[0]),
                            To = BuildPinRef(pins[1]),
                        }
                    );
                    break;

                default:
                    AddDiagnostic(
                        stmtCtx,
                        DiagnosticSeverity.Error,
                        $"CAS2011: Unsupported fill statement: '{stmtCtx.GetText()}'"
                    );
                    break;
            }
        }

        return fill;
    }

    /// <summary>Builds a device declaration from its parse context.</summary>
    private DeviceDeclaration BuildDevice(CascodeParser.DeviceDeclContext ctx)
    {
        var deviceType = ctx.DEVICE_TYPE().GetText();
        var deviceId = BuildDeviceId(ctx.deviceId());
        var primitiveName = ctx.primitiveName.Text;

        string? sizeName = null;
        SizePack? sizePack = null;
        if (ctx.sizeArg().IDENT() != null)
        {
            sizeName = ctx.sizeArg().IDENT().GetText();
        }
        else if (ctx.sizeArg().sizeExpr() != null)
        {
            sizePack = BuildSizeExpression(ctx.sizeArg().sizeExpr(), ctx.sizeArg());
        }

        var bindings = BuildBindings(ctx.bindingBlock().bindingList());

        return new DeviceDeclaration
        {
            DeviceType = deviceType,
            Id = deviceId,
            Bindings = bindings,
            Primitive = primitiveName,
            SizeName = sizeName,
            Size = sizePack,
        };
    }

    private static string BuildDeviceId(CascodeParser.DeviceIdContext ctx)
    {
        return string.Join(".", ctx.idPart().Select(p => p.GetText()));
    }

    private static Dictionary<string, string> BuildBindings(CascodeParser.BindingListContext ctx)
    {
        var bindings = new Dictionary<string, string>();
        if (ctx == null)
        {
            return bindings;
        }

        foreach (var bindingCtx in ctx.binding())
        {
            var pins = bindingCtx.pinRef();
            var from = BuildPinRef(pins[0]);
            var to = BuildPinRef(pins[1]);
            bindings[from] = to;
        }
        return bindings;
    }

    private InstanceDeclaration BuildSlotInstance(CascodeParser.SlotInstanceDeclContext ctx)
    {
        return BuildInstance(
            declaredType: ctx.slotDeclaredType().GetText(),
            id: ctx.instanceId.Text,
            type: ctx.instanceTypeName().GetText(),
            argList: ctx.argList(),
            bindingBlock: ctx.bindingBlock(),
            diagnosticCtx: ctx,
            allowSomeDeclaredType: true
        );
    }

    /// <summary>Builds an instance declaration with parameters and bindings.</summary>
    private InstanceDeclaration BuildInstance(
        CascodeParser.InstanceDeclContext ctx,
        bool allowSomeDeclaredType = false
    )
    {
        return BuildInstance(
            declaredType: ctx.declaredType.Text,
            id: ctx.instanceId.Text,
            type: ctx.instanceTypeName().GetText(),
            argList: ctx.argList(),
            bindingBlock: ctx.bindingBlock(),
            diagnosticCtx: ctx,
            allowSomeDeclaredType: allowSomeDeclaredType
        );
    }

    private InstanceDeclaration BuildInstance(
        string? declaredType,
        string id,
        string type,
        CascodeParser.ArgListContext? argList,
        CascodeParser.BindingBlockContext? bindingBlock,
        Antlr4.Runtime.ParserRuleContext diagnosticCtx,
        bool allowSomeDeclaredType
    )
    {
        var usesSomeDeclaredType =
            allowSomeDeclaredType
            && declaredType is not null
            && declaredType.Equals("Some", StringComparison.Ordinal);
        if (
            !usesSomeDeclaredType
            && declaredType is not null
            && !declaredType.Equals(type, StringComparison.Ordinal)
        )
        {
            AddDiagnostic(
                diagnosticCtx,
                DiagnosticSeverity.Error,
                $"CAS0036: Instance '{id}' declares type '{declaredType}' but constructs '{type}'. The declared and constructor types must match exactly."
            );
        }

        var bindings = bindingBlock is null
            ? new Dictionary<string, string>()
            : BuildBindings(bindingBlock.bindingList());
        var prefix = $"{id}.";
        var invalidKeys = bindings
            .Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (var key in invalidKeys)
        {
            bindings.Remove(key);
            AddDiagnostic(
                bindingBlock ?? diagnosticCtx,
                DiagnosticSeverity.Error,
                $"CAS0033: Instance bindings must not be instance-qualified; use '.PORT--net' not '.{id}.PORT--net'"
            );
        }

        var instanceParams = new Dictionary<string, ParamValue>();
        var sizes = new Dictionary<string, SizePack>();

        if (argList != null)
        {
            foreach (var argCtx in argList.arg())
            {
                var name = argCtx.argName()?.GetText();
                if (string.IsNullOrWhiteSpace(name))
                {
                    // Positional arg syntax: new Foo(<expr>). Store under "value" so harness emitters
                    // can read it via "value" or as the single param (GetFirstParam()).
                    name = "value";
                    if (
                        instanceParams.ContainsKey(name)
                        || sizes.ContainsKey(name)
                        || argList.arg().Length > 1
                    )
                    {
                        AddDiagnostic(
                            argCtx,
                            DiagnosticSeverity.Error,
                            "CAS0034: Positional instance arguments support only a single argument."
                        );
                        continue;
                    }
                }

                if (argCtx.argValue().sizeExpr() != null)
                {
                    sizes[name] = BuildSizeExpression(argCtx.argValue().sizeExpr(), argCtx);
                }
                else if (argCtx.argValue().expr() is not null)
                {
                    instanceParams[name] = new ParamValue
                    {
                        Symbolic = argCtx.argValue().expr().GetText(),
                    };
                }
                else
                {
                    instanceParams[name] = BuildScalarValue(argCtx.argValue().scalarExpr());
                }
            }
        }

        return new InstanceDeclaration
        {
            Id = id,
            Type = type,
            DeclaredType = declaredType,
            Bindings = bindings,
            Params = instanceParams,
            Sizes = sizes,
            Connects = new List<ConnectionStatement>(),
        };
    }

    /// <summary>Builds an attach statement and any connector overrides.</summary>
    private AttachStatement BuildAttach(CascodeParser.FillAttachDeclContext ctx)
    {
        var sourceInstance = ctx.IDENT(0).GetText();
        var targetList = ctx.attachTargetList();
        var targetInstances = targetList.IDENT().Select(i => i.GetText()).ToList();
        var sourceInterface = ctx.IDENT(1).GetText();
        var targetInterface = ctx.IDENT(2).GetText();
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
            Via = $"{sourceInterface}::{targetInterface}",
            Anchor = anchor,
            Overrides = overrides,
        };
    }

    private static ParamValue BuildScalarValue(CascodeParser.ScalarExprContext ctx)
    {
        if (ctx.UNSIZED() != null)
        {
            return new ParamValue { Symbolic = ctx.UNSIZED().GetText() };
        }
        if (ctx.AUTO_KW() != null)
        {
            return new ParamValue { Symbolic = ctx.AUTO_KW().GetText() };
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
            var str = ctx.STRING().GetText();
            return new ParamValue { Literal = str[1..^1] };
        }
        if (ctx.IDENT() != null)
        {
            return new ParamValue { Symbolic = ctx.IDENT().GetText() };
        }

        return new ParamValue();
    }
}
