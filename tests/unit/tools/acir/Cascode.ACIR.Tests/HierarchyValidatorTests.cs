using Cascode.Language;
using Cascode.Language.Validation;

namespace Cascode.Language.Tests;

public class HierarchyValidatorTests
{
    [Fact]
    public void Validate_UndefinedCircuitReference_ReturnsHIER001()
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
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration { Id = "cm1", Type = "UndefinedCircuit" },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "HIER-001");
    }

    [Fact]
    public void Validate_MissingRequiredParameter_ReturnsHIER002()
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
                    Parameters = new List<CircuitParameter>
                    {
                        new CircuitParameter
                        {
                            Name = "width",
                            Type = "real",
                            Default = null, // Required parameter
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.ML,
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
                                // No Params set - width is required but not provided
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "HIER-002");
    }

    [Fact]
    public void Validate_UnboundPort_ReturnsHIER003()
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
                    Level = ACIRLevel.ML,
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
                                    ["IN"] = "sig_in",
                                    // OUT not bound
                                },
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "HIER-003");
    }

    [Fact]
    public void Validate_CircularDependency_ReturnsHIER004()
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
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration { Id = "b1", Type = "CircuitB" },
                        },
                    },
                },
                new Circuit
                {
                    Name = "CircuitB",
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "a1",
                                Type = "CircuitA", // Circular reference
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "HIER-004");
    }

    [Fact]
    public void Validate_AttachUnknownInstance_ReturnsHIER006()
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
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "nonexistent1",
                                TargetInstances = new List<string> { "nonexistent2" },
                                Via = "SomeTrait::OtherTrait",
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "HIER-006");
    }

    [Fact]
    public void Validate_ValidHierarchy_Succeeds()
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
                new TraitDefinition
                {
                    Name = "LoadBranch",
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
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "CMirror",
                    Level = ACIRLevel.EL,
                    Traits = new List<string> { "CurrentMirror" },
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
                    Parameters = new List<CircuitParameter>
                    {
                        new CircuitParameter
                        {
                            Name = "ratio",
                            Type = "real",
                            Default = new ParamValue { Numeric = "2" },
                        },
                    },
                },
                new Circuit
                {
                    Name = "LoadCell",
                    Level = ACIRLevel.EL,
                    Traits = new List<string> { "LoadBranch" },
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
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "cm1",
                                Type = "CMirror",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                    ["IN"] = "bias_in",
                                    ["OUT"] = "bias_out",
                                },
                                Params = new Dictionary<string, ParamValue>
                                {
                                    ["ratio"] = new ParamValue { Numeric = "4" },
                                },
                            },
                            new InstanceDeclaration
                            {
                                Id = "load1",
                                Type = "LoadCell",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                    ["IN"] = "load_in",
                                },
                            },
                        },
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

        var result = HierarchyValidator.Validate(doc);

        Assert.True(result.IsValid);
        Assert.Empty(result.GetErrors());
    }

    [Fact]
    public void Validate_ParameterWithDefault_NotRequired()
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
                    Parameters = new List<CircuitParameter>
                    {
                        new CircuitParameter
                        {
                            Name = "width",
                            Type = "real",
                            Default = new ParamValue { Numeric = "1u" },
                        },
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.ML,
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
                                // Using default for width
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_SelfInstantiation_ReturnsHIER004()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "RecursiveCircuit",
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "self1",
                                Type = "RecursiveCircuit", // Self-reference
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "HIER-004");
    }

    [Fact]
    public void EmptyDocument_ShouldReportNoCircuits()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>(),
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.True(result.IsValid);
        Assert.Empty(result.GetErrors());
    }

    [Fact]
    public void DuplicateCircuitNames_ShouldReportError()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "DuplicateName",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
                new Circuit
                {
                    Name = "DuplicateName",
                    Level = ACIRLevel.EL,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), e => e.Code == "HIER-005");
    }

    [Fact]
    public void MultipleErrors_ShouldReportAllErrors()
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
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "child1",
                                Type = "UndefinedCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                },
                            },
                            new InstanceDeclaration
                            {
                                Id = "child2",
                                Type = "ChildCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                    ["IN"] = "sig_in",
                                    // OUT not bound - HIER-003
                                },
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.False(result.IsValid);
        var errors = result.GetErrors().ToList();
        Assert.True(errors.Count >= 2);
        Assert.Contains(errors, e => e.Code == "HIER-001");
        Assert.Contains(errors, e => e.Code == "HIER-003");
    }

    [Fact]
    public void Validate_PortCoveredByAttach_NotReportedAsUnbound()
    {
        var doc = new ACIRDocument
        {
            VersionMajor = ACIRVersion.Major,
            VersionMinor = ACIRVersion.Minor,
            Traits = new List<TraitDefinition>
            {
                new TraitDefinition
                {
                    Name = "Driver",
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
                            TargetTrait = "Receiver",
                            Mappings = new List<ConnectorMapping>
                            {
                                new ConnectorMapping { SourcePort = "OUT", TargetPort = "IN" },
                            },
                        },
                    },
                },
                new TraitDefinition
                {
                    Name = "Receiver",
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
            },
            Circuits = new List<Circuit>
            {
                new Circuit
                {
                    Name = "DriverCircuit",
                    Level = ACIRLevel.EL,
                    Traits = new List<string> { "Driver" },
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
                    Name = "ReceiverCircuit",
                    Level = ACIRLevel.EL,
                    Traits = new List<string> { "Receiver" },
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
                    },
                },
                new Circuit
                {
                    Name = "TopLevel",
                    Level = ACIRLevel.ML,
                    Supplies = new List<string> { "VDD" },
                    Grounds = new List<string> { "GND" },
                    Fill = new FillBlock
                    {
                        Instances = new List<InstanceDeclaration>
                        {
                            new InstanceDeclaration
                            {
                                Id = "drv1",
                                Type = "DriverCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                    // OUT not directly bound - covered by attach
                                },
                            },
                            new InstanceDeclaration
                            {
                                Id = "rcv1",
                                Type = "ReceiverCircuit",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["VDD"] = "VDD",
                                    ["GND"] = "GND",
                                    // IN not directly bound - covered by attach
                                },
                            },
                        },
                        Attaches = new List<AttachStatement>
                        {
                            new AttachStatement
                            {
                                SourceInstance = "drv1",
                                TargetInstances = new List<string> { "rcv1" },
                                Via = "Driver::Receiver",
                            },
                        },
                    },
                },
            },
        };

        var result = HierarchyValidator.Validate(doc);

        Assert.True(result.IsValid);
        Assert.Empty(result.GetErrors());
    }
}
