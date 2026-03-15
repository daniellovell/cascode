using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascode.Cli.Services;
using Cascode.Workspace;
using Spectre.Console;

namespace Cascode.Cli.Commands;

internal sealed partial class PdkCharacterizationCommandHandlersImpl
{
    public CommandResult PdkCharReadCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Output.WriteLine(
                "Usage: pdk char read <model> [--corner <name>] [--backend ngspice] [--head <n>] [--job <path>]"
            );
            return CommandResult.Success;
        }

        var query = args[0];
        var backend = "ngspice";
        var corner = "tt";
        var head = 24;
        string? jobOverride = null;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                backend = args[++i];
            }
            else if (
                arg.Equals("--corner", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                corner = args[++i];
            }
            else if (
                arg.Equals("--head", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(
                    args[++i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed
                )
            )
            {
                head = Math.Max(1, parsed);
            }
            else if (arg.Equals("--job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                jobOverride = PathUtils.NormalizePath(args[++i]);
            }
        }

        var dbPath = Path.Combine(
            WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
            "pdk.db"
        );
        if (!File.Exists(dbPath))
        {
            Output.WriteLine("No PDK database found. Run 'pdk scan' first.");
            return CommandResult.Failure;
        }

        string jobDir;
        if (!string.IsNullOrEmpty(jobOverride))
        {
            jobDir = jobOverride;
        }
        else
        {
            CharRunRecord? run = null;
            var deviceRuns = Cascode.Workspace.CharLutReader.GetRunsForDevice(
                dbPath,
                query,
                corner
            );
            if (deviceRuns.Count > 0)
            {
                run = deviceRuns[0];
            }

            if (run is null)
            {
                var modelRuns = Cascode.Workspace.CharLutReader.GetRunsForModel(
                    dbPath,
                    query,
                    corner
                );
                if (modelRuns.Count > 0)
                {
                    run = modelRuns[0];
                }
            }

            if (run is null)
            {
                Output.WriteLine($"No characterization recorded for '{query}'.");
                return CommandResult.Failure;
            }

            jobDir = run.JobDir;
        }

        if (!Directory.Exists(jobDir))
        {
            Output.WriteLine($"Job directory not found: {jobDir}");
            return CommandResult.Failure;
        }

        var derivedPath = Path.Combine(jobDir, "derived.csv");
        if (!File.Exists(derivedPath))
        {
            Output.WriteLine(
                $"Derived metrics not found at {derivedPath}. Run 'char export {jobDir}' first."
            );
            return CommandResult.Failure;
        }

        var (headers, samples) = Services.CharIoHelpers.LoadDerivedCsv(derivedPath);
        if (headers.Count == 0 || samples.Count == 0)
        {
            Output.WriteLine("Derived CSV did not contain numeric samples.");
            return CommandResult.Failure;
        }

        var (controlIdx, controlName) = Services.CharIoHelpers.FindColumn(headers, "vgs", "vsg");
        var (idIdx, _) = Services.CharIoHelpers.FindColumn(headers, "id");
        var (gmIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm");
        var (gmIdIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm_over_id");
        var (vthIdx, _) = Services.CharIoHelpers.FindColumn(headers, "vth");
        var (gmPerWIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm_per_w");
        var (idPerWIdx, _) = Services.CharIoHelpers.FindColumn(headers, "id_per_w");
        var (vstarIdx, _) = Services.CharIoHelpers.FindColumn(headers, "vstar");
        var (roIdx, _) = Services.CharIoHelpers.FindColumn(headers, "ro");
        var (gmRoIdx, _) = Services.CharIoHelpers.FindColumn(headers, "gm_ro");
        var (ftIdx, _) = Services.CharIoHelpers.FindColumn(headers, "ft");

        var preview = Math.Min(head, samples.Count);
        var displayHeaders = BuildDisplayHeaders(
            controlName,
            gmIdx,
            gmIdIdx,
            vthIdx,
            gmPerWIdx,
            idPerWIdx,
            vstarIdx,
            roIdx,
            gmRoIdx,
            ftIdx
        );
        var displayRows = BuildDisplayRows(
            samples,
            preview,
            controlIdx,
            idIdx,
            gmIdx,
            gmIdIdx,
            vthIdx,
            gmPerWIdx,
            idPerWIdx,
            vstarIdx,
            roIdx,
            gmRoIdx,
            ftIdx
        );
        var sparklines = BuildSparklines(
            samples,
            controlIdx,
            idIdx,
            gmIdIdx,
            vthIdx,
            gmPerWIdx,
            idPerWIdx,
            vstarIdx,
            roIdx,
            gmRoIdx,
            ftIdx
        );

        if (_isInteractive())
        {
            var rowsReadOnly = displayRows.Select(r => (IReadOnlyList<string>)r).ToList();
            var view = new CharReadViewState(
                query,
                $"{backend} / {corner}",
                displayHeaders,
                rowsReadOnly,
                sparklines,
                derivedPath
            );
            _state.ShowCharRead(view);
            Output.WriteLine($"Showing characterization for {query}");
            return CommandResult.Success;
        }

        var table = new Table().Border(TableBorder.SimpleHeavy);
        foreach (var header in displayHeaders)
        {
            table.AddColumn(header);
        }

        foreach (var row in displayRows)
        {
            table.AddRow(row.ToArray());
        }

        WriteRenderable(
            new Rule($"[bold]{query}[/] — {backend} / {corner}") { Justification = Justify.Left }
        );
        WriteRenderable(table);

        foreach (var sparkline in sparklines)
        {
            WriteRenderable(ShellRenderer.BuildSparkline(sparkline.Key, sparkline.Value));
            Output.WriteLine(string.Empty);
        }

        Output.WriteLine($"Derived source: {derivedPath}");
        return CommandResult.Success;
    }

    private static List<string> BuildDisplayHeaders(
        string controlName,
        int gmIdx,
        int gmIdIdx,
        int vthIdx,
        int gmPerWIdx,
        int idPerWIdx,
        int vstarIdx,
        int roIdx,
        int gmRoIdx,
        int ftIdx
    )
    {
        var displayHeaders = new List<string> { "#", controlName.ToUpperInvariant(), "Id" };
        if (gmIdx >= 0)
        {
            displayHeaders.Add("gm");
        }

        if (gmIdIdx >= 0)
        {
            displayHeaders.Add("gm/Id");
        }

        if (gmPerWIdx >= 0)
        {
            displayHeaders.Add("gm/W");
        }

        if (idPerWIdx >= 0)
        {
            displayHeaders.Add("Id/W");
        }

        if (vstarIdx >= 0)
        {
            displayHeaders.Add("Vov");
        }

        if (roIdx >= 0)
        {
            displayHeaders.Add("ro");
        }

        if (gmRoIdx >= 0)
        {
            displayHeaders.Add("gm·ro");
        }

        if (ftIdx >= 0)
        {
            displayHeaders.Add("fT");
        }

        if (vthIdx >= 0)
        {
            displayHeaders.Add("Vth");
        }

        return displayHeaders;
    }

    private static List<List<string>> BuildDisplayRows(
        IReadOnlyList<IReadOnlyList<double>> samples,
        int preview,
        int controlIdx,
        int idIdx,
        int gmIdx,
        int gmIdIdx,
        int vthIdx,
        int gmPerWIdx,
        int idPerWIdx,
        int vstarIdx,
        int roIdx,
        int gmRoIdx,
        int ftIdx
    )
    {
        var displayRows = new List<List<string>>();
        for (var i = 0; i < preview; i++)
        {
            var sample = samples[i];
            var row = new List<string>
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                Services.CharIoHelpers.FormatNumber(SampleSafe(sample, controlIdx)),
                Services.CharIoHelpers.FormatNumber(SampleSafe(sample, idIdx)),
            };
            if (gmIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, gmIdx)));
            }

            if (gmIdIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, gmIdIdx)));
            }

            if (gmPerWIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, gmPerWIdx)));
            }

            if (idPerWIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, idPerWIdx)));
            }

            if (vstarIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, vstarIdx)));
            }

            if (roIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, roIdx)));
            }

            if (gmRoIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, gmRoIdx)));
            }

            if (ftIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, ftIdx)));
            }

            if (vthIdx >= 0)
            {
                row.Add(Services.CharIoHelpers.FormatNumber(SampleSafe(sample, vthIdx)));
            }

            displayRows.Add(row);
        }

        return displayRows;
    }

    private static Dictionary<string, IReadOnlyList<double>> BuildSparklines(
        IReadOnlyList<IReadOnlyList<double>> samples,
        int controlIdx,
        int idIdx,
        int gmIdIdx,
        int vthIdx,
        int gmPerWIdx,
        int idPerWIdx,
        int vstarIdx,
        int roIdx,
        int gmRoIdx,
        int ftIdx
    )
    {
        var sparklines = new Dictionary<string, IReadOnlyList<double>>();
        List<double> ExtractColumn(int idx) => samples.Select(s => SampleSafe(s, idx)).ToList();

        if (gmIdIdx >= 0)
        {
            sparklines["gm/Id"] = ExtractColumn(gmIdIdx);
        }

        if (idIdx >= 0)
        {
            sparklines["Id"] = ExtractColumn(idIdx);
        }

        if (gmPerWIdx >= 0)
        {
            sparklines["gm/W"] = ExtractColumn(gmPerWIdx);
        }

        if (idPerWIdx >= 0)
        {
            sparklines["Id/W"] = ExtractColumn(idPerWIdx);
        }

        if (vstarIdx >= 0)
        {
            sparklines["Vov"] = ExtractColumn(vstarIdx);
        }

        if (roIdx >= 0)
        {
            sparklines["ro"] = ExtractColumn(roIdx);
        }

        if (gmRoIdx >= 0)
        {
            sparklines["gm·ro"] = ExtractColumn(gmRoIdx);
        }

        if (ftIdx >= 0)
        {
            sparklines["fT"] = ExtractColumn(ftIdx);
        }

        if (controlIdx >= 0 && vthIdx >= 0)
        {
            sparklines["Vov (VGS-Vth)"] = samples
                .Select(s => SampleSafe(s, controlIdx) - SampleSafe(s, vthIdx))
                .ToList();
        }

        return sparklines;
    }

    private static double SampleSafe(IReadOnlyList<double> data, int idx) =>
        idx >= 0 && idx < data.Count ? data[idx] : double.NaN;
}
