using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
    /// <summary>Builds the constraints block from its section context.</summary>
    /// <param name="ctx">Constraints section context.</param>
    /// <returns>Constraints block.</returns>
    private ConstraintsBlock BuildConstraintsBlock(CascodeParser.ConstraintsSectionContext ctx)
    {
        var constraints = new ConstraintsBlock();

        foreach (var sectionCtx in ctx.constraintSection())
        {
            switch (sectionCtx)
            {
                case CascodeParser.BenchSectionContext benchCtx:
                    foreach (var constraintCtx in benchCtx.numericConstraint())
                    {
                        constraints.Bench.Add(BuildMetricConstraint(constraintCtx));
                    }
                    break;

                case CascodeParser.SpecSectionContext specCtx:
                    foreach (var constraintCtx in specCtx.numericConstraint())
                    {
                        constraints.Spec.Add(BuildMetricConstraint(constraintCtx));
                    }
                    break;

                case CascodeParser.PhysicalSectionContext physicalCtx:
                    foreach (var constraintCtx in physicalCtx.techConstraint())
                    {
                        constraints.Physical.Add(BuildPhysicalConstraint(constraintCtx));
                    }
                    break;
            }
        }

        return constraints;
    }

    /// <summary>Builds a metric constraint from its parse context.</summary>
    /// <param name="ctx">Metric constraint context.</param>
    /// <returns>Metric constraint.</returns>
    private static MetricConstraint BuildMetricConstraint(
        CascodeParser.NumericConstraintContext ctx
    )
    {
        var id = ctx.IDENT().GetText();
        var source = ctx.constraintMetricRef().GetText();
        var benchRef = ctx.constraintMetricRef().benchMetricRef();
        var benchBase = string.Empty;
        var metric = source;
        var argLists = benchRef?.measurementArgList() ?? [];
        var benchArgs = new List<MetricCallArg>();
        var metricArgs = new List<MetricCallArg>();

        if (benchRef is not null)
        {
            benchBase = benchRef.IDENT().GetText();
            metric = benchRef.idPart().GetText();

            if (argLists.Length == 2)
            {
                benchArgs = ExtractConstraintArgs(argLists[0], "bench invocation");
                metricArgs = ExtractConstraintArgs(argLists[1], "metric invocation");
            }
            else if (argLists.Length == 1)
            {
                var colonColonIndex = benchRef.COLONCOLON().Symbol.TokenIndex;
                var argListStartIndex = argLists[0].Start.TokenIndex;
                if (argListStartIndex < colonColonIndex)
                {
                    benchArgs = ExtractConstraintArgs(argLists[0], "bench invocation");
                }
                else
                {
                    metricArgs = ExtractConstraintArgs(argLists[0], "metric invocation");
                }
            }
        }

        var node = ctx.nodeRef() != null ? BuildNodeRef(ctx.nodeRef()) : null;
        var op = ctx.COMPARISON_OP().GetText();
        var threshold = ParseThreshold(ctx.signedThreshold());
        var (value, unit) = ParseQuantity(threshold);

        // Compute the bench instance name from BenchBase + BenchArgs.
        var bench =
            benchArgs.Count == 0
                ? benchBase
                : BenchRuntime.BenchInvocationName.Compute(benchBase, benchArgs);

        return new MetricConstraint
        {
            Id = id,
            Source = source,
            BenchBase = benchBase,
            BenchArgs = benchArgs,
            Bench = bench,
            Metric = metric,
            MetricArgs = metricArgs,
            Node = node,
            Op = op,
            Value = value,
            Unit = unit,
        };
    }

    private static List<MetricCallArg> ExtractConstraintArgs(
        CascodeParser.MeasurementArgListContext argList,
        string context
    )
    {
        var args = new List<MetricCallArg>();
        foreach (var arg in argList.measurementArg())
        {
            if (arg.idPart() is null)
            {
                throw new InvalidOperationException(
                    $"Numeric constraint {context} requires named arguments."
                );
            }

            args.Add(new MetricCallArg(arg.idPart().GetText(), arg.measurementExpr().GetText()));
        }
        return args;
    }

    /// <summary>Builds a physical constraint from its parse context.</summary>
    /// <param name="ctx">Physical constraint context.</param>
    /// <returns>Physical constraint.</returns>
    private static PhysicalConstraint BuildPhysicalConstraint(
        CascodeParser.TechConstraintContext ctx
    )
    {
        var id = ctx.IDENT(0).GetText();
        var param = ctx.IDENT(1).GetText();
        var scope = ctx.techConstraintScope().GetText();
        var op = ctx.COMPARISON_OP().GetText();
        var threshold = ParseThreshold(ctx.signedThreshold());
        var (value, unit) = ParseQuantity(threshold);

        return new PhysicalConstraint
        {
            Id = id,
            Param = param,
            Op = op,
            Value = value,
            Unit = unit,
            Scope = scope,
        };
    }

    private static NodeRef BuildNodeRef(CascodeParser.NodeRefContext ctx)
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
    private HarnessBlock BuildHarnessBlock(CascodeParser.HarnessSectionContext ctx)
    {
        var grounds = new List<GroundValue>();
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
                case CascodeParser.HarnessGroundContext groundCtx:
                    grounds.Add(
                        new GroundValue
                        {
                            Net = groundCtx.IDENT().GetText(),
                            Value = BuildHarnessValue(groundCtx.harnessValue()),
                        }
                    );
                    break;

                case CascodeParser.HarnessSupplyContext supplyCtx:
                    supplies.Add(
                        new SupplyValue
                        {
                            Net = supplyCtx.IDENT().GetText(),
                            Value = BuildHarnessValue(supplyCtx.harnessValue()),
                        }
                    );
                    break;

                case CascodeParser.HarnessBiasContext biasCtx:
                    biases.Add(
                        new BiasValue
                        {
                            Net = biasCtx.IDENT().GetText(),
                            Value = BuildHarnessValue(biasCtx.harnessValue()),
                        }
                    );
                    break;

                case CascodeParser.HarnessLoadContext loadCtx:
                    loads.Add(BuildLoad(loadCtx));
                    break;

                case CascodeParser.HarnessSourceContext sourceCtx:
                    var sourceSpec = sourceCtx.sourceSpec();
                    var zValue = sourceSpec.signedQuantity().GetText();
                    sources.Add(new SourceValue { Net = sourceCtx.IDENT().GetText(), Z = zValue });
                    break;

                case CascodeParser.HarnessSweepContext sweepCtx:
                    sweeps.Add(BuildSweep(sweepCtx));
                    break;

                case CascodeParser.HarnessIcmrContext icmrCtx:
                    icmr = new IcmrRange
                    {
                        Min = icmrCtx.signedQuantity(0).GetText(),
                        Max = icmrCtx.signedQuantity(1).GetText(),
                    };
                    break;

                case CascodeParser.HarnessPvtContext pvtCtx:
                    pvt.AddRange(pvtCtx.pvtList().IDENT().Select(i => i.GetText()));
                    break;
            }
        }

        return new HarnessBlock
        {
            Grounds = grounds,
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
    private static LoadValue BuildLoad(CascodeParser.HarnessLoadContext ctx)
    {
        var net = ctx.IDENT().GetText();
        var elements = new List<LoadElement>();

        var loadSpec = ctx.loadSpec();
        switch (loadSpec)
        {
            case CascodeParser.SimpleLoadSpecContext simpleCtx:
                foreach (var elemCtx in simpleCtx.loadElement())
                {
                    elements.Add(BuildLoadElement(elemCtx));
                }
                break;

            case CascodeParser.ParenLoadSpecContext parenCtx:
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
    private static LoadElement BuildLoadElement(CascodeParser.LoadElementContext ctx)
    {
        var type = ctx.IDENT().GetText();
        var value = ctx.signedQuantity().GetText();
        return new LoadElement(type, value);
    }

    /// <summary>Builds a sweep condition from its parse context.</summary>
    /// <param name="ctx">Harness sweep context.</param>
    /// <returns>Sweep condition.</returns>
    private static SweepCondition BuildSweep(CascodeParser.HarnessSweepContext ctx)
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
            case CascodeParser.ExplicitSweepContext explicitCtx:
                return new SweepCondition
                {
                    Name = name,
                    Start = BuildSweepValue(explicitCtx.sweepValue(0)),
                    Step = BuildSweepValue(explicitCtx.sweepValue(1)),
                    Stop = BuildSweepValue(explicitCtx.sweepValue(2)),
                    IsAuto = false,
                };

            case CascodeParser.AutoStepSweepContext autoCtx:
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

    /// <summary>Builds a sweep value string.</summary>
    /// <param name="ctx">Sweep value context.</param>
    /// <returns>Sweep value.</returns>
    private static string BuildSweepValue(CascodeParser.SweepValueContext ctx)
    {
        return ctx.signedQuantity().GetText();
    }

    /// <summary>Builds a harness value string.</summary>
    /// <param name="ctx">Harness value context.</param>
    /// <returns>Harness value.</returns>
    private static string BuildHarnessValue(CascodeParser.HarnessValueContext ctx)
    {
        return ctx.signedQuantity().GetText();
    }

    /// <summary>Builds the provenance block with sources, transforms, and aliases.</summary>
    /// <param name="ctx">Provenance section context.</param>
    /// <returns>Provenance block.</returns>
    private static ProvenanceBlock BuildProvenanceBlock(CascodeParser.ProvenanceSectionContext ctx)
    {
        var provenance = new ProvenanceBlock();

        foreach (var entryCtx in ctx.provenanceEntry())
        {
            switch (entryCtx)
            {
                case CascodeParser.ProvenanceSourceContext sourceCtx:
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

                case CascodeParser.ProvenanceTransformContext transformCtx:
                    provenance.Transforms.Add(transformCtx.STRING().GetText()[1..^1]);
                    break;

                case CascodeParser.ProvenanceAliasContext aliasCtx:
                    provenance.Aliases[aliasCtx.IDENT(0).GetText()] = aliasCtx.IDENT(1).GetText();
                    break;
            }
        }

        return provenance;
    }

    /// <summary>Splits a quantity string into numeric value and unit.</summary>
    /// <param name="quantity">Quantity text such as "1.8V".</param>
    /// <returns>Tuple of numeric value and unit.</returns>
    private static string ParseThreshold(CascodeParser.SignedThresholdContext threshold)
    {
        return threshold.signedQuantity()?.GetText() ?? threshold.GetText();
    }

    /// <summary>Splits a quantity string into numeric value and unit.</summary>
    /// <param name="quantity">Quantity text such as "1.8V".</param>
    /// <returns>Tuple of numeric value and unit.</returns>
    private static (string Value, string Unit) ParseQuantity(string quantity)
    {
        return QuantityLiteral.SplitValueAndUnit(quantity);
    }
}
