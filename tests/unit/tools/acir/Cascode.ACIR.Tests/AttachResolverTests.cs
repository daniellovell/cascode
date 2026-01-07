using System.Linq;
using Cascode.ACIR;
using Cascode.Parser;

namespace Cascode.ACIR.Tests;

public class AttachResolverTests
{
    [Fact]
    public void Resolve_EmptyDocument_ReturnsSuccess()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_CircuitWithNets_CreatesEquivalenceClasses()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
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
                            new NetDeclaration { Id = "internal", Domain = "analog" },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];

        // Each net should be its own representative
        Assert.Equal("VDD", circuitResult.NetToRepresentative["VDD"]);
        Assert.Equal("GND", circuitResult.NetToRepresentative["GND"]);
        Assert.Equal("IN", circuitResult.NetToRepresentative["IN"]);
        Assert.Equal("OUT", circuitResult.NetToRepresentative["OUT"]);
        Assert.Equal("internal", circuitResult.NetToRepresentative["internal"]);
    }

    [Fact]
    public void Resolve_ConnectStatement_UnifiesNets()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "net1", Domain = "analog" },
                            new NetDeclaration { Id = "net2", Domain = "analog" },
                        },
                        Connections = new List<ConnectionStatement>
                        {
                            new ConnectionStatement { From = "net1", To = "net2" },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];

        // net1 and net2 should have the same representative
        Assert.Equal(
            circuitResult.NetToRepresentative["net1"],
            circuitResult.NetToRepresentative["net2"]
        );
    }

    [Fact]
    public void Resolve_SupplyGroundPriority_SelectsSupplyAsRepresentative()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "power_alias", Domain = "power" },
                        },
                        Connections = new List<ConnectionStatement>
                        {
                            new ConnectionStatement { From = "power_alias", To = "VDD" },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];

        // VDD should be selected as representative (supply has higher priority)
        Assert.Equal("VDD", circuitResult.NetToRepresentative["power_alias"]);
        Assert.Equal("VDD", circuitResult.NetToRepresentative["VDD"]);
    }

    [Fact]
    public void Resolve_AttachWithValidConnector_CreatesBindings()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "LoadBranch",
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
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "CurrentMirror::LoadBranch",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];
        var netName = circuitResult.TerminalToNet["cm1.OUT"];
        Assert.Equal(netName, circuitResult.TerminalToNet["load1.IN"]);
    }

    [Fact]
    public void Resolve_AttachWithAnchor_UsesAnchorInNetName()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "LoadBranch",
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
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "CurrentMirror::LoadBranch",
                                Anchor = "bias",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];
        var netName = circuitResult.TerminalToNet["cm1.OUT"];
        Assert.Equal("bias", netName);
        Assert.Equal(netName, circuitResult.TerminalToNet["load1.IN"]);
    }

    [Fact]
    public void Resolve_AttachWithAnchor_MultipleMappings_UsesSuffixes()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                        new PortDeclaration { Name = "REF", Type = "analog" },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "LoadBranch",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "OUT", TargetPort = "IN" },
                                new ConnectorMapping { SourcePort = "REF", TargetPort = "REF" },
                            },
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "a",
                                TargetInstances = new List<string> { "b" },
                                Via = "CurrentMirror::LoadBranch",
                                Anchor = "tie",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];
        Assert.Equal("tie_0", circuitResult.TerminalToNet["a.OUT"]);
        Assert.Equal("tie_0", circuitResult.TerminalToNet["b.IN"]);
        Assert.Equal("tie_1", circuitResult.TerminalToNet["a.REF"]);
        Assert.Equal("tie_1", circuitResult.TerminalToNet["b.REF"]);
    }

    [Fact]
    public void Resolve_AttachChain_AppliesPairwise()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "LoadBranch",
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
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "a",
                                TargetInstances = new List<string> { "b", "c" },
                                Via = "CurrentMirror::LoadBranch",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];
        Assert.Equal("_auto_a_OUT__b_IN", circuitResult.TerminalToNet["a.OUT"]);
        Assert.Equal("_auto_a_OUT__b_IN", circuitResult.TerminalToNet["b.IN"]);
        Assert.Equal("_auto_b_OUT__c_IN", circuitResult.TerminalToNet["b.OUT"]);
        Assert.Equal("_auto_b_OUT__c_IN", circuitResult.TerminalToNet["c.IN"]);
    }

    [Fact]
    public void Resolve_AttachChainWithAnchor_AssignsPairwiseNames()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "LoadBranch",
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
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "a",
                                TargetInstances = new List<string> { "b", "c" },
                                Via = "CurrentMirror::LoadBranch",
                                Anchor = "link",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];
        Assert.Equal("link_0", circuitResult.TerminalToNet["a.OUT"]);
        Assert.Equal("link_0", circuitResult.TerminalToNet["b.IN"]);
        Assert.Equal("link_1", circuitResult.TerminalToNet["b.OUT"]);
        Assert.Equal("link_1", circuitResult.TerminalToNet["c.IN"]);
    }

    [Fact]
    public void Resolve_AttachWithOverrides_AppliesOverrideMappings()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "LoadBranch",
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
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "CurrentMirror::LoadBranch",
                                Overrides = new List<ConnectorMapping>
                                {
                                    new ConnectorMapping
                                    {
                                        SourcePort = "OUT",
                                        TargetPort = "OUT.N",
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var circuitResult = result.CircuitResults["TestCircuit"];
        var netName = circuitResult.TerminalToNet["cm1.OUT"];
        Assert.Equal(netName, circuitResult.TerminalToNet["load1.OUT.N"]);
        Assert.Equal("_auto_cm1_OUT__load1_OUT_N", netName);
    }

    [Fact]
    public void Resolve_UndefinedTrait_ReturnsError()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "UndefinedTrait::LoadBranch",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0021")
        );
    }

    [Fact]
    public void Resolve_MissingConnector_ReturnsError()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration { Name = "OUT", Type = "analog" },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstances = new List<string> { "load1" },
                                Via = "CurrentMirror::NonExistentTarget",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0023")
        );
    }

    [Fact]
    public void Resolve_IncompatibleDomains_ReturnsError()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Connections = new List<ConnectionStatement>
                        {
                            new ConnectionStatement { From = "VDD", To = "GND" },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0024")
        );
    }

    [Theory]
    [InlineData("MalformedViaWithoutSeparator")]
    [InlineData("")]
    [InlineData("Single:Colon")]
    [InlineData("A::B::C")]
    public void Resolve_MalformedViaClause_ReturnsError(string malformedVia)
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm1",
                                TargetInstance = "load1",
                                Via = malformedVia,
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ACIR0022")
        );
    }
}
