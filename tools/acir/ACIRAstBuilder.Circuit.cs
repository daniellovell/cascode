using System.Collections.Generic;
using System.Linq;
using Cascode.Parser;

namespace Cascode.ACIR;

internal sealed partial class ACIRAstBuilder
{
    /// <summary>Builds a circuit block from its parse context.</summary>
    private Circuit BuildCircuit(ACIRParser.CircuitContext ctx)
    {
        var circuit = new Circuit
        {
            Name = ctx.IDENT().GetText(),
            Traits = ctx.traitList()?.IDENT().Select(i => i.GetText()).ToList(),
        };

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

    private static string BuildQualifiedName(ACIRParser.QualifiedNameContext ctx)
    {
        return string.Join(".", ctx.IDENT().Select(i => i.GetText()));
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

    /// <summary>Builds instance bindings while stripping the instance prefix.</summary>
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

    /// <summary>Builds device parameters and optional PDK device name.</summary>
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

    /// <summary>Builds an instance declaration with parameters, sizes, and connects.</summary>
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
