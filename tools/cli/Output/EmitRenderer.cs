using System;
using System.IO;
using System.Linq;
using Cascode.Language;
using Spectre.Console;

namespace Cascode.Cli.Output;

internal static class EmitRenderer
{
    public static void Render(ValidatedEmitResult result, ICliOutput output)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(output);

        if (output.Mode == CliOutputMode.Spectre && output.Out is not null)
        {
            RenderSpectre(result, output);
            return;
        }

        RenderPlain(result, output);
    }

    private static void RenderPlain(ValidatedEmitResult result, ICliOutput output)
    {
        foreach (var warning in result.Validation.GetWarnings())
        {
            output.Warning(warning.ToString());
        }

        foreach (var path in result.Emit.DesignPaths)
        {
            output.WriteLine($"Design netlist: {path}");
        }

        foreach (var path in result.Emit.TestbenchPaths)
        {
            output.WriteLine($"Testbench: {path}");
        }

        output.Success(
            $"Emitted {result.Emit.DesignPaths.Count} design(s) and {result.Emit.TestbenchPaths.Count} testbench(es)."
        );
    }

    private static void RenderSpectre(ValidatedEmitResult result, ICliOutput output)
    {
        foreach (var warning in result.Validation.GetWarnings())
        {
            output.Warning(warning.ToString());
        }

        var console = output.Out!;

        console.Write(
            new Rule("[grey]Emit[/]") { Style = Style.Parse("grey"), Justification = Justify.Left }
        );

        var files = new Table().Border(TableBorder.Simple);
        files.AddColumn(new TableColumn("[grey]Kind[/]").LeftAligned().NoWrap().Width(10));
        files.AddColumn(new TableColumn("[grey]Path[/]").LeftAligned());

        foreach (var p in result.Emit.DesignPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            files.AddRow("[cyan]design[/]", Markup.Escape(Path.GetFullPath(p)));
        }

        foreach (
            var p in result.Emit.TestbenchPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        )
        {
            files.AddRow("[cyan]testbench[/]", Markup.Escape(Path.GetFullPath(p)));
        }

        console.Write(files);

        output.Success(
            $"Emitted {result.Emit.DesignPaths.Count} design(s) and {result.Emit.TestbenchPaths.Count} testbench(es)."
        );
    }
}
