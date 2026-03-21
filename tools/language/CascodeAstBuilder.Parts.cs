using System.Collections.Generic;
using System.Linq;

namespace Cascode.Language;

internal sealed partial class CascodeAstBuilder
{
    private MetricsBlock BuildDeclaredMetrics(CascodeParser.InterfaceMetricsBlockContext ctx)
    {
        var metrics = new MetricsBlock();
        foreach (var decl in ctx.metricDecl())
        {
            metrics.Declarations.Add(
                new MetricDeclaration
                {
                    Name = decl.IDENT().GetText(),
                    Unit = decl.unitType().GetText(),
                    RequiredQualifiers =
                        decl.qualifierRequirement()
                            ?.metricQualifier()
                            .Select(q => q.GetText())
                            .ToList()
                        ?? new List<string>(),
                }
            );
        }

        return metrics;
    }

    private MetricsBlock BuildMetricsBlock(CascodeParser.MetricsValueBlockContext ctx)
    {
        var metrics = new MetricsBlock();
        foreach (var entry in ctx.metricsEntry())
        {
            if (entry.AT_KW() is null)
            {
                metrics.Assignments.Add(BuildMetricAssignment(entry.metricAssign()[0], null));
                continue;
            }

            var cornerName = entry.IDENT().GetText();
            foreach (var assign in entry.metricAssign())
            {
                metrics.Assignments.Add(BuildMetricAssignment(assign, cornerName));
            }
        }

        return metrics;
    }

    private MetricsBlock BuildMetricsBlock(CascodeParser.BindingMetricsBlockContext ctx)
    {
        var metrics = new MetricsBlock();
        foreach (var assign in ctx.metricAssign())
        {
            metrics.Assignments.Add(BuildMetricAssignment(assign, null));
        }

        return metrics;
    }

    private static MetricAssignment BuildMetricAssignment(
        CascodeParser.MetricAssignContext ctx,
        string? corner
    )
    {
        return new MetricAssignment
        {
            Name = ctx.IDENT().GetText(),
            Qualifier = ctx.metricQualifier()?.GetText(),
            Corner = corner,
            Value = ctx.expr().GetText(),
        };
    }

    private PartDefinition BuildPart(CascodeParser.PartDefContext ctx)
    {
        var parameters = new List<CircuitParameter>();
        if (ctx.paramList() is not null)
        {
            foreach (var param in ctx.paramList().paramDecl())
            {
                if (param.SIZE_KW() is null)
                {
                    parameters.Add(BuildCircuitParameter(param));
                }
            }
        }

        var part = new PartDefinition
        {
            Name = ctx.name.Text,
            IsAbstract = ctx.ABSTRACT_KW() is not null,
            BasePart = ctx.@base?.Text,
            Implements =
                ctx.implementsClause()?.interfaceList()?.idPart().Select(i => i.GetText()).ToList()
                ?? new List<string>(),
            Parameters = parameters,
            Catalog = BuildCatalog(ctx.catalogBlock()),
        };

        foreach (var member in ctx.partMember())
        {
            switch (member)
            {
                case CascodeParser.PartParamsContext paramsCtx:
                    foreach (var mapping in paramsCtx.paramsBlock().paramMapping())
                    {
                        part.ParamMappings[mapping.IDENT().GetText()] = mapping
                            .paramExpr()
                            .GetText();
                    }
                    break;

                case CascodeParser.PartPortContext portCtx:
                    part.Ports.Add(
                        new PortDeclaration
                        {
                            Direction = BuildPortDirection(portCtx.direction()),
                            Name = BuildPortName(portCtx.portName()),
                            Type = BuildPortType(portCtx.portType()),
                        }
                    );
                    break;

                case CascodeParser.PartSupplyContext supplyCtx:
                    part.Supplies.Add(supplyCtx.IDENT().GetText());
                    break;

                case CascodeParser.PartGroundContext groundCtx:
                    part.Grounds.Add(groundCtx.IDENT().GetText());
                    break;

                case CascodeParser.PartCornersContext cornersCtx:
                    foreach (var cornerCtx in cornersCtx.cornersBlock().cornerDef())
                    {
                        var fields = new Dictionary<string, string>();
                        foreach (var field in cornerCtx.cornerField())
                        {
                            fields[field.IDENT(0).GetText()] = field.GetChild(2).GetText();
                        }

                        part.Corners.Add(
                            new PartCorner { Name = cornerCtx.IDENT().GetText(), Fields = fields }
                        );
                    }
                    break;
            }
        }

        return part;
    }

    private PartCatalog BuildCatalog(CascodeParser.CatalogBlockContext ctx)
    {
        var catalog = new PartCatalog();
        foreach (var member in ctx.catalogMember())
        {
            if (member.defaultsBlock() is not null)
            {
                catalog = new PartCatalog
                {
                    Defaults = BuildCatalogBody(member.defaultsBlock().entryMember()),
                    Entries = catalog.Entries,
                    Variants = catalog.Variants,
                };
                continue;
            }

            if (member.entryDef() is not null)
            {
                catalog.Entries.Add(
                    new PartCatalogEntry
                    {
                        Name = member.entryDef().name.Text,
                        Body = BuildCatalogBody(member.entryDef().entryMember()),
                    }
                );
                continue;
            }

            if (member.variantBlock() is not null)
            {
                var axis = new PartVariantAxis { Name = member.variantBlock().axis.Text };
                foreach (var optionCtx in member.variantBlock().variantOption())
                {
                    var option = new PartVariantOption
                    {
                        Name = optionCtx.name.GetText(),
                        Body = BuildCatalogBody(
                            optionCtx
                                .variantOptionMember()
                                .Where(m => m.entryMember() is not null)
                                .Select(m => m.entryMember())
                        ),
                    };

                    foreach (
                        var excludeCtx in optionCtx
                            .variantOptionMember()
                            .Where(m => m.excludeDirective() is not null)
                            .Select(m => m.excludeDirective())
                    )
                    {
                        option.Excludes.Add(
                            new SelectionArgument
                            {
                                Axis = excludeCtx.IDENT().GetText(),
                                Value = excludeCtx.idPart().GetText(),
                            }
                        );
                    }

                    axis.Options.Add(option);
                }

                catalog.Variants.Add(axis);
            }
        }

        return catalog;
    }

    private PartCatalogBody BuildCatalogBody(IEnumerable<CascodeParser.EntryMemberContext> members)
    {
        var body = new PartCatalogBody();
        foreach (var member in members)
        {
            if (member.catalogOption() is not null)
            {
                var option = new CatalogOption();
                foreach (var field in member.catalogOption().optionField())
                {
                    option.Fields[field.idPart(0).GetText()] = field.GetChild(2).GetText();
                }

                body.Options.Add(option);
                continue;
            }

            if (member.pinsBlock() is not null)
            {
                foreach (var entry in member.pinsBlock().pinMapEntry())
                {
                    body.Pins.Add(
                        new PinMapEntry
                        {
                            Pad = entry.padMap().GetText(),
                            Target = entry.pinMapTarget().GetText(),
                        }
                    );
                }

                continue;
            }

            if (member.unitsBlock() is not null)
            {
                foreach (var unitCtx in member.unitsBlock().unitDef())
                {
                    var unit = new UnitGroup { Name = unitCtx.IDENT().GetText() };
                    foreach (var field in unitCtx.unitField())
                    {
                        unit.Fields[field.IDENT().GetText()] = field.tupleLiteral().GetText();
                    }

                    body.Units.Add(unit);
                }

                continue;
            }

            if (member.metricsValueBlock() is not null)
            {
                body = new PartCatalogBody
                {
                    Fields = body.Fields,
                    Options = body.Options,
                    Pins = body.Pins,
                    Units = body.Units,
                    Metrics = BuildMetricsBlock(member.metricsValueBlock()),
                };
                continue;
            }

            body.Fields[member.idPart().GetText()] = member.entryValue().GetText();
        }

        return body;
    }
}
