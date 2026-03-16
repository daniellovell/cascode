using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascode.Cli.Output;
using Cascode.Workspace;
using Spectre.Console;

namespace Cascode.Cli.Commands;

internal sealed partial class PdkCharacterizationCommandHandlersImpl
    : PdkCommandHandlersSupport,
        IPdkCharacterizationCommandHandlers
{
    internal static readonly string[] SimulatorBackends = new[] { "spectre", "ngspice" };
    internal static readonly string[] InfraFilterOptions = new[]
    {
        "all",
        "infra-only",
        "exclude-infra",
    };

    public PdkCharacterizationCommandHandlersImpl(
        ShellState state,
        Func<bool> isInteractive,
        CliOutputProvider outputProvider
    )
        : base(state, isInteractive, outputProvider) { }

    public CommandResult ShowPdkCharUsage(string[] args)
    {
        Output.WriteLine("=== PDK Characterization ===");
        Output.WriteLine(string.Empty);
        Output.WriteLine("Goal: Build device LUTs (gm/Id, etc.) for synthesis and sizing.");
        Output.WriteLine("Outputs: Netlists, results.csv, derived.csv stored in workspace cache.");
        Output.WriteLine(string.Empty);
        Output.WriteLine("Commands:");
        Output.WriteLine(
            "  pdk char config              Interactive form to set defaults (backend/corner/filters/jobs)."
        );
        Output.WriteLine("  pdk char config --show       Show the saved defaults.");
        Output.WriteLine(
            "  pdk char run                 Run a batch using saved defaults (flags override). Shows progress."
        );
        Output.WriteLine(
            "  pdk char status              Show characterization coverage matrix (devices × corners)."
        );
        Output.WriteLine("  pdk char read <device/model> Preview latest LUT — table + sparklines.");
        Output.WriteLine(string.Empty);
        Output.WriteLine("Common Flags:");
        Output.WriteLine("  --backend ngspice           Pick simulator (ngspice only for now).");
        Output.WriteLine("  --corner <name>              Model section/corner, e.g., tt/ff/ss.");
        Output.WriteLine(
            "  --limit <n>                  Cap how many devices to process (0 = all)."
        );
        Output.WriteLine("  --jobs <n>                   Ignored (legacy flag).");
        Output.WriteLine("  --class nmos,pmos            Filter device classes.");
        Output.WriteLine("  --name-contains <csv>        Only names containing any token.");
        Output.WriteLine("  --name-excludes <csv>        Skip names containing any token.");
        Output.WriteLine("  --vt <csv>                   Only VT flavors (e.g., LVT,HVT).");
        Output.WriteLine("  --vdd <csv>                  Only VDD tags (e.g., 1.8V,01v8).");
        Output.WriteLine(
            "  --infra / --no-infra         Include only infra devices, or exclude them."
        );
        Output.WriteLine(string.Empty);
        Output.WriteLine("Examples:");
        Output.WriteLine("  pdk char config");
        Output.WriteLine(
            "    → Open the defaults form; save corner/backend/filters/jobs to workspace."
        );
        Output.WriteLine("  pdk char run");
        Output.WriteLine(
            "    → Start a batch with saved defaults; shows a live progress bar chart."
        );
        Output.WriteLine("  pdk char run --class nmos --limit 5 --name-excludes esd,io --vt LVT");
        Output.WriteLine("    → Quick LVT-only NMOS subset; skips ESD/IO variants.");
        Output.WriteLine("  pdk char run --corner tt --backend ngspice");
        Output.WriteLine("    → Run characterization using ngspice.");
        Output.WriteLine("  pdk char read sky130_fd_pr__nfet_01v8");
        Output.WriteLine("    → Show table and gm/Id sparkline for the latest run of that device.");
        Output.WriteLine(string.Empty);
        Output.WriteLine("Notes:");
        Output.WriteLine("- Requires 'pdk scan' and 'pdk emit primitives' before running.");
        Output.WriteLine(
            "- Results live under ~/.cascode/workspaces/<id>/char/<backend>/<corner>/<device>/<ts>/"
        );
        return CommandResult.Success;
    }

    public CommandResult PdkCharConfigCommand(string[] args)
    {
        var cfgPath = WorkspaceState.GetCharConfigPath(_state.WorkspaceRoot);
        var cfg = CharRunConfig.Load(cfgPath);

        if (args.Length > 0 && args[0].Equals("--show", StringComparison.OrdinalIgnoreCase))
        {
            DumpConfig(cfg);
            return CommandResult.Success;
        }

        cfg.Backend = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select backend")
                .AddChoices(SimulatorBackends)
                .HighlightStyle(new Style(Color.Cyan1))
                .MoreChoicesText("[grey](Move up/down to reveal more)[/]")
                .AddChoices(
                    string.IsNullOrWhiteSpace(cfg.Backend)
                        ? Array.Empty<string>()
                        : new[] { cfg.Backend }
                )
        );

        cfg.Corner = AnsiConsole.Ask<string>(
            "Corner (e.g., tt/ff/ss):",
            string.IsNullOrWhiteSpace(cfg.Corner) ? "tt" : cfg.Corner
        );

        var classPrompt = new MultiSelectionPrompt<string>()
            .Title("Device classes")
            .NotRequired()
            .InstructionsText("[grey](Space to toggle, Enter to accept)[/]")
            .AddChoices("nmos", "pmos");
        foreach (var selectedClass in cfg.Classes ?? new())
        {
            classPrompt.Select(selectedClass);
        }

        cfg.Classes = AnsiConsole.Prompt(classPrompt).ToList();
        cfg.Limit = AnsiConsole.Ask<int>("Limit (0 = all):", Math.Max(0, cfg.Limit));
        cfg.Jobs = AnsiConsole.Ask<int>(
            "Jobs (parallel generation) [1..8]:",
            Math.Clamp(cfg.Jobs <= 0 ? 1 : cfg.Jobs, 1, 8)
        );

        var nameContains = AnsiConsole.Ask<string>(
            "Name contains (comma-separated, blank=none):",
            string.Join(',', cfg.NameContains ?? new())
        );
        cfg.NameContains = string.IsNullOrWhiteSpace(nameContains)
            ? new List<string>()
            : SplitCsv(nameContains);

        var nameExcludes = AnsiConsole.Ask<string>(
            "Name excludes (comma-separated, blank=esd):",
            string.Join(',', cfg.NameExcludes ?? new())
        );
        cfg.NameExcludes = string.IsNullOrWhiteSpace(nameExcludes)
            ? new List<string> { "esd" }
            : SplitCsv(nameExcludes);

        var vtPrompt = new MultiSelectionPrompt<string>()
            .Title("VT flavors (optional)")
            .NotRequired()
            .InstructionsText("[grey](Space to toggle, Enter to accept)[/]")
            .AddChoices("ULVT", "LLVT", "SLVT", "LVT", "RVT", "SVT", "NVT", "HVT", "MVT");
        foreach (var vt in cfg.Vt ?? new())
        {
            vtPrompt.Select(vt.ToUpperInvariant());
        }

        cfg.Vt = AnsiConsole.Prompt(vtPrompt).ToList();

        var vddInput = AnsiConsole.Ask<string>(
            "VDD filter (comma-separated, blank=all):",
            string.Join(',', cfg.Vdd ?? new())
        );
        cfg.Vdd = string.IsNullOrWhiteSpace(vddInput) ? new List<string>() : SplitCsv(vddInput);

        var infraChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Infra devices")
                .AddChoices(InfraFilterOptions)
                .HighlightStyle(new Style(Color.Cyan1))
                .MoreChoicesText("[grey](Use arrows to change selection)[/]")
        );
        cfg.Infra = infraChoice switch
        {
            "infra-only" => true,
            "exclude-infra" => false,
            _ => null,
        };

        cfg.OutRoot = AnsiConsole.Ask<string>(
            "Output root (blank = default)",
            cfg.OutRoot ?? string.Empty
        );
        if (string.IsNullOrWhiteSpace(cfg.OutRoot))
        {
            cfg.OutRoot = null;
        }

        cfg.Save(cfgPath);
        Output.WriteLine($"Saved characterization config → {cfgPath}");
        Output.WriteLine("Run 'pdk char run' to start a batch using this configuration.");
        return CommandResult.Success;

        void DumpConfig(CharRunConfig show)
        {
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Key").AddColumn("Value");
            table.AddRow("Backend", show.Backend);
            table.AddRow("Corner", show.Corner);
            table.AddRow("Limit", show.Limit.ToString(CultureInfo.InvariantCulture));
            table.AddRow("Jobs", show.Jobs.ToString(CultureInfo.InvariantCulture));
            table.AddRow("Classes", string.Join(',', show.Classes ?? new()));
            table.AddRow("NameContains", string.Join(',', show.NameContains ?? new()));
            table.AddRow("NameExcludes", string.Join(',', show.NameExcludes ?? new()));
            table.AddRow("VT", string.Join(',', show.Vt ?? new()));
            table.AddRow("VDD", string.Join(',', show.Vdd ?? new()));
            table.AddRow(
                "Infra",
                show.Infra.HasValue ? (show.Infra.Value ? "infra-only" : "exclude-infra") : "all"
            );
            table.AddRow("OutRoot", show.OutRoot ?? "(default)");
            WriteRenderable(table);
        }
    }

    public CommandResult PdkCharStatusCommand(string[] args)
    {
        var dbPath = Path.Combine(
            WorkspaceState.GetWorkspaceFolder(_state.WorkspaceRoot),
            "pdk.db"
        );
        if (!File.Exists(dbPath))
        {
            Output.WriteLine("PDK database not found. Run 'pdk scan' first.");
            return CommandResult.Failure;
        }

        try
        {
            var coverage = Cascode.Workspace.CharLutReader.GetDeviceCoverage(dbPath);
            if (coverage.Devices.Count == 0)
            {
                Output.WriteLine("No devices discovered. Run 'pdk scan' first.");
                return CommandResult.Success;
            }

            if (coverage.TotalRuns == 0)
            {
                Output.WriteLine(
                    "No characterization runs found. Use 'pdk char run' to characterize devices."
                );
                return CommandResult.Success;
            }

            var classes = coverage
                .DeviceClasses.Values.Distinct()
                .OrderBy(c => c.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            var totalsByClass = coverage
                .DeviceClasses.GroupBy(kvp => kvp.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold]Device Characterization Coverage[/]");
            table.AddColumn("Class");
            table.AddColumn("Total");
            foreach (var corner in coverage.Corners)
            {
                table.AddColumn(new TableColumn(corner.ToUpperInvariant()).Centered());
            }

            foreach (var deviceClass in classes)
            {
                var total = totalsByClass.TryGetValue(deviceClass, out var count) ? count : 0;
                var row = new List<string>
                {
                    deviceClass.ToString(),
                    total.ToString(CultureInfo.InvariantCulture),
                };
                foreach (var corner in coverage.Corners)
                {
                    var covered = coverage.Devices.Count(d =>
                        coverage.GetDeviceClass(d) == deviceClass && coverage.HasRun(d, corner)
                    );
                    row.Add($"{covered}/{total}");
                }

                table.AddRow(row.ToArray());
            }

            WriteRenderable(table);

            var totalPossible = coverage.Devices.Count * Math.Max(1, coverage.Corners.Count);
            var percentage = totalPossible > 0 ? (coverage.TotalRuns * 100.0 / totalPossible) : 0.0;
            Output.WriteLine(
                $"Device coverage: {coverage.TotalRuns}/{totalPossible} ({percentage:F1}%)"
            );
            return CommandResult.Success;
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to load characterization status: {ex.Message}");
            return CommandResult.Failure;
        }
    }

    private static DeviceFilterOptions BuildDeviceFilterOptions(CharRunConfig cfg)
    {
        var vddNormalized = new List<string>();
        foreach (var token in cfg.Vdd ?? new List<string>())
        {
            if (DeviceFilterEvaluator.TryNormalizeVddFilter(token, out var normalized))
            {
                vddNormalized.Add(normalized);
            }
            else
            {
                vddNormalized.Add(token.ToLowerInvariant());
            }
        }

        return new DeviceFilterOptions(
            cfg.Classes ?? new List<string>(),
            cfg.Vt ?? new List<string>(),
            vddNormalized,
            cfg.Infra,
            matched: null,
            cfg.NameContains ?? new List<string>(),
            cfg.NameExcludes ?? new List<string>()
        );
    }
}
