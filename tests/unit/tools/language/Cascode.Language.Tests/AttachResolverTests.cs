using System.Linq;
using Cascode.Language;

namespace Cascode.Language.Tests;

public class AttachResolverTests
{
    [Fact]
    public void Resolve_EmptyDocument_ReturnsSuccess()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
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
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
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
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "REF",
                            Type = "analog",
                        },
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
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
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
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
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
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
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
                    Level = CascodeLevel.EL,
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
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0021")
        );
    }

    [Fact]
    public void Resolve_MissingConnector_ReturnsError()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
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
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0023")
        );
    }

    [Fact]
    public void Resolve_IncompatibleDomains_ReturnsError()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0024")
        );
    }

    [Theory]
    [InlineData("MalformedViaWithoutSeparator")]
    [InlineData("")]
    [InlineData("Single:Colon")]
    [InlineData("A::B::C")]
    public void Resolve_MalformedViaClause_ReturnsError(string malformedVia)
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
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
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0022")
        );
    }

    #region Domain Compatibility Checks

    [Fact]
    public void Resolve_AttachWithMismatchedDomains_ReturnsCAS0024()
    {
        // Trait A: port SENSE : analog
        // Trait B: port OUT : bias
        // Connector: SENSE -> OUT
        // Expect: CAS0024 (analog != bias)
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "SourceTrait",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "SENSE",
                            Type = "analog",
                        },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "TargetTrait",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "SENSE", TargetPort = "OUT" },
                            },
                        },
                    },
                },
                new TraitDefinition
                {
                    Name = "TargetTrait",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "OUT",
                            Type = "bias",
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "src",
                                TargetInstances = new List<string> { "tgt" },
                                Via = "SourceTrait::TargetTrait",
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
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0024")
        );
    }

    [Fact]
    public void Resolve_AttachWithMatchingDomains_Succeeds()
    {
        // Both ports are analog
        // Expect: Success, no errors
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "SourceTrait",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "SENSE",
                            Type = "analog",
                        },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "TargetTrait",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "SENSE", TargetPort = "OUT" },
                            },
                        },
                    },
                },
                new TraitDefinition
                {
                    Name = "TargetTrait",
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
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "src",
                                TargetInstances = new List<string> { "tgt" },
                                Via = "SourceTrait::TargetTrait",
                            },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Resolve_AttachAnalogToBias_ReturnsCAS0024()
    {
        // Source: analog, Target: bias
        // Expect: CAS0024 (exact matching required)
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "AnalogTrait",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "SIG",
                            Type = "analog",
                        },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "BiasTrait",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "SIG", TargetPort = "BIAS" },
                            },
                        },
                    },
                },
                new TraitDefinition
                {
                    Name = "BiasTrait",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "BIAS",
                            Type = "bias",
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "analog_inst",
                                TargetInstances = new List<string> { "bias_inst" },
                                Via = "AnalogTrait::BiasTrait",
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
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0024")
        );
    }

    [Fact]
    public void Resolve_ConnectAnalogToBias_ReturnsCAS0024()
    {
        // Verify exact domain matching also applies to connect statements
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "sig", Domain = "analog" },
                            new NetDeclaration { Id = "vbias", Domain = "bias" },
                        },
                        Connections = new List<ConnectionStatement>
                        {
                            new ConnectionStatement { From = "sig", To = "vbias" },
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
            d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("CAS0024")
        );
    }

    #endregion

    #region Attach Override Mappings

    [Fact]
    public void Resolve_AttachWithOverrideReplacement_UsesOverriddenTarget()
    {
        // Connector: SENSE -> OUT.P
        // Override: SENSE -> OUT.N
        // Expect: Resolution uses OUT.N, not OUT.P
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "SENSE",
                            Type = "analog",
                        },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "DiffPair",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "SENSE", TargetPort = "OUT.P" },
                            },
                        },
                    },
                },
                new TraitDefinition
                {
                    Name = "DiffPair",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT.P",
                            Type = "analog",
                        },
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT.N",
                            Type = "analog",
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm",
                                TargetInstances = new List<string> { "dp" },
                                Via = "CurrentMirror::DiffPair",
                                Anchor = "mirror",
                                Overrides = new List<ConnectorMapping>
                                {
                                    new ConnectorMapping
                                    {
                                        SourcePort = "SENSE",
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
        var attach = Assert.Single(circuitResult.AttachBindings.Keys);
        var bindings = circuitResult.AttachBindings[attach];

        // The net should reflect the overridden target (OUT.N) instead of the default (OUT.P)
        // The binding key is the source port, and the value is the generated net name
        Assert.True(bindings.ContainsKey("SENSE"));
        // Net name should be based on anchor
        Assert.Contains("mirror", bindings["SENSE"]);
    }

    [Fact]
    public void Resolve_AttachWithInvalidOverrideSourcePort_ReturnsWarning()
    {
        // Override: NONEXISTENT -> OUT.N (not in connector)
        // Expect: Warning diagnostic (unknown override source)
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "CurrentMirror",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "SENSE",
                            Type = "analog",
                        },
                    },
                    Connectors = new List<TraitConnector>
                    {
                        new TraitConnector
                        {
                            TargetTrait = "DiffPair",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "SENSE", TargetPort = "OUT.P" },
                            },
                        },
                    },
                },
                new TraitDefinition
                {
                    Name = "DiffPair",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Output,
                            Name = "OUT.P",
                            Type = "analog",
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "cm",
                                TargetInstances = new List<string> { "dp" },
                                Via = "CurrentMirror::DiffPair",
                                Overrides = new List<ConnectorMapping>
                                {
                                    new ConnectorMapping
                                    {
                                        SourcePort = "NONEXISTENT",
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

        // Should still succeed (warning, not error) but have a warning diagnostic
        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("NONEXISTENT")
        );
    }

    #endregion

    #region Duplicate Name Handling

    [Fact]
    public void Resolve_DuplicateTraitNames_EmitsWarningAndKeepsFirst()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "DuplicateTrait",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "FirstPort",
                            Type = "analog",
                        },
                    },
                },
                new TraitDefinition
                {
                    Name = "DuplicateTrait",
                    Ports = new List<PortDeclaration>
                    {
                        new PortDeclaration
                        {
                            Direction = PortDirection.Input,
                            Name = "SecondPort",
                            Type = "analog",
                        },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var warning = Assert.Single(
            result.Diagnostics,
            d => d.Code == "CAS0026" && d.Message.Contains("interface")
        );
        Assert.Contains("DuplicateTrait", warning.Message);
        Assert.Contains("keeping first definition", warning.Message);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Resolve_DuplicateCircuitNames_EmitsWarningAndKeepsFirst()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "DuplicateCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "net1", Domain = "analog" },
                        },
                    },
                },
                new Circuit
                {
                    Name = "DuplicateCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDDA" },
                    Grounds = new List<string> { "GNDA" },
                    Fill = new FillBlock
                    {
                        Nets = new List<NetDeclaration>
                        {
                            new NetDeclaration { Id = "net2", Domain = "analog" },
                        },
                    },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var warning = Assert.Single(
            result.Diagnostics,
            d => d.Code == "CAS0026" && d.Message.Contains("circuit")
        );
        Assert.Contains("DuplicateCircuit", warning.Message);
        Assert.Contains("keeping first definition", warning.Message);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Resolve_DuplicateBundleTypeNames_EmitsWarningAndKeepsFirst()
    {
        var doc = new CascodeDocument
        {
            VersionMajor = CascodeVersion.Major,
            VersionMinor = CascodeVersion.Minor,
            BundleTypes = new List<BundleType>
            {
                new BundleType
                {
                    Name = "DuplicateBundle",
                    Fields = new Dictionary<string, string>
                    {
                        { "P", "analog" },
                        { "N", "analog" },
                    },
                },
                new BundleType
                {
                    Name = "DuplicateBundle",
                    Fields = new Dictionary<string, string>
                    {
                        { "A", "analog" },
                        { "B", "analog" },
                        { "C", "analog" },
                    },
                },
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "TestCircuit",
                    Level = CascodeLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        var resolver = new AttachResolver(doc);
        var result = resolver.Resolve();

        Assert.True(result.Success);
        var warning = Assert.Single(
            result.Diagnostics,
            d => d.Code == "CAS0026" && d.Message.Contains("bundle type")
        );
        Assert.Contains("DuplicateBundle", warning.Message);
        Assert.Contains("keeping first definition", warning.Message);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    #endregion
}
