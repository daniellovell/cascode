using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cascode.Language;

public static partial class CascodeWriter
{
    private static void WritePartDefinition(PartDefinition part, TextWriter writer)
    {
        var signatureParts = new List<string>();
        foreach (var size in part.Sizes.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            signatureParts.Add($"size {size.Name}");
        }
        foreach (var parameter in part.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var defaultPart = parameter.Default is null
                ? string.Empty
                : $" = {FormatParamValue(parameter.Default)}";
            signatureParts.Add($"{parameter.Type} {parameter.Name}{defaultPart}");
        }

        var signature =
            signatureParts.Count == 0 ? string.Empty : $"({string.Join(", ", signatureParts)})";
        var abstractPrefix = part.IsAbstract ? "abstract " : string.Empty;
        var extendsClause = string.IsNullOrWhiteSpace(part.BasePart)
            ? string.Empty
            : $" extends {part.BasePart}{FormatPartBaseArguments(part.BaseArguments)}";
        var implementsClause =
            part.Implements.Count == 0
                ? string.Empty
                : $" implements {string.Join(", ", part.Implements)}";

        writer.WriteLine(
            $"{abstractPrefix}part {part.Name}{signature}{extendsClause}{implementsClause} {{"
        );

        foreach (var supply in part.Supplies.OrderBy(s => s, StringComparer.Ordinal))
        {
            writer.WriteLine($"  supply {supply}");
        }
        foreach (var ground in part.Grounds.OrderBy(g => g, StringComparer.Ordinal))
        {
            writer.WriteLine($"  ground {ground}");
        }
        foreach (var port in part.Ports.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            writer.WriteLine($"  {port.Direction.ToCascodeString()} {port.Name} : {port.Type}");
        }

        if (part.ParameterMappings.Count > 0)
        {
            writer.WriteLine("  params {");
            foreach (
                var mapping in part.ParameterMappings.OrderBy(m => m.Key, StringComparer.Ordinal)
            )
            {
                writer.WriteLine($"    {mapping.Key} = {mapping.Value}");
            }
            writer.WriteLine("  }");
        }

        if (part.Corners.Count > 0)
        {
            writer.WriteLine("  corners {");
            foreach (var corner in part.Corners.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                writer.WriteLine($"    {corner.Name} {{");
                foreach (var field in corner.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    writer.WriteLine($"      {field.Key} = {field.Value}");
                }
                writer.WriteLine("    }");
            }
            writer.WriteLine("  }");
        }

        if (part.Metrics.Count > 0)
        {
            WriteMetricAssignments(part.Metrics, writer, "  ");
        }

        writer.WriteLine("  catalog {");
        WritePartCatalog(part.Catalog, writer);
        writer.WriteLine("  }");
        writer.WriteLine("}");
    }

    private static void WritePartCatalog(PartCatalog catalog, TextWriter writer)
    {
        if (HasEntryData(catalog.Defaults))
        {
            writer.WriteLine("    defaults {");
            WriteEntryData(catalog.Defaults, writer, "      ");
            writer.WriteLine("    }");
        }

        foreach (var entry in catalog.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            writer.WriteLine($"    entry {entry.Name} {{");
            WriteEntryData(entry.Data, writer, "      ");
            writer.WriteLine("    }");
        }

        foreach (var axis in catalog.Variants.OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            writer.WriteLine($"    variant {axis.Name} {{");
            foreach (var option in axis.Options.OrderBy(o => o.Name, StringComparer.Ordinal))
            {
                writer.WriteLine($"      {option.Name} {{");
                WriteEntryData(option.Data, writer, "        ");
                foreach (
                    var exclusion in option.Exclusions.OrderBy(e => e.Axis, StringComparer.Ordinal)
                )
                {
                    writer.WriteLine($"        exclude {exclusion.Axis}={exclusion.Option}");
                }
                writer.WriteLine("      }");
            }
            writer.WriteLine("    }");
        }
    }

    private static bool HasEntryData(PartEntryData data)
    {
        return data.Fields.Count > 0
            || data.Options.Count > 0
            || data.Pins.Count > 0
            || data.Units.Count > 0
            || data.Metrics.Count > 0
            || data.MechanicalFields.Count > 0;
    }

    private static void WriteEntryData(PartEntryData data, TextWriter writer, string indent)
    {
        foreach (var field in data.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            writer.WriteLine($"{indent}{field.Key} = {field.Value}");
        }

        foreach (var option in data.Options)
        {
            var fieldText = string.Join(
                " ",
                option
                    .Fields.OrderBy(f => f.Key, StringComparer.Ordinal)
                    .Select(f => $"{f.Key} = {f.Value}")
            );
            writer.WriteLine($"{indent}option {{ {fieldText} }}");
        }

        if (data.Pins.Count > 0)
        {
            writer.WriteLine($"{indent}pins {{");
            foreach (var pinMap in data.Pins.OrderBy(p => p.Terminal, StringComparer.Ordinal))
            {
                var pads = pinMap.IsPadRange ? pinMap.Pads[0] : string.Join(", ", pinMap.Pads);
                writer.WriteLine($"{indent}  {pinMap.Terminal} = {pads}");
            }
            writer.WriteLine($"{indent}}}");
        }

        if (data.Units.Count > 0)
        {
            writer.WriteLine($"{indent}units {{");
            foreach (var unit in data.Units.OrderBy(u => u.Name, StringComparer.Ordinal))
            {
                writer.WriteLine($"{indent}  {unit.Name} {{");
                foreach (var field in unit.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    writer.WriteLine($"{indent}    {field.Key} = {field.Value}");
                }
                writer.WriteLine($"{indent}  }}");
            }
            writer.WriteLine($"{indent}}}");
        }

        if (data.Metrics.Count > 0)
        {
            WriteMetricAssignments(data.Metrics, writer, indent);
        }

        if (data.MechanicalFields.Count > 0)
        {
            writer.WriteLine($"{indent}mechanical {{");
            foreach (var field in data.MechanicalFields.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                writer.WriteLine($"{indent}  {field.Key} = {field.Value}");
            }
            writer.WriteLine($"{indent}}}");
        }
    }

    private static string FormatPartBaseArguments(Dictionary<string, ParamValue> args)
    {
        if (args.Count == 0)
        {
            return string.Empty;
        }

        var entries = args.OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => $"{a.Key}={FormatParamValue(a.Value)}");
        return $"({string.Join(", ", entries)})";
    }

    private static void WriteMetricAssignments(
        IReadOnlyList<MetricAssignment> assignments,
        TextWriter writer,
        string indent
    )
    {
        writer.WriteLine($"{indent}metrics {{");
        foreach (
            var assignment in assignments
                .Where(m => string.IsNullOrWhiteSpace(m.Corner))
                .OrderBy(m => m.Name, StringComparer.Ordinal)
        )
        {
            writer.WriteLine($"{indent}  {FormatMetricAssignment(assignment)}");
        }

        foreach (
            var cornerGroup in assignments
                .Where(m => !string.IsNullOrWhiteSpace(m.Corner))
                .GroupBy(m => m.Corner!, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            writer.WriteLine($"{indent}  at {cornerGroup.Key} {{");
            foreach (var assignment in cornerGroup.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                writer.WriteLine($"{indent}    {FormatMetricAssignment(assignment)}");
            }
            writer.WriteLine($"{indent}  }}");
        }
        writer.WriteLine($"{indent}}}");
    }

    private static string FormatMetricAssignment(MetricAssignment assignment)
    {
        var qualifier = assignment.Qualifier is null
            ? string.Empty
            : $" {assignment.Qualifier.Value.ToCascodeString()}";
        var value = assignment.Value.Source is null
            ? assignment.Value.Scalar ?? string.Empty
            : assignment.Value.Source.Value;
        return $"{assignment.Name}{qualifier} = {value}";
    }

    private static string FormatSelectionValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            return value;
        }

        return value.All(c => char.IsLetterOrDigit(c) || c == '_') ? value : $"\"{value}\"";
    }
}
