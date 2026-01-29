using System.IO;
using Cascode.Language;
using Cascode.Language.Validation;
using Cascode.TestSupport;

namespace Cascode.Language.Tests;

public class SpiceEmitterHierarchyTests
{
    [Fact]
    public void EmitDesign_WithInstance_EmitsXElement()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "ChildCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "SIG_IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "SIG_OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "child1",
                                Type = "ChildCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "SIG_IN",
                                    ["OUT"] = "SIG_OUT",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = doc.Circuits.First(c => c.Name == "TopLevel");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        Assert.Contains("Xchild1", output);
        Assert.Contains("SIG_IN SIG_OUT VDD GND ChildCircuit", output);
    }

    [Fact]
    public void EmitDesign_InstancePortOrder_MatchesSubcktDeclaration()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "ThreePortChild",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "AVDD", "DVDD" },
                    Grounds = new List<string> { "VSS" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "A",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "B",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "Y",
                            Type = "analog",
                        },
                    },
                },
                new Circuit
                {
                    Name = "Parent",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "u1",
                                Type = "ThreePortChild",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["A"] = "net_a",
                                    ["B"] = "net_b",
                                    ["Y"] = "net_y",
                                    ["AVDD"] = "VDD",
                                    ["DVDD"] = "VDD",
                                    ["VSS"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var parent = doc.Circuits.First(c => c.Name == "Parent");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(parent, writer, document: doc);
        var output = writer.ToString();

        // Port order should be: A B Y AVDD DVDD VSS (ports, then supplies, then grounds)
        Assert.Contains("Xu1 net_a net_b net_y VDD VDD GND ThreePortChild", output);
    }

    [Fact]
    public void EmitDesign_WithAttachResolution_UsesResolvedNets()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "ChildCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "child1",
                                Type = "ChildCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["OUT"] = "internal_net",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = doc.Circuits.First(c => c.Name == "TopLevel");

        // Create a resolution that maps internal_net to a different representative
        var resolution = new CircuitResolutionResult();
        resolution._netToRepresentative["internal_net"] = "resolved_output";

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc, resolution: resolution);
        var output = writer.ToString();

        // Should use resolved net name
        Assert.Contains("resolved_output", output);
        Assert.DoesNotContain("internal_net", output);
    }

    [Fact]
    public void EmitDesign_AttachResolution_UsesTerminalToNet()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "TraitA",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "TraitB",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "OUT", TargetPort = "IN" },
                            },
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Source",
                    Level = ACIRLevel.EL,
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                },
                new Circuit
                {
                    Name = "Target",
                    Level = ACIRLevel.EL,
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                    },
                },
                new Circuit
                {
                    Name = "Top",
                    Level = ACIRLevel.EL,
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration { Id = "a", Type = "Source" },
                            new InstanceDeclaration { Id = "b", Type = "Target" },
                        },
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "a",
                                TargetInstances = new List<string> { "b" },
                                Via = "TraitA::TraitB",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var resolution = resolver.Resolve().CircuitResults["Top"];
        var topLevel = doc.Circuits.First(c => c.Name == "Top");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc, resolution: resolution);
        var output = writer.ToString();

        Assert.Contains("Xa _auto_a_OUT__b_IN Source", output);
        Assert.Contains("Xb _auto_a_OUT__b_IN Target", output);
    }

    [Fact]
    public void EmitDesign_MultipleInstances_EmitsAllXElements()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Inverter",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                },
                new Circuit
                {
                    Name = "BufferChain",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "A",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "Y",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "n1", Domain = "analog" },
                        },
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "inv1",
                                Type = "Inverter",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "A",
                                    ["OUT"] = "n1",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                            new InstanceDeclaration
                            {
                                Id = "inv2",
                                Type = "Inverter",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "n1",
                                    ["OUT"] = "Y",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var bufferChain = doc.Circuits.First(c => c.Name == "BufferChain");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(bufferChain, writer, document: doc);
        var output = writer.ToString();

        Assert.Contains("Xinv1 A n1 VDD GND Inverter", output);
        Assert.Contains("Xinv2 n1 Y VDD GND Inverter", output);
    }

    [Fact]
    public void EmitDesign_NoDocument_SkipsInstances()
    {
        var circuit = new Circuit
        {
            Name = "TopLevel",
            Level = ACIRLevel.EL,
            Supplies = new List<string> { "VDD" },
            Grounds = new List<string> { "GND" },
            Fill = new FillBlock
            {
                Instances = new List<InstanceDeclaration>
                {
                    new InstanceDeclaration
                    {
                        Id = "child1",
                        Type = "ChildCircuit",
                        Bindings = new Dictionary<string, string>
                        {
                            ["VDD"] = "VDD",
                            ["GND"] = "GND",
                        },
                    },
                },
            },
        };

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(circuit, writer);
        var output = writer.ToString();

        // Should not emit X-element without document to resolve type
        Assert.DoesNotContain("Xchild1", output);
    }

    [Fact]
    public void EmitDesign_InlineCircuit_ExpandsDevices()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "InverterCell",
                    Level = ACIRLevel.EL,
                    Inline = true, // Marked for inline expansion
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "pmos",
                                Id = "MP",
                                Primitive = "Level1_PMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "IN",
                                    ["S"] = "VDD",
                                    ["B"] = "VDD",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "2u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "MN",
                                Primitive = "Level1_NMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "IN",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "1u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "BufferTop",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "A",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "Y",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "inv1",
                                Type = "InverterCell",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "A",
                                    ["OUT"] = "Y",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var bufferTop = doc.Circuits.First(c => c.Name == "BufferTop");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(bufferTop, writer, document: doc);
        var output = writer.ToString();

        // Should NOT emit X-element for inline circuit
        Assert.DoesNotContain("Xinv1", output);

        // Should emit expanded devices with hierarchical naming
        Assert.Contains("Minv1__MP", output);
        Assert.Contains("Minv1__MN", output);

        // Check inline expansion comment
        Assert.Contains("Inline expansion of inv1 : InverterCell", output);
    }

    [Fact]
    public void EmitDesign_InlineCircuit_UniquifiesNetNames()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "BufferCell",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "mid", Domain = "analog" },
                        },
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M1",
                                Primitive = "Level1_NMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "mid",
                                    ["G"] = "IN",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "1u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M2",
                                Primitive = "Level1_NMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "mid",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "1u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "A",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "Y",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "buf1",
                                Type = "BufferCell",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "A",
                                    ["OUT"] = "Y",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = doc.Circuits.First(c => c.Name == "TopLevel");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        // Internal net "mid" should be uniquified with instance prefix
        Assert.Contains("buf1__mid", output);
    }

    [Fact]
    public void EmitDesign_InlineCircuit_SubstitutesPortBindings()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Resistor",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string>(),
                    Grounds = new List<string>(),
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "P",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "N",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "resistor",
                                Id = "R1",
                                Primitive = "Ideal_Resistor",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["P"] = "P",
                                    ["N"] = "N",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string> { ["R"] = "1k" },
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "rload",
                                Type = "Resistor",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["P"] = "OUT",
                                    ["N"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = doc.Circuits.First(c => c.Name == "TopLevel");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        // Port bindings should be substituted: P->OUT, N->GND
        Assert.Contains("Rrload__R1 OUT GND 1k", output);
    }

    [Fact]
    public void ValidateAndEmit_InvalidHierarchy_ReturnsErrors()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "child1",
                                Type = "UndefinedCircuit", // Invalid reference
                            },
                        },
                    },
                },
            },
        };

        using var cascodeHome = CascodeHome.CreateInTemp("SpiceEmitterTest");
        var tempDir = cascodeHome.Path;

        var result = SpiceEmitter.ValidateAndEmit(doc, tempDir);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.GetErrors(), e => e.Code == "HIER-001");
    }

    [Fact]
    public void Emit_DependencyOrder_LeafFirst()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                // Defined in reverse order (top-level first)
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "child1",
                                Type = "ChildCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "ChildCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        using var cascodeHome = CascodeHome.CreateInTemp("SpiceEmitterTest");
        var tempDir = cascodeHome.Path;

        var result = SpiceEmitter.Emit(doc, tempDir);

        // Both circuits should be emitted
        Assert.Equal(2, result.DesignPaths.Count);

        // Check order of files: ChildCircuit should come before TopLevel
        var childPath = result.DesignPaths.FirstOrDefault(p => p.Contains("ChildCircuit"));
        var topPath = result.DesignPaths.FirstOrDefault(p => p.Contains("TopLevel"));
        Assert.NotNull(childPath);
        Assert.NotNull(topPath);

        var childIndex = result.DesignPaths.IndexOf(childPath);
        var topIndex = result.DesignPaths.IndexOf(topPath);
        Assert.True(childIndex < topIndex, "Child circuit should be emitted before top-level");
    }

    [Fact]
    public void Emit_MultiCircuitDocument_EmitsAllSubckts()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "CircuitA",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
                new Circuit
                {
                    Name = "CircuitB",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
                new Circuit
                {
                    Name = "CircuitC",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        using var cascodeHome = CascodeHome.CreateInTemp("SpiceEmitterTest");
        var tempDir = cascodeHome.Path;

        var result = SpiceEmitter.Emit(doc, tempDir);

        Assert.Equal(3, result.DesignPaths.Count);
        Assert.True(File.Exists(Path.Combine(tempDir, "CircuitA.sp")));
        Assert.True(File.Exists(Path.Combine(tempDir, "CircuitB.sp")));
        Assert.True(File.Exists(Path.Combine(tempDir, "CircuitC.sp")));
    }

    // Integration tests using golden files

    [Fact]
    public void GoldenFile_OTA5T_Hierarchical_ParsesCorrectly()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cas"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        Assert.Equal(ACIRVersion.Major, doc.VersionMajor);
        Assert.Equal(ACIRVersion.Minor, doc.VersionMinor);

        // Should have 3 circuits: DiffPair, OTA5T_Hierarchical, CurrentMirror
        Assert.Equal(3, doc.Circuits.Count);

        // Verify traits
        Assert.Equal(2, doc.Traits.Count);
        Assert.Contains(doc.Traits, t => t.Name == "CurrentMirrorLike");
        Assert.Contains(doc.Traits, t => t.Name == "DiffPairLike");

        // Verify inline circuit
        var diffPair = doc.Circuits.First(c => c.Name == "DiffPair");
        Assert.True(diffPair.Inline);
        Assert.Contains("DiffPairLike", diffPair.Traits!);

        // Verify top-level circuit has instances
        var topLevel = doc.Circuits.First(c => c.Name == "OTA5T_Hierarchical");
        Assert.NotNull(topLevel.Fill);
        Assert.Equal(2, topLevel.Fill.Instances.Count);
        Assert.Contains(topLevel.Fill.Instances, i => i.Id == "dp" && i.Type == "DiffPair");
        Assert.Contains(topLevel.Fill.Instances, i => i.Id == "cm" && i.Type == "CurrentMirror");
    }

    [Fact]
    public void GoldenFile_CurrentMirror_Standalone_ParsesCorrectly()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/CurrentMirror_Standalone.el.cas"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        Assert.Single(doc.Circuits);
        Assert.Equal(2, doc.Traits.Count);
        Assert.Contains(doc.Traits, t => t.Name == "LoadBranch");
        Assert.Contains(doc.Traits, t => t.Name == "CurrentMirrorLike");

        var circuit = doc.Circuits[0];
        Assert.Equal("CurrentMirror", circuit.Name);
        Assert.Contains("CurrentMirrorLike", circuit.Traits!);

        // Verify parameters and size packs
        Assert.Single(circuit.Parameters);
        Assert.Contains(circuit.Parameters, p => p.Name == "ratio" && p.Default?.Numeric == "1");
        Assert.Single(circuit.Sizes);
        Assert.Contains(
            circuit.Sizes,
            s =>
                s.Name == "Sense"
                && s.Default is not null
                && s.Default.Entries.TryGetValue("W", out var w)
                && s.Default.Entries.TryGetValue("L", out var l)
                && s.Default.Entries.TryGetValue("M", out var m)
                && w == "2u"
                && l == "180n"
                && m == "1"
        );

        // Verify devices
        Assert.NotNull(circuit.Fill);
        Assert.Equal(2, circuit.Fill.Devices.Count);
    }

    [Fact]
    public void GoldenFile_OTA5T_Hierarchical_EmitsSpiceWithInlineExpansion()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cas"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        var topLevel = doc.Circuits.First(c => c.Name == "OTA5T_Hierarchical");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        // Inline circuit (DiffPair) should be expanded, not X-element
        Assert.DoesNotContain("Xdp ", output);

        // Non-inline circuit (CurrentMirror) should emit as X-element
        Assert.Contains("Xcm ", output);

        // DiffPair inline expansion: devices should have dp__ prefix
        Assert.Contains("Mdp__M_N", output);
        Assert.Contains("Mdp__M_P", output);
        Assert.Contains("Mdp__M_TAIL", output);

        // Internal net from DiffPair should be uniquified
        Assert.Contains("dp__tnode", output);
    }

    [Fact]
    public void GoldenFile_OTA5T_Hierarchical_ValidatesSuccessfully()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cas"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        var result = HierarchyValidator.Validate(doc);

        Assert.True(
            result.IsValid,
            $"Validation failed: {string.Join(", ", result.GetErrors().Select(e => e.Message))}"
        );
    }

    [Fact]
    public void GoldenFile_OTA5T_Hierarchical_EmitProducesValidSubckts()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cas"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        using var cascodeHome = CascodeHome.CreateInTemp("SpiceEmitterTest");
        var tempDir = cascodeHome.Path;

        // Emit each non-inline circuit manually
        var designPaths = new List<string>();
        foreach (var circuit in doc.Circuits.Where(c => !c.Inline))
        {
            var path = Path.Combine(tempDir, $"{circuit.Name}.sp");
            using var writer = File.CreateText(path);
            SpiceEmitter.EmitDesign(circuit, writer, document: doc);
            designPaths.Add(path);
        }

        // Non-inline circuits should each produce a .sp file
        // OTA5T_Hierarchical and CurrentMirror are non-inline
        Assert.Contains(designPaths, p => p.Contains("OTA5T_Hierarchical.sp"));
        Assert.Contains(designPaths, p => p.Contains("CurrentMirror.sp"));

        // Read top-level file and verify structure
        var topLevelPath = designPaths.First(p => p.Contains("OTA5T_Hierarchical.sp"));
        var spiceContent = File.ReadAllText(topLevelPath);

        // Should have subckt declaration
        Assert.Contains(".subckt OTA5T_Hierarchical", spiceContent);
        Assert.Contains(".ends OTA5T_Hierarchical", spiceContent);

        // Should reference CurrentMirror as X-element
        Assert.Contains("Xcm", spiceContent);

        var mirror = doc.Circuits.First(c => c.Name == "CurrentMirror");
        var mirrorVariant = SpiceEmitter.GetDefaultVariantName(mirror);
        Assert.Contains(mirrorVariant, spiceContent);
    }

    [Fact]
    public void GoldenFile_OTA5T_Hierarchical_TraitConnectorsPreserved()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cas"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        // Verify trait connector was parsed correctly
        var mirrorTrait = doc.Traits.First(t => t.Name == "CurrentMirrorLike");
        Assert.Single(mirrorTrait.Connectors);

        var connector = mirrorTrait.Connectors[0];
        Assert.Equal("DiffPairLike", connector.TargetTrait);
        Assert.Equal(2, connector.Mappings.Count);
        // After desugaring, dot-separated names are preserved
        Assert.Contains(
            connector.Mappings,
            m => m.SourcePort == "SENSE" && m.TargetPort == "OUT.N"
        );
        Assert.Contains(
            connector.Mappings,
            m => m.SourcePort == "TAP[0]" && m.TargetPort == "OUT.P"
        );
    }

    [Fact]
    public void GoldenFile_RoundTrip_ParseWriteParseMatches()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/CurrentMirror_Standalone.el.cas"
        );

        using var reader = File.OpenText(acirPath);
        var doc1 = ACIRReader.Read(reader);

        // Write to string
        using var writeBuffer = new StringWriter();
        ACIRWriter.Write(doc1, writeBuffer);
        var written = writeBuffer.ToString();

        // Parse again
        using var reader2 = new StringReader(written);
        var doc2 = ACIRReader.Read(reader2);

        // Verify structure matches
        Assert.Equal(doc1.Circuits.Count, doc2.Circuits.Count);
        Assert.Equal(doc1.Traits.Count, doc2.Traits.Count);

        var c1 = doc1.Circuits[0];
        var c2 = doc2.Circuits[0];

        Assert.Equal(c1.Name, c2.Name);
        Assert.Equal(c1.Traits?.Count, c2.Traits?.Count);
        Assert.Equal(c1.Parameters.Count, c2.Parameters.Count);
        Assert.Equal(c1.Fill?.Devices.Count, c2.Fill?.Devices.Count);
    }

    [Fact]
    public void EmitDesign_NestedInlineCircuits_ExpandsRecursively()
    {
        // Tests recursive inline expansion: OuterInline contains InnerInline
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "InnerInline",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "A",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "Z",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "inner_net", Domain = "analog" },
                        },
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M_INNER",
                                Primitive = "Level1_NMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "Z",
                                    ["G"] = "A",
                                    ["S"] = "inner_net",
                                    ["B"] = "GND",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "1u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "OuterInline",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "outer_mid", Domain = "analog" },
                        },
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "pmos",
                                Id = "M_OUTER",
                                Primitive = "Level1_PMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "outer_mid",
                                    ["G"] = "IN",
                                    ["S"] = "VDD",
                                    ["B"] = "VDD",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "2u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                        },
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "inner_inst",
                                Type = "InnerInline",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["A"] = "outer_mid",
                                    ["Z"] = "OUT",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "SIG_IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "SIG_OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "outer_inst",
                                Type = "OuterInline",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "SIG_IN",
                                    ["OUT"] = "SIG_OUT",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = doc.Circuits.First(c => c.Name == "TopLevel");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        // Should expand outer inline's device with hierarchy: outer_inst__M_OUTER
        Assert.Contains("Mouter_inst__M_OUTER", output);

        // Should recursively expand inner inline's device: outer_inst__inner_inst__M_INNER
        Assert.Contains("Mouter_inst__inner_inst__M_INNER", output);

        // Outer's internal net should be prefixed: outer_inst__outer_mid
        Assert.Contains("outer_inst__outer_mid", output);

        // Inner's internal net should be deeply prefixed: outer_inst__inner_inst__inner_net
        Assert.Contains("outer_inst__inner_inst__inner_net", output);

        // Port substitutions should compose correctly:
        // Inner's A port connects to outer's outer_mid internal net
        // Inner's Z port connects to outer's OUT port, which is bound to SIG_OUT at top level

        // Should NOT have any X-elements for inline circuits
        Assert.DoesNotContain("Xouter_inst", output);
        Assert.DoesNotContain("Xinner_inst", output);
    }

    [Fact]
    public void EmitDesign_InlineCircuitWithNonInlineInstance_EmitsHierarchicalXElement()
    {
        // Tests that non-inline instances within inline circuits get hierarchical X-element names
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "SubCircuit",
                    Level = ACIRLevel.EL,
                    Inline = false, // NOT inline
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "P",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "N",
                            Type = "analog",
                        },
                    },
                },
                new Circuit
                {
                    Name = "WrapperInline",
                    Level = ACIRLevel.EL,
                    Inline = true, // This IS inline
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "IN",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "wrapper_net", Domain = "analog" },
                        },
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M1",
                                Primitive = "Level1_NMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "wrapper_net",
                                    ["G"] = "IN",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "1u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                        },
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "sub1",
                                Type = "SubCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["P"] = "wrapper_net",
                                    ["N"] = "OUT",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "A",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "Z",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "wrap",
                                Type = "WrapperInline",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["IN"] = "A",
                                    ["OUT"] = "Z",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = doc.Circuits.First(c => c.Name == "TopLevel");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        // Inline device should be expanded with hierarchical name
        Assert.Contains("Mwrap__M1", output);

        // Non-inline instance should be emitted as X-element with hierarchical name
        Assert.Contains("Xwrap__sub1", output);

        // The X-element should reference the subcircuit name
        Assert.Contains("SubCircuit", output);

        // Internal net should be prefixed
        Assert.Contains("wrap__wrapper_net", output);
    }

    [Fact]
    public void EmitDesign_ThreeLevelNestedInline_ExpandsAllLevels()
    {
        // Tests three levels of nested inline circuits
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Primitives = BuildDefaultPrimitives(),
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "Level3",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "X",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M3",
                                Primitive = "Level1_NMOS",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "X",
                                    ["G"] = "X",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Size = new SizePack
                                {
                                    Entries = new Dictionary<string, string>
                                    {
                                        ["W"] = "1u",
                                        ["L"] = "100n",
                                        ["M"] = "1",
                                    },
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "Level2",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "Y",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "l3",
                                Type = "Level3",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["X"] = "Y",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "Level1",
                    Level = ACIRLevel.EL,
                    Inline = true,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "Z",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "l2",
                                Type = "Level2",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["Y"] = "Z",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Io,
                            Name = "SIG",
                            Type = "analog",
                        },
                    },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "l1",
                                Type = "Level1",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["Z"] = "SIG",
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                        },
                    },
                },
            },
        };

        var topLevel = doc.Circuits.First(c => c.Name == "TopLevel");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        // Three-level hierarchical device name
        Assert.Contains("Ml1__l2__l3__M3", output);

        // The deepest device should connect to the top-level port through port composition
        // Level3.X -> Level2.Y -> Level1.Z -> TopLevel.SIG
        Assert.Contains("SIG", output);
    }

    private static List<PrimitiveDefinition> BuildDefaultPrimitives()
    {
        return
        [
            new PrimitiveDefinition
            {
                Name = "Level1_NMOS",
                Kind = "nmos",
                Device = "level1_nmos",
                SizeParameter = "primSize",
                Params = new Dictionary<string, string>
                {
                    ["W"] = "primSize.W",
                    ["L"] = "primSize.L",
                    ["m"] = "primSize.M",
                },
            },
            new PrimitiveDefinition
            {
                Name = "Level1_PMOS",
                Kind = "pmos",
                Device = "level1_pmos",
                SizeParameter = "primSize",
                Params = new Dictionary<string, string>
                {
                    ["W"] = "primSize.W",
                    ["L"] = "primSize.L",
                    ["m"] = "primSize.M",
                },
            },
            new PrimitiveDefinition
            {
                Name = "Ideal_Resistor",
                Kind = "resistor",
                Device = "resistor",
                SizeParameter = "primSize",
                Params = new Dictionary<string, string> { ["R"] = "primSize.R" },
            },
            new PrimitiveDefinition
            {
                Name = "Ideal_Capacitor",
                Kind = "capacitor",
                Device = "capacitor",
                SizeParameter = "primSize",
                Params = new Dictionary<string, string> { ["C"] = "primSize.C" },
            },
            new PrimitiveDefinition
            {
                Name = "Ideal_Inductor",
                Kind = "inductor",
                Device = "inductor",
                SizeParameter = "primSize",
                Params = new Dictionary<string, string> { ["L"] = "primSize.L" },
            },
        ];
    }
}
