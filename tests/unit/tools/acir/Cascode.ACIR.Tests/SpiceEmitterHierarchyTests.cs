using System.IO;
using Cascode.ACIR;
using Cascode.ACIR.Validation;
using Cascode.TestSupport;

namespace Cascode.ACIR.Tests;

public class SpiceEmitterHierarchyTests
{
    [Fact]
    public void EmitDesign_WithInstance_EmitsXElement()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
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
                        new PortDeclaration { Name = "IN", Type = "analog" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
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
                        new PortDeclaration { Name = "SIG_IN", Type = "analog" },
                        new PortDeclaration { Name = "SIG_OUT", Type = "analog" },
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
                        new PortDeclaration { Name = "A", Type = "analog" },
                        new PortDeclaration { Name = "B", Type = "analog" },
                        new PortDeclaration { Name = "Y", Type = "analog" },
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
                        new PortDeclaration { Name = "OUT", Type = "analog" },
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
                        new PortDeclaration { Name = "OUT", Type = "analog" },
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
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                },
                new Circuit
                {
                    Name = "Target",
                    Level = ACIRLevel.EL,
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "IN", Type = "analog" },
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
                        new PortDeclaration { Name = "IN", Type = "analog" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
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
                        new PortDeclaration { Name = "A", Type = "analog" },
                        new PortDeclaration { Name = "Y", Type = "analog" },
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
                        new PortDeclaration { Name = "IN", Type = "analog" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                    Fill = new FillBlock
                    {
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "pmos",
                                Id = "MP",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "IN",
                                    ["S"] = "VDD",
                                    ["B"] = "VDD",
                                },
                                Params = new Dictionary<string, string>
                                {
                                    ["W"] = "2u",
                                    ["L"] = "100n",
                                },
                            },
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "MN",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "IN",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Params = new Dictionary<string, string>
                                {
                                    ["W"] = "1u",
                                    ["L"] = "100n",
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
                        new PortDeclaration { Name = "A", Type = "analog" },
                        new PortDeclaration { Name = "Y", Type = "analog" },
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
                        new PortDeclaration { Name = "IN", Type = "analog" },
                        new PortDeclaration { Name = "OUT", Type = "analog" },
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
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "mid",
                                    ["G"] = "IN",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Params = new Dictionary<string, string>
                                {
                                    ["W"] = "1u",
                                    ["L"] = "100n",
                                },
                            },
                            new DeviceDeclaration
                            {
                                DeviceType = "nmos",
                                Id = "M2",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "mid",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                                Params = new Dictionary<string, string>
                                {
                                    ["W"] = "1u",
                                    ["L"] = "100n",
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
                        new PortDeclaration { Name = "A", Type = "analog" },
                        new PortDeclaration { Name = "Y", Type = "analog" },
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
                        new PortDeclaration { Name = "P", Type = "analog" },
                        new PortDeclaration { Name = "N", Type = "analog" },
                    },
                    Fill = new FillBlock
                    {
                        Devices = new List<DeviceDeclaration>
                        {
                            new DeviceDeclaration
                            {
                                DeviceType = "resistor",
                                Id = "R1",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["P"] = "P",
                                    ["N"] = "N",
                                },
                                Params = new Dictionary<string, string> { ["R"] = "1k" },
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
                        new PortDeclaration { Name = "OUT", Type = "analog" },
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
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        Assert.Equal(ACIRVersion.Major, doc.VersionMajor);
        Assert.Equal(ACIRVersion.Minor, doc.VersionMinor);

        // Should have 4 circuits: DiffPair, ActiveLoad, OTA5T_Hierarchical, CurrentMirror
        Assert.Equal(4, doc.Circuits.Count);

        // Verify traits
        Assert.Equal(3, doc.Traits.Count);
        Assert.Contains(doc.Traits, t => t.Name == "CurrentMirrorTrait");
        Assert.Contains(doc.Traits, t => t.Name == "LoadBranch");
        Assert.Contains(doc.Traits, t => t.Name == "DiffPairTrait");

        // Verify inline circuits
        var diffPair = doc.Circuits.First(c => c.Name == "DiffPair");
        Assert.True(diffPair.Inline);
        Assert.Contains("DiffPairTrait", diffPair.Traits!);

        var activeLoad = doc.Circuits.First(c => c.Name == "ActiveLoad");
        Assert.True(activeLoad.Inline);
        Assert.Contains("LoadBranch", activeLoad.Traits!);

        // Verify top-level circuit has instances
        var topLevel = doc.Circuits.First(c => c.Name == "OTA5T_Hierarchical");
        Assert.NotNull(topLevel.Fill);
        Assert.Equal(3, topLevel.Fill.Instances.Count);
        Assert.Contains(topLevel.Fill.Instances, i => i.Id == "dp" && i.Type == "DiffPair");
        Assert.Contains(topLevel.Fill.Instances, i => i.Id == "cm" && i.Type == "CurrentMirror");
        Assert.Contains(topLevel.Fill.Instances, i => i.Id == "load" && i.Type == "ActiveLoad");
    }

    [Fact]
    public void GoldenFile_CurrentMirror_Standalone_ParsesCorrectly()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/CurrentMirror_Standalone.el.cir"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        Assert.Single(doc.Circuits);
        Assert.Single(doc.Traits);

        var circuit = doc.Circuits[0];
        Assert.Equal("CurrentMirror", circuit.Name);
        Assert.Contains("CurrentMirrorTrait", circuit.Traits!);

        // Verify parameters
        Assert.Equal(3, circuit.Parameters.Count);
        Assert.Contains(circuit.Parameters, p => p.Name == "ratio" && p.Default?.Numeric == "1");
        Assert.Contains(circuit.Parameters, p => p.Name == "W" && p.Default?.Numeric == "2u");
        Assert.Contains(circuit.Parameters, p => p.Name == "L" && p.Default?.Numeric == "180n");

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
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        var topLevel = doc.Circuits.First(c => c.Name == "OTA5T_Hierarchical");

        using var writer = new StringWriter();
        SpiceEmitter.EmitDesign(topLevel, writer, document: doc);
        var output = writer.ToString();

        // Inline circuits (DiffPair, ActiveLoad) should be expanded, not X-elements
        Assert.DoesNotContain("Xdp ", output);
        Assert.DoesNotContain("Xload ", output);

        // Non-inline circuit (CurrentMirror) should emit as X-element
        Assert.Contains("Xcm ", output);

        // DiffPair inline expansion: devices should have dp__ prefix
        Assert.Contains("Mdp__M_N", output);
        Assert.Contains("Mdp__M_P", output);
        Assert.Contains("Mdp__M_TAIL", output);

        // ActiveLoad inline expansion
        Assert.Contains("Mload__M_LOAD", output);

        // Internal net from DiffPair should be uniquified
        Assert.Contains("dp__tnode", output);
    }

    [Fact]
    public void GoldenFile_OTA5T_Hierarchical_ValidatesSuccessfully()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
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
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
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
        Assert.Contains("CurrentMirror", spiceContent);
    }

    [Fact]
    public void GoldenFile_OTA5T_Hierarchical_TraitConnectorsPreserved()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
        );

        using var reader = File.OpenText(acirPath);
        var doc = ACIRReader.Read(reader);

        // Verify trait connector was parsed correctly
        var mirrorTrait = doc.Traits.First(t => t.Name == "CurrentMirrorTrait");
        Assert.Single(mirrorTrait.Connectors);

        var connector = mirrorTrait.Connectors[0];
        Assert.Equal("LoadBranch", connector.TargetTrait);
        Assert.Single(connector.Mappings);
        Assert.Equal("OUT", connector.Mappings[0].SourcePort);
        Assert.Equal("IN", connector.Mappings[0].TargetPort);
    }

    [Fact]
    public void GoldenFile_RoundTrip_ParseWriteParseMatches()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var acirPath = Path.Combine(
            repoRoot,
            "tests/golden/acir/hierarchy/CurrentMirror_Standalone.el.cir"
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
}
