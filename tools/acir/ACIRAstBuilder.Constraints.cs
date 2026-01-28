using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cascode.Parser;

namespace Cascode.ACIR;

internal sealed partial class ACIRAstBuilder
{
    /// <summary>Builds the constraints block from its section context.</summary>
    /// <param name="ctx">Constraints section context.</param>
    /// <returns>Constraints block.</returns>
    private ConstraintsBlock BuildConstraintsBlock(ACIRParser.ConstraintsSectionContext ctx)
    {
        var constraints = new ConstraintsBlock();

        foreach (var sectionCtx in ctx.constraintSection())
        {
            switch (sectionCtx)
            {
                case ACIRParser.NumericSectionContext numericCtx:
                    foreach (var constraintCtx in numericCtx.numericConstraint())
                    {
                        constraints.Numeric.Add(BuildNumericConstraint(constraintCtx));
                    }
                    break;

                case ACIRParser.TechSectionContext techCtx:
                    foreach (var constraintCtx in techCtx.techConstraint())
                    {
                        constraints.Tech.Add(BuildTechConstraint(constraintCtx));
                    }
                    break;

                case ACIRParser.GraphSectionContext graphCtx:
                    foreach (var constraintCtx in graphCtx.graphConstraint())
                    {
                        constraints.Graph.Add(BuildGraphConstraint(constraintCtx));
                    }
                    break;
            }
        }

        return constraints;
    }

    /// <summary>Builds a numeric constraint from its parse context.</summary>
    /// <param name="ctx">Numeric constraint context.</param>
    /// <returns>Numeric constraint.</returns>
    private static NumericConstraint BuildNumericConstraint(ACIRParser.NumericConstraintContext ctx)
    {
        var id = ctx.IDENT().GetText();
        var benchRef = ctx.benchMetricRef();
        var bench = benchRef.IDENT(0).GetText();
        var metric = benchRef.IDENT(1).GetText();
        var node = ctx.nodeRef() != null ? BuildNodeRef(ctx.nodeRef()) : null;
        var op = ctx.COMPARISON_OP().GetText();
        var quantity = ctx.QUANTITY().GetText();
        var (value, unit) = ParseQuantity(quantity);

        return new NumericConstraint
        {
            Id = id,
            Bench = bench,
            Metric = metric,
            Node = node,
            Op = op,
            Value = value,
            Unit = unit,
        };
    }

    /// <summary>Builds a technology constraint from its parse context.</summary>
    /// <param name="ctx">Tech constraint context.</param>
    /// <returns>Technology constraint.</returns>
    private static TechConstraint BuildTechConstraint(ACIRParser.TechConstraintContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var param = ctx.IDENT(1).GetText();
        var scope = ctx.techConstraintScope().GetText();
        var op = ctx.COMPARISON_OP().GetText();
        var quantity = ctx.QUANTITY().GetText();
        var (value, unit) = ParseQuantity(quantity);

        return new TechConstraint
        {
            Id = id,
            Param = param,
            Op = op,
            Value = value,
            Unit = unit,
            Scope = scope,
        };
    }

    /// <summary>Builds a graph constraint with optional properties.</summary>
    /// <param name="ctx">Graph constraint context.</param>
    /// <returns>Graph constraint.</returns>
    private static GraphConstraint BuildGraphConstraint(ACIRParser.GraphConstraintContext ctx)
    {
        var id = ctx.IDENT(0).GetText();
        var rule = ctx.IDENT(1).GetText();
        var props = new Dictionary<string, string>();

        if (ctx.graphProps() != null)
        {
            foreach (var propCtx in ctx.graphProps().graphProp())
            {
                var key = propCtx.IDENT(0).GetText();
                var value =
                    propCtx.IDENT().Length > 1
                        ? propCtx.IDENT(1).GetText()
                        : propCtx.NUMBER()?.GetText()
                            ?? propCtx.QUANTITY()?.GetText()
                            ?? propCtx.STRING()?.GetText()
                            ?? string.Empty;
                props[key] = value;
            }
        }

        return new GraphConstraint
        {
            Id = id,
            Rule = rule,
            Properties = props,
        };
    }

    private static NodeRef BuildNodeRef(ACIRParser.NodeRefContext ctx)
    {
        var scopeToken = ctx.nodeScope();
        var scope =
            scopeToken.IDENT()?.GetText()
            ?? scopeToken.NET_KW()?.GetText()
            ?? scopeToken.PORT_KW()?.GetText()
            ?? string.Empty;
        return new NodeRef { Scope = scope, Path = BuildPinRef(ctx.pinRef()) };
    }

    /// <summary>Builds the harness block for supplies, biases, loads, and sweeps.</summary>
    /// <param name="ctx">Harness section context.</param>
    /// <returns>Harness block.</returns>
    private HarnessBlock BuildHarnessBlock(ACIRParser.HarnessSectionContext ctx)
    {
        var supplies = new List<SupplyValue>();
        var biases = new List<BiasValue>();
        var sources = new List<SourceValue>();
        var loads = new List<LoadValue>();
        var sweeps = new List<SweepCondition>();
        var pvt = new List<string>();
        IcmrRange? icmr = null;

        foreach (var stmtCtx in ctx.harnessStatement())
        {
            switch (stmtCtx)
            {
                case ACIRParser.HarnessSupplyContext supplyCtx:
                    supplies.Add(
                        new SupplyValue
                        {
                            Net = supplyCtx.IDENT().GetText(),
                            Value = BuildHarnessValue(supplyCtx.harnessValue()),
                        }
                    );
                    break;

                case ACIRParser.HarnessBiasContext biasCtx:
                    biases.Add(
                        new BiasValue
                        {
                            Net = biasCtx.IDENT().GetText(),
                            Value = BuildHarnessValue(biasCtx.harnessValue()),
                        }
                    );
                    break;

                case ACIRParser.HarnessLoadContext loadCtx:
                    loads.Add(BuildLoad(loadCtx));
                    break;

                case ACIRParser.HarnessSourceContext sourceCtx:
                    var sourceSpec = sourceCtx.sourceSpec();
                    var zValue =
                        sourceSpec.QUANTITY()?.GetText()
                        ?? sourceSpec.NUMBER()?.GetText()
                        ?? string.Empty;
                    // Normalize: if no unit, add "Ohm"
                    if (sourceSpec.NUMBER() != null)
                    {
                        zValue = zValue + "Ohm";
                    }
                    sources.Add(new SourceValue { Net = sourceCtx.IDENT().GetText(), Z = zValue });
                    break;

                case ACIRParser.HarnessSweepContext sweepCtx:
                    sweeps.Add(BuildSweep(sweepCtx));
                    break;

                case ACIRParser.HarnessIcmrContext icmrCtx:
                    icmr = new IcmrRange
                    {
                        Min = icmrCtx.QUANTITY(0).GetText(),
                        Max = icmrCtx.QUANTITY(1).GetText(),
                    };
                    break;

                case ACIRParser.HarnessPvtContext pvtCtx:
                    pvt.AddRange(pvtCtx.pvtList().IDENT().Select(i => i.GetText()));
                    break;
            }
        }

        return new HarnessBlock
        {
            Supplies = supplies,
            Biases = biases,
            Sources = sources,
            Loads = loads,
            Sweeps = sweeps,
            Icmr = icmr,
            Pvt = pvt,
        };
    }

    /// <summary>Builds a load value with one or more load elements.</summary>
    /// <param name="ctx">Harness load context.</param>
    /// <returns>Load value.</returns>
    private static LoadValue BuildLoad(ACIRParser.HarnessLoadContext ctx)
    {
        var net = ctx.IDENT().GetText();
        var elements = new List<LoadElement>();

        var loadSpec = ctx.loadSpec();
        switch (loadSpec)
        {
            case ACIRParser.SimpleLoadSpecContext simpleCtx:
                foreach (var elemCtx in simpleCtx.loadElement())
                {
                    elements.Add(BuildLoadElement(elemCtx));
                }
                break;

            case ACIRParser.ParenLoadSpecContext parenCtx:
                foreach (var elemCtx in parenCtx.loadElement())
                {
                    elements.Add(BuildLoadElement(elemCtx));
                }
                break;
        }

        return new LoadValue { Net = net, Elements = elements };
    }

    /// <summary>Builds a single load element from its parse context.</summary>
    /// <param name="ctx">Load element context.</param>
    /// <returns>Load element.</returns>
    private static LoadElement BuildLoadElement(ACIRParser.LoadElementContext ctx)
    {
        var idents = ctx.IDENT();
        var type = idents[0].GetText();
        var value = ctx.QUANTITY()?.GetText() ?? ctx.NUMBER()?.GetText() ?? string.Empty;
        var unit = idents.Length > 1 ? idents[1].GetText() : null;

        // Normalize legacy format: combine value and unit (e.g., "1p" + "F" -> "1pF")
        if (unit != null)
        {
            value = value + unit;
        }

        return new LoadElement(type, value);
    }

    /// <summary>Builds a sweep condition from its parse context.</summary>
    /// <param name="ctx">Harness sweep context.</param>
    /// <returns>Sweep condition.</returns>
    private static SweepCondition BuildSweep(ACIRParser.HarnessSweepContext ctx)
    {
        var name = ctx.IDENT().GetText();
        var sweepSpec = ctx.sweepSpec();

        if (sweepSpec.AUTO_KW() != null)
        {
            return new SweepCondition
            {
                Name = name,
                IsAuto = true,
                Start = string.Empty,
                Stop = string.Empty,
            };
        }

        var rangeCtx = sweepSpec.sweepRange();
        switch (rangeCtx)
        {
            case ACIRParser.ExplicitSweepContext explicitCtx:
                return new SweepCondition
                {
                    Name = name,
                    Start = BuildSweepValue(explicitCtx.sweepValue(0)),
                    Step = BuildSweepValue(explicitCtx.sweepValue(1)),
                    Stop = BuildSweepValue(explicitCtx.sweepValue(2)),
                    IsAuto = false,
                };

            case ACIRParser.AutoStepSweepContext autoCtx:
                return new SweepCondition
                {
                    Name = name,
                    Start = BuildSweepValue(autoCtx.sweepValue(0)),
                    Stop = BuildSweepValue(autoCtx.sweepValue(1)),
                    IsAuto = false,
                };

            default:
                return new SweepCondition { Name = name };
        }
    }

    /// <summary>Builds a sweep value string, normalizing units when needed.</summary>
    /// <param name="ctx">Sweep value context.</param>
    /// <returns>Normalized sweep value.</returns>
    private static string BuildSweepValue(ACIRParser.SweepValueContext ctx)
    {
        if (ctx.QUANTITY() != null)
        {
            return ctx.QUANTITY().GetText();
        }
        // Normalize: combine NUMBER and optional IDENT unit (e.g., "0.3" + "V" -> "0.3V")
        var value = ctx.NUMBER()?.GetText() ?? string.Empty;
        var unit = ctx.IDENT()?.GetText();
        if (unit != null)
        {
            return value + unit;
        }
        return value;
    }

    /// <summary>Builds a harness value string, normalizing units when needed.</summary>
    /// <param name="ctx">Harness value context.</param>
    /// <returns>Normalized harness value.</returns>
    private static string BuildHarnessValue(ACIRParser.HarnessValueContext ctx)
    {
        if (ctx.QUANTITY() != null)
        {
            return ctx.QUANTITY().GetText();
        }
        // Normalize: combine NUMBER and optional IDENT unit (e.g., "1.8" + "V" -> "1.8V")
        var value = ctx.NUMBER()?.GetText() ?? string.Empty;
        var unit = ctx.IDENT()?.GetText();
        if (unit != null)
        {
            return value + unit;
        }
        return value;
    }

    /// <summary>Builds the provenance block with sources, transforms, and aliases.</summary>
    /// <param name="ctx">Provenance section context.</param>
    /// <returns>Provenance block.</returns>
    private static ProvenanceBlock BuildProvenanceBlock(ACIRParser.ProvenanceSectionContext ctx)
    {
        var provenance = new ProvenanceBlock();

        foreach (var entryCtx in ctx.provenanceEntry())
        {
            switch (entryCtx)
            {
                case ACIRParser.ProvenanceSourceContext sourceCtx:
                    var file = sourceCtx.STRING().GetText()[1..^1]; // Remove quotes
                    int? fromLine = null;
                    int? toLine = null;
                    if (sourceCtx.NUMBER().Length >= 2)
                    {
                        fromLine = int.Parse(sourceCtx.NUMBER(0).GetText());
                        toLine = int.Parse(sourceCtx.NUMBER(1).GetText());
                    }
                    provenance.Sources.Add(
                        new SourceReference
                        {
                            File = file,
                            FromLine = fromLine,
                            ToLine = toLine,
                        }
                    );
                    break;

                case ACIRParser.ProvenanceTransformContext transformCtx:
                    provenance.Transforms.Add(transformCtx.STRING().GetText()[1..^1]);
                    break;

                case ACIRParser.ProvenanceAliasContext aliasCtx:
                    provenance.Aliases[aliasCtx.IDENT(0).GetText()] = aliasCtx.IDENT(1).GetText();
                    break;
            }
        }

        return provenance;
    }

    /// <summary>Splits a quantity string into numeric value and unit.</summary>
    /// <param name="quantity">Quantity text such as "1.8V".</param>
    /// <returns>Tuple of numeric value and unit.</returns>
    private static (string Value, string Unit) ParseQuantity(string quantity)
    {
        // Match patterns like "50MHz", "30dB", "60deg", "1.8V"
        var match = QuantityPattern().Match(quantity);
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }
        return (quantity, string.Empty);
    }

    /// <summary>Regex used to parse quantity value/unit pairs.</summary>
    [GeneratedRegex(@"^(-?[0-9][0-9.eE+-]*[fpnumkMGT]?)([A-Za-z]+)$")]
    private static partial Regex QuantityPattern();
}
