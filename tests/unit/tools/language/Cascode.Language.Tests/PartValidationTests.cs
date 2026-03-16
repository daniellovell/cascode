using System.Collections.Generic;
using System.Linq;
using Cascode.Language.Validation;

namespace Cascode.Language.Tests;

public sealed class PartValidationTests
{
    [Fact]
    public void Validate_AbstractPartCannotBeInstantiated()
    {
        var result = Validate(
            new PartDefinition
            {
                Name = "BaseRes",
                IsAbstract = true,
                Ports = [Port("P"), Port("N")],
                Catalog = EntryCatalog("_0402", ("P1", "P"), ("P2", "N")),
            },
            new InstanceDeclaration
            {
                Id = "r1",
                DeclaredType = "BaseRes",
                Type = "BaseRes",
                Selection = [new SelectionArgument { Value = "_0402" }],
            }
        );

        Assert.Contains(result.GetErrors(), error => error.Code == "PART-004");
    }

    [Fact]
    public void Validate_ConcretePartMustProduceEffectiveEntry()
    {
        var result = Validate(
            new PartDefinition
            {
                Name = "LoosePart",
                Ports = [Port("P"), Port("N")],
                Catalog = new PartCatalog { Variants = [new PartVariantAxis { Name = "body" }] },
            }
        );

        Assert.Contains(result.GetErrors(), error => error.Code == "PART-003");
    }

    [Fact]
    public void Validate_ExtendsChainMergesPortsAndEntries()
    {
        var basePart = new PartDefinition
        {
            Name = "BaseMcu",
            IsAbstract = true,
            Supplies = ["VDD"],
            Grounds = ["GND"],
            Ports = [Port("PWR_GOOD", "digital")],
            Catalog = new PartCatalog
            {
                Defaults = new PartCatalogBody
                {
                    Pins =
                    [
                        new PinMapEntry { Pad = "P1", Target = "VDD" },
                        new PinMapEntry { Pad = "P2", Target = "GND" },
                        new PinMapEntry { Pad = "P3", Target = "PWR_GOOD" },
                    ],
                },
            },
        };
        var derived = new PartDefinition
        {
            Name = "Mcu32",
            BasePart = "BaseMcu",
            Ports = [Port("PA[0:3]", "digital")],
            Catalog = EntryCatalog("_qfn", ("P4:P7", "PA[0:3]")),
        };

        var result = CompleteDocumentSemanticValidator.Validate(
            new CascodeDocument { Parts = [basePart, derived] }
        );

        Assert.True(result.IsValid, string.Join("\n", result.GetErrors().Select(e => e.Message)));
    }

    [Fact]
    public void Validate_SelectionMustResolveToSingleEffectiveEntry()
    {
        var result = Validate(
            VariantPart(),
            new InstanceDeclaration
            {
                Id = "r1",
                DeclaredType = "DualVariantRes",
                Type = "DualVariantRes",
                Selection = [new SelectionArgument { Axis = "body", Value = "_0603" }],
            }
        );

        Assert.Contains(result.GetErrors(), error => error.Code == "PART-008");
    }

    [Fact]
    public void Validate_ExcludedVariantCombinationIsRejected()
    {
        var result = Validate(
            VariantPart(),
            new InstanceDeclaration
            {
                Id = "r1",
                DeclaredType = "DualVariantRes",
                Type = "DualVariantRes",
                Selection =
                [
                    new SelectionArgument { Axis = "body", Value = "_0402" },
                    new SelectionArgument { Axis = "grade", Value = "F" },
                ],
            }
        );

        Assert.Contains(result.GetErrors(), error => error.Code == "PART-007");
    }

    [Fact]
    public void Validate_PinCoverageAndUnitsMustReferenceKnownTargets()
    {
        var result = CompleteDocumentSemanticValidator.Validate(
            new CascodeDocument
            {
                Parts =
                [
                    new PartDefinition
                    {
                        Name = "BadUnits",
                        Ports = [Port("P"), Port("N")],
                        Catalog = new PartCatalog
                        {
                            Entries =
                            [
                                new PartCatalogEntry
                                {
                                    Name = "_0402",
                                    Body = new PartCatalogBody
                                    {
                                        Pins = [new PinMapEntry { Pad = "P1", Target = "P" }],
                                        Units =
                                        [
                                            new UnitGroup
                                            {
                                                Name = "A",
                                                Fields = new Dictionary<string, string>
                                                {
                                                    ["pads"] = "(P1, P2)",
                                                    ["terminals"] = "(P, N)",
                                                },
                                            },
                                        ],
                                    },
                                },
                            ],
                        },
                    },
                ],
            }
        );

        Assert.Contains(result.GetErrors(), error => error.Code == "PART-011");
        Assert.Contains(result.GetErrors(), error => error.Code == "PART-012");
        Assert.Contains(result.GetErrors(), error => error.Code == "PART-013");
    }

    [Fact]
    public void Validate_TemplatePlaceholdersMustResolve()
    {
        var result = CompleteDocumentSemanticValidator.Validate(
            new CascodeDocument
            {
                Parts =
                [
                    new PartDefinition
                    {
                        Name = "PlaceholderRes",
                        Parameters = [new CircuitParameter { Name = "R", Type = "real" }],
                        Ports = [Port("P"), Port("N")],
                        Catalog = new PartCatalog
                        {
                            Defaults = new PartCatalogBody
                            {
                                Fields = new Dictionary<string, string>
                                {
                                    ["mpn"] = "\"R{body.code}{missing}\"",
                                },
                            },
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
                            Variants =
                            [
                                new PartVariantAxis
                                {
                                    Name = "body",
                                    Options =
                                    [
                                        new PartVariantOption
                                        {
                                            Name = "_0402",
                                            Body = new PartCatalogBody
                                            {
                                                Fields = new Dictionary<string, string>
                                                {
                                                    ["code"] = "\"A\"",
                                                },
                                            },
                                        },
                                    ],
                                },
                            ],
                        },
                    },
                ],
            }
        );

        Assert.Contains(result.GetErrors(), error => error.Code == "PART-014");
    }

    [Fact]
    public void Validate_ESeriesMembershipIsCheckedAtInstantiation()
    {
        var result = Validate(
            new PartDefinition
            {
                Name = "SeriesRes",
                Parameters = [new CircuitParameter { Name = "R", Type = "e12" }],
                Ports = [Port("P"), Port("N")],
                Catalog = EntryCatalog("_0402", ("P1", "P"), ("P2", "N")),
            },
            new InstanceDeclaration
            {
                Id = "r1",
                DeclaredType = "SeriesRes",
                Type = "SeriesRes",
                Selection = [new SelectionArgument { Value = "_0402" }],
                Params = new Dictionary<string, ParamValue>
                {
                    ["R"] = new ParamValue { Numeric = "1.3k" },
                },
            }
        );

        Assert.Contains(result.GetErrors(), error => error.Code == "PART-015");
    }

    private static ValidationResult Validate(
        PartDefinition part,
        InstanceDeclaration? instance = null
    )
    {
        var document = new CascodeDocument { Parts = [part] };
        if (instance is not null)
        {
            document = new CascodeDocument
            {
                Parts = [part],
                Circuits =
                [
                    new Circuit
                    {
                        Name = "Top",
                        Level = CascodeLevel.EL,
                        Fill = new FillBlock { Instances = [instance] },
                    },
                ],
            };
        }

        return CompleteDocumentSemanticValidator.Validate(document);
    }

    private static PartCatalog EntryCatalog(
        string entryName,
        params (string Pad, string Target)[] pins
    ) =>
        new()
        {
            Entries =
            [
                new PartCatalogEntry
                {
                    Name = entryName,
                    Body = new PartCatalogBody
                    {
                        Pins = pins.Select(pin => new PinMapEntry
                            {
                                Pad = pin.Pad,
                                Target = pin.Target,
                            })
                            .ToList(),
                    },
                },
            ],
        };

    private static PartDefinition VariantPart() =>
        new()
        {
            Name = "DualVariantRes",
            Ports = [Port("P"), Port("N")],
            Catalog = new PartCatalog
            {
                Entries =
                [
                    new PartCatalogEntry
                    {
                        Name = "_base",
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
                Variants =
                [
                    new PartVariantAxis
                    {
                        Name = "body",
                        Options =
                        [
                            new PartVariantOption { Name = "_0402" },
                            new PartVariantOption { Name = "_0603" },
                        ],
                    },
                    new PartVariantAxis
                    {
                        Name = "grade",
                        Options =
                        [
                            new PartVariantOption
                            {
                                Name = "F",
                                Excludes =
                                [
                                    new SelectionArgument { Axis = "body", Value = "_0402" },
                                ],
                            },
                            new PartVariantOption { Name = "J" },
                        ],
                    },
                ],
            },
        };

    private static PortDeclaration Port(string name, string type = "analog") =>
        new()
        {
            Direction = PortDirection.Io,
            Name = name,
            Type = type,
        };
}
