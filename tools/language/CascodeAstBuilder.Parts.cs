using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
    private PartDefinition BuildPart(CascodeParser.PartDefContext ctx)
    {
        var signature = BuildPartSignature(ctx.paramList());
        var part = new PartDefinition
        {
            Name = ctx.name.Text,
            IsAbstract = ctx.ABSTRACT_KW() is not null,
            BasePart = ctx.parentPart?.Text,
            BaseArguments = BuildPartArguments(ctx.argList()),
            Implements = ctx.implementsList()?.IDENT().Select(i => i.GetText()).ToList() ?? [],
            Parameters = signature.Parameters,
            Sizes = signature.Sizes,
        };

        foreach (var memberCtx in ctx.partMember())
        {
            if (memberCtx.paramsBlock() is not null)
            {
                foreach (var mapping in memberCtx.paramsBlock().paramMapping())
                {
                    part.ParameterMappings[mapping.IDENT().GetText()] = mapping
                        .paramExpr()
                        .GetText();
                }
                continue;
            }

            if (
                memberCtx.direction() is not null
                && memberCtx.portName() is not null
                && memberCtx.portType() is not null
            )
            {
                part.Ports.Add(
                    BuildPortDeclaration(
                        memberCtx.direction(),
                        memberCtx.portName(),
                        memberCtx.portType()
                    )
                );
                continue;
            }

            if (memberCtx.SUPPLY_KW() is not null)
            {
                part.Supplies.Add(memberCtx.IDENT().GetText());
                continue;
            }

            if (memberCtx.GROUND_KW() is not null)
            {
                part.Grounds.Add(memberCtx.IDENT().GetText());
                continue;
            }

            if (memberCtx.cornersBlock() is not null)
            {
                part.Corners.AddRange(BuildCorners(memberCtx.cornersBlock()));
                continue;
            }

            if (memberCtx.metricsValueBlock() is not null)
            {
                part.Metrics.AddRange(BuildMetricAssignments(memberCtx.metricsValueBlock()));
            }
        }

        part.Catalog = BuildPartCatalog(ctx.catalogBlock());
        return part;
    }

    private static Dictionary<string, ParamValue> BuildPartArguments(
        CascodeParser.ArgListContext? argList
    )
    {
        var args = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
        if (argList is null)
        {
            return args;
        }

        var positional = 0;
        foreach (var argCtx in argList.arg())
        {
            var name = argCtx.argName()?.GetText() ?? $"arg{positional++}";
            if (argCtx.argValue().scalarExpr() is not null)
            {
                args[name] = BuildScalarValue(argCtx.argValue().scalarExpr());
            }
            else
            {
                args[name] = new ParamValue { Symbolic = argCtx.argValue().GetText() };
            }
        }

        return args;
    }

    private static (
        List<CircuitParameter> Parameters,
        List<SizeDeclaration> Sizes
    ) BuildPartSignature(CascodeParser.ParamListContext? paramList)
    {
        var parameters = new List<CircuitParameter>();
        var sizes = new List<SizeDeclaration>();

        if (paramList is null)
        {
            return (parameters, sizes);
        }

        foreach (var paramCtx in paramList.paramDecl())
        {
            if (paramCtx.SIZE_KW() is not null)
            {
                sizes.Add(new SizeDeclaration { Name = paramCtx.sizeName.Text, Default = null });
                continue;
            }

            parameters.Add(
                new CircuitParameter
                {
                    Name = paramCtx.paramName.Text,
                    Type = paramCtx.paramType().GetText(),
                    Default = paramCtx.paramValue() is null
                        ? null
                        : BuildParamValue(paramCtx.paramValue()),
                }
            );
        }

        return (parameters, sizes);
    }

    private PartCatalog BuildPartCatalog(CascodeParser.CatalogBlockContext ctx)
    {
        var catalog = new PartCatalog();

        foreach (var member in ctx.catalogMember())
        {
            if (member.defaultsBlock() is not null)
            {
                MergeEntryData(
                    catalog.Defaults,
                    BuildPartEntryData(member.defaultsBlock().entryMember())
                );
                continue;
            }

            if (member.entryDef() is not null)
            {
                catalog.Entries.Add(
                    new PartCatalogEntry
                    {
                        Name = member.entryDef().entryName.Text,
                        Data = BuildPartEntryData(member.entryDef().entryMember()),
                    }
                );
                continue;
            }

            if (member.variantBlock() is not null)
            {
                catalog.Variants.Add(BuildVariantAxis(member.variantBlock()));
            }
        }

        return catalog;
    }

    private PartVariantAxis BuildVariantAxis(CascodeParser.VariantBlockContext ctx)
    {
        var axis = new PartVariantAxis { Name = ctx.axisName.Text };

        foreach (var optionCtx in ctx.variantOption())
        {
            var optionMembers = new List<CascodeParser.EntryMemberContext>();
            foreach (var member in optionCtx.variantOptionMember())
            {
                if (member.entryMember() is not null)
                {
                    optionMembers.Add(member.entryMember());
                }
            }

            var option = new PartVariantOption
            {
                Name = optionCtx.optionName.Text,
                Data = BuildPartEntryData(optionMembers),
            };

            foreach (var member in optionCtx.variantOptionMember())
            {
                if (member.excludeDirective() is null)
                {
                    continue;
                }

                option.Exclusions.Add(
                    new PartVariantExclusion(
                        member.excludeDirective().IDENT(0).GetText(),
                        member.excludeDirective().STRING() is not null
                            ? member.excludeDirective().STRING().GetText()
                            : member.excludeDirective().IDENT(1).GetText()
                    )
                );
            }

            axis.Options.Add(option);
        }

        return axis;
    }

    private PartEntryData BuildPartEntryData(
        IEnumerable<CascodeParser.EntryMemberContext> entryMembers
    )
    {
        var data = new PartEntryData();
        foreach (var member in entryMembers)
        {
            if (member.catalogOption() is not null)
            {
                data.Options.Add(BuildCatalogOption(member.catalogOption()));
                continue;
            }

            if (member.pinsBlock() is not null)
            {
                data.Pins.AddRange(BuildPins(member.pinsBlock()));
                continue;
            }

            if (member.unitsBlock() is not null)
            {
                data.Units.AddRange(BuildUnits(member.unitsBlock()));
                continue;
            }

            if (member.metricsValueBlock() is not null)
            {
                data.Metrics.AddRange(BuildMetricAssignments(member.metricsValueBlock()));
                continue;
            }

            if (member.mechanicalBlock() is not null)
            {
                foreach (var field in member.mechanicalBlock().mechanicalField())
                {
                    data.MechanicalFields[field.IDENT().GetText()] = field
                        .entryFieldValue()
                        .GetText();
                }
                continue;
            }

            data.Fields[member.IDENT().GetText()] = member.entryFieldValue().GetText();
        }

        return data;
    }

    private static void MergeEntryData(PartEntryData target, PartEntryData source)
    {
        foreach (var field in source.Fields)
        {
            target.Fields[field.Key] = field.Value;
        }

        target.Options.AddRange(source.Options);
        target.Pins.AddRange(source.Pins);
        target.Units.AddRange(source.Units);
        target.Metrics.AddRange(source.Metrics);

        foreach (var field in source.MechanicalFields)
        {
            target.MechanicalFields[field.Key] = field.Value;
        }
    }

    private static PartCatalogOption BuildCatalogOption(CascodeParser.CatalogOptionContext ctx)
    {
        var option = new PartCatalogOption();
        foreach (var field in ctx.catalogOptionField())
        {
            option.Fields[field.IDENT().GetText()] =
                field.STRING()?.GetText() ?? field.NUMBER()?.GetText() ?? string.Empty;
        }

        return option;
    }

    private static IEnumerable<PartPinMap> BuildPins(CascodeParser.PinsBlockContext ctx)
    {
        foreach (var map in ctx.pinMapEntry())
        {
            var pinMap = new PartPinMap
            {
                Terminal = BuildPinRef(map.pinRef()),
                IsPadRange = map.padMap().padRange() is not null,
            };
            if (map.padMap().padRange() is not null)
            {
                pinMap.Pads.Add(map.padMap().padRange().GetText());
            }
            else
            {
                pinMap.Pads.AddRange(map.padMap().padRef().Select(p => p.GetText()));
            }

            yield return pinMap;
        }
    }

    private static IEnumerable<PartUnitGroup> BuildUnits(CascodeParser.UnitsBlockContext ctx)
    {
        foreach (var unitDef in ctx.unitDef())
        {
            var unit = new PartUnitGroup { Name = unitDef.IDENT().GetText() };
            foreach (var field in unitDef.unitField())
            {
                unit.Fields[field.IDENT().GetText()] = field.tupleLiteral().GetText();
            }
            yield return unit;
        }
    }

    private static IEnumerable<CornerDefinition> BuildCorners(CascodeParser.CornersBlockContext ctx)
    {
        foreach (var cornerDef in ctx.cornerDef())
        {
            var corner = new CornerDefinition { Name = cornerDef.IDENT().GetText() };
            foreach (var field in cornerDef.cornerField())
            {
                corner.Fields[field.IDENT().GetText()] = field.GetChild(2).GetText();
            }
            yield return corner;
        }
    }

    private static List<MetricAssignment> BuildMetricAssignments(
        CascodeParser.MetricsValueBlockContext ctx
    )
    {
        var assignments = new List<MetricAssignment>();
        foreach (var entry in ctx.metricsEntry())
        {
            if (entry.metricAssign() is not null)
            {
                assignments.Add(BuildMetricAssignment(entry.metricAssign(), corner: null));
                continue;
            }

            var cornerName = entry.cornerMetricsBlock().cornerName.Text;
            foreach (var assign in entry.cornerMetricsBlock().metricAssign())
            {
                assignments.Add(BuildMetricAssignment(assign, cornerName));
            }
        }

        return assignments;
    }

    private static MetricAssignment BuildMetricAssignment(
        CascodeParser.MetricAssignContext ctx,
        string? corner
    )
    {
        return new MetricAssignment
        {
            Name = ctx.IDENT().GetText(),
            Qualifier = ctx.metricQualifier() is null
                ? null
                : ParseMetricQualifier(ctx.metricQualifier()),
            Corner = corner,
            Value = BuildMetricAssignmentValue(ctx.metricValue()),
        };
    }

    private static MetricAssignmentValue BuildMetricAssignmentValue(
        CascodeParser.MetricValueContext ctx
    )
    {
        if (ctx.signedQuantity() is not null)
        {
            return new MetricAssignmentValue { Scalar = ctx.signedQuantity().GetText() };
        }

        if (ctx.metricSource().benchMetricRef() is not null)
        {
            return new MetricAssignmentValue
            {
                Source = new MetricSourceReference
                {
                    Kind = "bench",
                    Value = ctx.metricSource().benchMetricRef().GetText(),
                },
            };
        }

        return new MetricAssignmentValue
        {
            Source = new MetricSourceReference
            {
                Kind = "instance",
                Value = ctx.metricSource().instanceMetricRef().GetText(),
            },
        };
    }

    private static MetricContract BuildMetricContract(CascodeParser.MetricDeclContext ctx)
    {
        return new MetricContract
        {
            Name = ctx.IDENT().GetText(),
            Unit = ctx.unitType().GetText(),
            RequiredQualifiers =
                ctx.qualifierRequirement()?.metricQualifier().Select(ParseMetricQualifier).ToList()
                ?? [],
        };
    }

    private static MetricQualifier ParseMetricQualifier(CascodeParser.MetricQualifierContext ctx)
    {
        if (ctx.MIN_KW() is not null)
        {
            return MetricQualifier.Min;
        }

        if (ctx.MAX_KW() is not null)
        {
            return MetricQualifier.Max;
        }

        return MetricQualifier.Typ;
    }
}
