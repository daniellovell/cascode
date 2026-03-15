using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.Cli.Services;
using Cascode.Workspace;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Commands;

internal sealed partial class PdkCharacterizationCommandHandlersImpl
{
    public CommandResult PdkCharRunCommand(string[] args)
    {
        var cfgPath = WorkspaceState.GetCharConfigPath(_state.WorkspaceRoot);
        var cfg = CharRunConfig.Load(cfgPath);

        var backend = cfg.Backend ?? "ngspice";
        var corner = cfg.Corner ?? "tt";
        var limit = cfg.Limit;
        var outRoot = cfg.OutRoot ?? WorkspaceState.GetCharacterizationFolder(_state.WorkspaceRoot);

        for (var i = 0; i < args.Length; i++)
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
                arg.Equals("--limit", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(
                    args[++i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedLimit
                )
            )
            {
                limit = Math.Max(0, parsedLimit);
            }
            else if (
                arg.Equals("--jobs", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(
                    args[++i],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedJobs
                )
            )
            {
                _ = parsedJobs;
            }
            else if (
                arg.Equals("--name-contains", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                cfg.NameContains = SplitCsv(args[++i]);
            }
            else if (
                arg.Equals("--name-excludes", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                cfg.NameExcludes = SplitCsv(args[++i]);
            }
            else if (arg.Equals("--vt", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cfg.Vt = SplitCsv(args[++i]).Select(s => s.ToUpperInvariant()).ToList();
            }
            else if (
                arg.Equals("--class", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                cfg.Classes = SplitCsv(args[++i]).Select(s => s.ToLowerInvariant()).ToList();
            }
            else if (arg.Equals("--vdd", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                cfg.Vdd = SplitCsv(args[++i]);
            }
            else if (arg.Equals("--infra", StringComparison.OrdinalIgnoreCase))
            {
                cfg.Infra = true;
            }
            else if (arg.Equals("--no-infra", StringComparison.OrdinalIgnoreCase))
            {
                cfg.Infra = false;
            }
            else if (
                arg.Equals("--out-root", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
            )
            {
                outRoot = args[++i];
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

        if (!backend.Equals("ngspice", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine(
                $"[warn] Backend '{backend}' is not supported by the declarative characterization flow yet; using ngspice."
            );
            backend = "ngspice";
        }

        var pdkNameSource = _state.PdkRoot ?? _state.WorkspaceRoot;
        var pdkName = Path.GetFileName(Path.GetFullPath(pdkNameSource));
        if (string.IsNullOrWhiteSpace(pdkName))
        {
            Output.WriteLine("Unable to determine active PDK name.");
            return CommandResult.Failure;
        }

        if (
            !PdkPrimitiveLibraryLayout.TryValidateLibrary(
                Directory.GetCurrentDirectory(),
                pdkName,
                out _,
                out var libraryError
            )
        )
        {
            Output.WriteLine(libraryError);
            return CommandResult.Failure;
        }

        var filters = BuildDeviceFilterOptions(cfg);
        var options = DeviceCharPlannerOptions.Create(backend, corner, limit, filters);

        IReadOnlyList<DeviceCharPlan> plans;
        try
        {
            plans = Cascode.Workspace.DeviceCharPlanner.Plan(dbPath, options);
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to build characterization plan: {ex.Message}");
            return CommandResult.Failure;
        }

        if (plans.Count == 0)
        {
            var allDevices = Cascode.Workspace.PdkDatabaseReader.LoadDevices(dbPath);
            if (allDevices.Count == 0)
            {
                Output.WriteLine("No devices discovered. Run 'pdk scan' first.");
                return CommandResult.Failure;
            }

            HashSet<string>? matchedKeys = null;
            if (filters.Matched.HasValue)
            {
                matchedKeys = Cascode.Workspace.PdkDatabaseReader.LoadMatchedDeviceKeys(dbPath);
            }

            var filteredDevices = allDevices
                .Where(d => DeviceFilterEvaluator.Matches(d, filters, matchedKeys))
                .ToList();
            if (filteredDevices.Count == 0)
            {
                Output.WriteLine("No devices matched the selected filters.");
                return CommandResult.Failure;
            }

            var bestMatches = Cascode.Workspace.PdkDatabaseReader.LoadBestMatchByDevice(dbPath);
            var matchedFiltered = filteredDevices
                .Where(d => bestMatches.ContainsKey(d.CanonicalName))
                .ToList();
            if (matchedFiltered.Count == 0)
            {
                Output.WriteLine(
                    $"Filtered devices: {filteredDevices.Count}. None have matched models; rerun 'pdk scan' or adjust matching."
                );
                return CommandResult.Failure;
            }

            Output.WriteLine("No devices matched the selection.");
            return CommandResult.Success;
        }

        var modelCount = plans
            .Select(p => p.ModelName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        _state.StartCharJob(plans.Count, backend, corner);
        Output.WriteLine(
            $"Starting characterization batch → backend={backend}, corner={corner}, devices={plans.Count}, models={modelCount}"
        );

        bool RunBatch()
        {
            var oldCorner = Environment.GetEnvironmentVariable("CASCODE_PDK_CORNER");
            Environment.SetEnvironmentVariable("CASCODE_PDK_CORNER", corner);

            ILoggerFactory? localFactory = null;
            var loggerFactory =
                _state.LoggerFactory
                ?? (
                    localFactory = LoggerFactory.Create(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Warning);
                        builder.AddSimpleConsole(o =>
                        {
                            o.SingleLine = true;
                        });
                    })
                );

            var ran = 0;
            var exported = 0;
            var skipped = 0;
            var completed = false;
            var fatalFailure = false;

            try
            {
                foreach (var plan in plans)
                {
                    _state.UpdateCharProgress(plan.DeviceName);
                    var jobDir = Path.Combine(
                        outRoot,
                        backend.ToLowerInvariant(),
                        string.IsNullOrWhiteSpace(corner) ? "default" : corner,
                        Sanitize(plan.DeviceName),
                        DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture)
                    );
                    Directory.CreateDirectory(jobDir);

                    var gen = CharGenService.GenerateAndRun(
                        Directory.GetCurrentDirectory(),
                        _state.PdkRoot ?? _state.WorkspaceRoot,
                        new CharGenService.CharGenArgs(
                            ModelQuery: plan.ModelName,
                            OutputDir: jobDir,
                            Corner: corner,
                            Backend: backend,
                            DeviceName: plan.DeviceName,
                            WidthM: plan.Width,
                            LengthM: plan.Length,
                            Mult: 1,
                            Nf: plan.Nf,
                            VdsV: plan.Vds,
                            VsbV: plan.Vsb,
                            VgsStartV: 0.0,
                            VgsStopV: plan.VgsStop,
                            VgsStepV: 0.01
                        ),
                        loggerFactory,
                        Output
                    );
                    if (!gen.Succeeded)
                    {
                        if (
                            gen.Message.Contains(
                                "No parametric primitive is available",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            Output.WriteLine($"[error] {plan.DeviceName}: {gen.Message}");
                            fatalFailure = true;
                            break;
                        }

                        Output.WriteLine($"[warn] {plan.DeviceName}: {gen.Message}");
                        skipped++;
                        _state.UpdateCharProgress(plan.DeviceName, skippedDelta: 1);
                        continue;
                    }

                    ran++;
                    _state.UpdateCharProgress(plan.DeviceName, generatedDelta: 1, ranDelta: 1);

                    var exportOk = Services.CharExportService.ExportDerived(
                        jobDir,
                        metricFilter: null,
                        out _,
                        out var exportMsg
                    );
                    Output.WriteLine(exportMsg);
                    if (!exportOk)
                    {
                        skipped++;
                        _state.UpdateCharProgress(plan.DeviceName, skippedDelta: 1);
                        continue;
                    }

                    exported++;
                    _state.UpdateCharProgress(plan.DeviceName, exportedDelta: 1);

                    try
                    {
                        Cascode.Workspace.CharLutWriter.ImportFromJobDir(dbPath, jobDir);
                        Output.WriteLine($"LUT stored in database for {plan.DeviceName}.");
                    }
                    catch (Exception lutEx)
                    {
                        Output.WriteLine($"[warn] Failed to store LUT: {lutEx.Message}");
                    }
                }

                Output.WriteLine(
                    $"Characterization batch complete: ran {ran}, exported {exported}, skipped {skipped}."
                );
                completed = !fatalFailure;
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Characterization batch failed: {ex.Message}");
            }
            finally
            {
                if (!completed)
                {
                    Output.WriteLine("Characterization batch terminated early.");
                }

                _state.CompleteCharJob();
                Environment.SetEnvironmentVariable("CASCODE_PDK_CORNER", oldCorner);
                localFactory?.Dispose();
            }

            return completed;
        }

        if (_isInteractive())
        {
            Task.Run(RunBatch);
            Output.WriteLine(
                "Batch running in background; progress will update while the CLI remains responsive."
            );
            return CommandResult.Success;
        }

        var batchSucceeded = RunBatch();
        return batchSucceeded ? CommandResult.Success : CommandResult.Failure;
    }
}
