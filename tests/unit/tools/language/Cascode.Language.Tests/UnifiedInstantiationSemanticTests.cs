using System.Collections.Generic;
using System.Linq;
using Cascode.Language.Validation;

namespace Cascode.Language.Tests;

public sealed class UnifiedInstantiationSemanticTests
{
    [Fact]
    public void Validate_PrimitiveInstanceDeclaredTypeMustMatchPrimitiveKind()
    {
        var document = new CascodeDocument
        {
            Primitives =
            [
                new PrimitiveDefinition
                {
                    Name = "Nfet",
                    Kind = "NMOS",
                    Device = "nmos_level1",
                    SizeParameter = "primSize",
                },
            ],
            Circuits =
            [
                new Circuit
                {
                    Name = "Top",
                    Level = CascodeLevel.EL,
                    Fill = new FillBlock
                    {
                        Instances =
                        [
                            new InstanceDeclaration
                            {
                                Id = "m1",
                                DeclaredType = "Resistor",
                                Type = "Nfet",
                                Bindings = new Dictionary<string, string>
                                {
                                    ["D"] = "OUT",
                                    ["G"] = "IN",
                                    ["S"] = "GND",
                                    ["B"] = "GND",
                                },
                            },
                        ],
                    },
                },
            ],
        };

        var result = CompleteDocumentSemanticValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), error => error.Code == "INST-001");
    }

    [Fact]
    public void Validate_PartInstanceMayUseImplementedInterfaceAsDeclaredType()
    {
        var document = new CascodeDocument
        {
            Parts =
            [
                new PartDefinition
                {
                    Name = "YageoRC",
                    Implements = ["Resistor"],
                    Ports =
                    [
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
                    ],
                    Catalog = new PartCatalog
                    {
                        Entries =
                        [
                            new PartCatalogEntry
                            {
                                Name = "_0402",
                                Body = new PartCatalogBody
                                {
                                    Pins =
                                    [
                                        new PinMapEntry { Pad = "P1", Target = "P" },
                                        new PinMapEntry { Pad = "P2", Target = "N" },
                                    ],
                                },
                            },
                        ],
                    },
                },
            ],
            Circuits =
            [
                new Circuit
                {
                    Name = "Top",
                    Level = CascodeLevel.EL,
                    Fill = new FillBlock
                    {
                        Instances =
                        [
                            new InstanceDeclaration
                            {
                                Id = "r1",
                                DeclaredType = "Resistor",
                                Type = "YageoRC",
                                Selection = [new SelectionArgument { Value = "_0402" }],
                                Bindings = new Dictionary<string, string>
                                {
                                    ["P"] = "IN",
                                    ["N"] = "OUT",
                                },
                            },
                        ],
                    },
                },
            ],
        };

        var result = CompleteDocumentSemanticValidator.Validate(document);

        Assert.True(result.IsValid, string.Join("\n", result.GetErrors().Select(e => e.Message)));
    }

    [Fact]
    public void Validate_CircuitInstanceMayUseImplementedInterfaceAsDeclaredType()
    {
        var document = new CascodeDocument
        {
            Traits = [new TraitDefinition { Name = "SingleEndedOpAmp" }],
            Circuits =
            [
                new Circuit
                {
                    Name = "AmpCell",
                    Level = CascodeLevel.EL,
                    Traits = ["SingleEndedOpAmp"],
                },
                new Circuit
                {
                    Name = "Top",
                    Level = CascodeLevel.EL,
                    Fill = new FillBlock
                    {
                        Instances =
                        [
                            new InstanceDeclaration
                            {
                                Id = "u1",
                                DeclaredType = "SingleEndedOpAmp",
                                Type = "AmpCell",
                            },
                        ],
                    },
                },
            ],
        };

        var result = CompleteDocumentSemanticValidator.Validate(document);

        Assert.True(result.IsValid, string.Join("\n", result.GetErrors().Select(e => e.Message)));
    }

    [Fact]
    public void Validate_ShortNameAmbiguityAcrossConcreteTargets_IsRejected()
    {
        var document = new CascodeDocument
        {
            Parts =
            [
                new PartDefinition
                {
                    Name = "Foo",
                    Catalog = new PartCatalog
                    {
                        Entries = [new PartCatalogEntry { Name = "_0402" }],
                    },
                },
            ],
            Circuits =
            [
                new Circuit { Name = "Foo", Level = CascodeLevel.EL },
                new Circuit
                {
                    Name = "Top",
                    Level = CascodeLevel.EL,
                    Fill = new FillBlock
                    {
                        Instances =
                        [
                            new InstanceDeclaration
                            {
                                Id = "x1",
                                DeclaredType = "Foo",
                                Type = "Foo",
                            },
                        ],
                    },
                },
            ],
        };

        var result = CompleteDocumentSemanticValidator.Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.GetErrors(), error => error.Code == "INST-002");
    }
}
