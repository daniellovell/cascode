using System;
using System.IO;
using System.Linq;

namespace Cascode.Language;

public static partial class CascodeWriter
{
    private static void WritePartDefinition(PartDefinition part, TextWriter writer)
    {
        var header = part.IsAbstract ? "abstract part " : "part ";
        header += part.Name;
        if (part.Parameters.Count > 0)
        {
            header +=
                "("
                + string.Join(
                    ", ",
                    part.Parameters.Select(p =>
                        p.Default is null
                            ? $"{p.Type} {p.Name}"
                            : $"{p.Type} {p.Name} = {FormatParamValue(p.Default)}"
                    )
                )
                + ")";
        }
        if (!string.IsNullOrEmpty(part.BasePart))
        {
            header += $" extends {part.BasePart}";
        }
        if (part.Implements.Count > 0)
        {
            header += $" implements {string.Join(", ", part.Implements)}";
        }

        writer.WriteLine($"{header} {{");
        foreach (var port in part.Ports)
        {
            writer.WriteLine($"  {port.Direction.ToCascodeString()} {port.Name} : {port.Type}");
        }
        foreach (var supply in part.Supplies)
        {
            writer.WriteLine($"  supply {supply}");
        }
        foreach (var ground in part.Grounds)
        {
            writer.WriteLine($"  ground {ground}");
        }
        if (part.ParamMappings.Count > 0)
        {
            writer.WriteLine("  params {");
            foreach (var mapping in part.ParamMappings)
            {
                writer.WriteLine($"    {mapping.Key} = {mapping.Value}");
            }
            writer.WriteLine("  }");
        }
        if (part.Corners.Count > 0)
        {
            writer.WriteLine("  corners {");
            foreach (var corner in part.Corners)
            {
                writer.WriteLine(
                    $"    {corner.Name} {{ {string.Join("  ", corner.Fields.Select(f => $"{f.Key} = {f.Value}"))} }}"
                );
            }
            writer.WriteLine("  }");
        }

        writer.WriteLine("  catalog {");
        if (part.Catalog.Defaults is not null)
        {
            writer.WriteLine("    defaults {");
            WriteCatalogBody(part.Catalog.Defaults, writer, "      ");
            writer.WriteLine("    }");
        }
        foreach (var entry in part.Catalog.Entries)
        {
            writer.WriteLine($"    entry {entry.Name} {{");
            WriteCatalogBody(entry.Body, writer, "      ");
            writer.WriteLine("    }");
        }
        foreach (var variant in part.Catalog.Variants)
        {
            writer.WriteLine($"    variant {variant.Name} {{");
            foreach (var option in variant.Options)
            {
                writer.WriteLine($"      {option.Name} {{");
                WriteCatalogBody(option.Body, writer, "        ");
                foreach (var exclude in option.Excludes)
                {
                    writer.WriteLine($"        exclude {exclude.Axis}={exclude.Value}");
                }
                writer.WriteLine("      }");
            }
            writer.WriteLine("    }");
        }
        writer.WriteLine("  }");
        writer.WriteLine("}");
    }

    private static void WriteCatalogBody(PartCatalogBody body, TextWriter writer, string indent)
    {
        foreach (var field in body.Fields)
        {
            writer.WriteLine($"{indent}{field.Key} = {field.Value}");
        }
        if (body.Pins.Count > 0)
        {
            writer.WriteLine($"{indent}pins {{");
            foreach (var pin in body.Pins)
            {
                writer.WriteLine($"{indent}  {pin.Pad} = {pin.Target}");
            }
            writer.WriteLine($"{indent}}}");
        }
        if (body.Units.Count > 0)
        {
            writer.WriteLine($"{indent}units {{");
            foreach (var unit in body.Units)
            {
                writer.WriteLine(
                    $"{indent}  {unit.Name} {{ {string.Join(" ", unit.Fields.Select(f => $"{f.Key} = {f.Value}"))} }}"
                );
            }
            writer.WriteLine($"{indent}}}");
        }
        if (body.Metrics is not null)
        {
            WriteMetricsBlock(body.Metrics, writer, indent);
        }
        foreach (var option in body.Options)
        {
            writer.WriteLine(
                $"{indent}option {{ {string.Join(" ", option.Fields.Select(f => $"{f.Key} = {f.Value}"))} }}"
            );
        }
    }

    private static void WriteMetricsBlock(
        MetricsBlock metrics,
        TextWriter writer,
        string indent,
        bool declarationsOnly = false
    )
    {
        writer.WriteLine($"{indent}metrics {{");
        if (declarationsOnly)
        {
            foreach (var declaration in metrics.Declarations)
            {
                var qualifiers =
                    declaration.RequiredQualifiers.Count == 0
                        ? string.Empty
                        : $" {{ {string.Join(", ", declaration.RequiredQualifiers)} }}";
                writer.WriteLine($"{indent}  {declaration.Name} : {declaration.Unit}{qualifiers}");
            }
        }
        else
        {
            foreach (var group in metrics.Assignments.Where(a => a.Corner is null))
            {
                var qualifier = string.IsNullOrEmpty(group.Qualifier)
                    ? string.Empty
                    : $" {group.Qualifier}";
                writer.WriteLine($"{indent}  {group.Name}{qualifier} = {group.Value}");
            }
            foreach (
                var corner in metrics
                    .Assignments.Where(a => a.Corner is not null)
                    .GroupBy(a => a.Corner)
            )
            {
                writer.WriteLine($"{indent}  at {corner.Key} {{");
                foreach (var assignment in corner)
                {
                    var qualifier = string.IsNullOrEmpty(assignment.Qualifier)
                        ? string.Empty
                        : $" {assignment.Qualifier}";
                    writer.WriteLine(
                        $"{indent}    {assignment.Name}{qualifier} = {assignment.Value}"
                    );
                }
                writer.WriteLine($"{indent}  }}");
            }
        }
        writer.WriteLine($"{indent}}}");
    }
}
