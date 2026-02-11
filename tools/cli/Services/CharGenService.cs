using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cascode.Cli;
using Cascode.Cli.Output;
using Cascode.Language;
using Cascode.Workspace;
using Microsoft.Extensions.Logging;

namespace Cascode.Cli.Services;

internal static class CharGenService
{
    public sealed record CharGenArgs(
        string ModelQuery,
        string OutputDir,
        string Corner,
        string Backend,
        string? DeviceName,
        double WidthM,
        double LengthM,
        int Mult,
        int Nf,
        double VdsV,
        double VsbV,
        double VgsStartV,
        double VgsStopV,
        double VgsStepV,
        double TemperatureC = 27.0
    );

    public sealed record CharGenResult(bool Succeeded, string Message, string? JobDir = null);

    public static CharGenResult GenerateAndRun(
        string workspaceRoot,
        string? pdkRoot,
        CharGenArgs args,
        ILoggerFactory loggerFactory,
        ICliOutput output
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(output);

        var pdkWorkspaceRoot = string.IsNullOrWhiteSpace(pdkRoot) ? workspaceRoot : pdkRoot;
        var dbPath = Path.Combine(WorkspaceState.GetWorkspaceFolder(pdkWorkspaceRoot), "pdk.db");
        if (!File.Exists(dbPath))
        {
            return new CharGenResult(
                Succeeded: false,
                Message: "No PDK database found. Run 'pdk scan' first."
            );
        }

        var pdkName = ResolvePdkName(pdkRoot, workspaceRoot);
        if (
            !PdkPrimitiveLibraryLayout.TryValidateLibrary(
                workspaceRoot,
                pdkName,
                out _,
                out var libraryError
            )
        )
        {
            return new CharGenResult(Succeeded: false, Message: libraryError);
        }

        var models = PdkDatabaseReader.LoadModels(dbPath);
        if (models.Count == 0)
        {
            return new CharGenResult(
                Succeeded: false,
                Message: "No models found in pdk.db. Run 'pdk scan' first."
            );
        }

        var model = ResolveModel(models, args.ModelQuery);
        if (model is null)
        {
            return new CharGenResult(Succeeded: false, Message: "Model not found.");
        }

        if (model.DeviceClass is not (DeviceClass.Nmos or DeviceClass.Pmos))
        {
            return new CharGenResult(
                Succeeded: false,
                Message: $"Unsupported device class '{model.DeviceClass}'."
            );
        }

        var familyName = PdkPrimitiveNaming.PrimitiveFamilyNameFromModelName(model.Name);
        var familyRepresentative = ResolveFamilyRepresentativeModel(
            models,
            model.DeviceClass,
            familyName
        );
        if (familyRepresentative is null)
        {
            return new CharGenResult(
                Succeeded: false,
                Message: $"No parametric primitive is available for family '{familyName}' (matched model '{model.Name}'). Use a parametric family for characterization."
            );
        }

        var primitiveName = familyName;
        var circuitName = PdkPrimitiveNaming.SanitizeIdentifier("CharGmId_" + primitiveName);
        var deviceKind = model.DeviceClass == DeviceClass.Nmos ? "NMOS" : "PMOS";
        var isPmos = model.DeviceClass == DeviceClass.Pmos;
        var benchName = model.DeviceClass == DeviceClass.Nmos ? "GmIdNmos" : "GmIdPmos";
        var bindingAlias = "gm_id";
        var pdkLibraryNamespace = PdkPrimitiveLibraryLayout.GetLibraryNamespace(pdkName);

        Directory.CreateDirectory(args.OutputDir);

        var wrapperPath = Path.Combine(args.OutputDir, "char_job.cas");
        File.WriteAllText(
            wrapperPath,
            RenderWrapperCascode(
                args,
                circuitName,
                deviceKind,
                isPmos,
                benchName,
                bindingAlias,
                primitiveName,
                pdkLibraryNamespace
            )
        );

        var specPath = Path.Combine(args.OutputDir, "spec.json");
        File.WriteAllText(specPath, RenderSpecJson(args, familyRepresentative.Name));

        var runArgs = new BenchRunService.BenchRunArgs(
            CascodePath: wrapperPath,
            BenchName: null,
            OutputDir: args.OutputDir,
            Backend: Cascode.Bench.BenchBackendType.Ngspice,
            Verbose: false,
            StrictCompliance: false,
            Parallelism: 0,
            CircuitFilter: circuitName
        );

        var service = new BenchRunService(
            loggerFactory.CreateLogger<BenchRunService>(),
            progress: null
        );
        BenchRunService.MultiCircuitBenchRunResult result;
        try
        {
            result = service.RunAll(workspaceRoot, pdkRoot, runArgs);
        }
        catch (Exception ex)
        {
            return new CharGenResult(Succeeded: false, Message: $"bench run failed: {ex.Message}");
        }

        if (result.ExitCode != 0)
        {
            return new CharGenResult(
                Succeeded: false,
                Message: "bench run failed. See emitted artifacts for details.",
                JobDir: args.OutputDir
            );
        }

        var producedCsv = Path.Combine(args.OutputDir, $"{circuitName}_{bindingAlias}_results.csv");
        if (!File.Exists(producedCsv))
        {
            return new CharGenResult(
                Succeeded: false,
                Message: $"bench run succeeded, but results CSV was not produced: {producedCsv}",
                JobDir: args.OutputDir
            );
        }

        var resultsCsv = Path.Combine(args.OutputDir, "results.csv");
        File.Copy(producedCsv, resultsCsv, overwrite: true);

        output.WriteLine($"Wrote {resultsCsv}");
        output.WriteLine($"Wrapper: {wrapperPath}");

        return new CharGenResult(
            Succeeded: true,
            Message: $"Characterization complete. Results: {resultsCsv}",
            JobDir: args.OutputDir
        );
    }

    private static SpectreModel? ResolveModel(IReadOnlyList<SpectreModel> models, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var exact = models.FirstOrDefault(m =>
            m.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
        );
        if (exact is not null)
        {
            return exact;
        }

        var familyExact = models
            .Where(m =>
                PdkPrimitiveNaming
                    .PrimitiveFamilyNameFromModelName(m.Name)
                    .Equals(query, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        if (familyExact.Count > 0)
        {
            return ChooseBestFamilyModel(familyExact);
        }

        var matches = models
            .Where(m =>
                m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || PdkPrimitiveNaming
                    .PrimitiveFamilyNameFromModelName(m.Name)
                    .Contains(query, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        var families = matches
            .GroupBy(m => new
            {
                m.DeviceClass,
                Family = PdkPrimitiveNaming.PrimitiveFamilyNameFromModelName(m.Name),
            })
            .ToList();
        return families.Count == 1 ? ChooseBestFamilyModel(families[0]) : null;
    }

    private static SpectreModel? ResolveFamilyRepresentativeModel(
        IReadOnlyList<SpectreModel> models,
        DeviceClass deviceClass,
        string familyName
    )
    {
        var familyModels = models
            .Where(m =>
                m.DeviceClass == deviceClass
                && PdkPrimitiveNaming
                    .PrimitiveFamilyNameFromModelName(m.Name)
                    .Equals(familyName, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        if (familyModels.Count == 0)
        {
            return null;
        }

        return familyModels
            .Where(m => PdkPrimitiveNaming.IsFamilyRepresentativeModel(m.Name))
            .OrderBy(m => PdkPrimitiveNaming.PreferModelTypeRank(m.ModelType))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static SpectreModel ChooseBestFamilyModel(IEnumerable<SpectreModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var list = models.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("Models collection must not be empty.", nameof(models));
        }

        return list.OrderByDescending(m => PdkPrimitiveNaming.IsFamilyRepresentativeModel(m.Name))
            .ThenBy(m => PdkPrimitiveNaming.PreferModelTypeRank(m.ModelType))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string FormatDouble(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    private static string Volts(double v) => FormatDouble(v) + "V";

    private static string RenderWrapperCascode(
        CharGenArgs args,
        string circuitName,
        string deviceKind,
        bool isPmos,
        string benchName,
        string bindingAlias,
        string primitiveName,
        string pdkLibraryNamespace
    )
    {
        var sizeExpr =
            $"size(W={FormatDouble(args.WidthM)}, L={FormatDouble(args.LengthM)}, M={args.Mult.ToString(CultureInfo.InvariantCulture)})";

        var harness = isPmos ? RenderPmosHarness(args) : RenderNmosHarness(args);

        return $@"VERSION {CascodeVersion.Current}

include lib.char
include {pdkLibraryNamespace}

circuit {circuitName} {{
  level EL
  ground GND
  input D : bias
  input G : bias
  input S : bias
  input B : bias

  benches {{
    bind {benchName} as {bindingAlias} {{
      bench.G--dut.G
      bench.D--dut.D
      bench.S--dut.S
      bench.B--dut.B
    }}
  }}

  constraints {{
    numeric {{
      c_seed = {bindingAlias}::Gm >= 0S
    }}
  }}

  harness {{
{harness}
  }}

  fill {{
    {deviceKind} DUT = new {primitiveName}({sizeExpr}) {{
      .D--D
      .G--G
      .S--S
      .B--B
    }}
  }}
}}";
    }

    private static string ResolvePdkName(string? pdkRoot, string workspaceRoot)
    {
        var source = string.IsNullOrWhiteSpace(pdkRoot) ? workspaceRoot : pdkRoot;
        var derived = Path.GetFileName(Path.GetFullPath(source));
        return string.IsNullOrWhiteSpace(derived) ? "pdk" : derived;
    }

    private static string RenderNmosHarness(CharGenArgs args)
    {
        return $@"    ground GND = 0V
    bias S = 0V
    bias B = {Volts(args.VsbV)}
    bias D = {Volts(args.VdsV)}
    bias G = {Volts(args.VgsStartV)}
    sweep G [{Volts(args.VgsStartV)}:{Volts(args.VgsStepV)}:{Volts(args.VgsStopV)}]";
    }

    private static string RenderPmosHarness(CharGenArgs args)
    {
        var vdd = args.VgsStopV;
        var gateStart = vdd - args.VgsStartV;
        var gateStop = vdd - args.VgsStopV;
        var gateStep = -args.VgsStepV;

        // Interpret Vsb as Vs - Vb (a positive reverse-bias value).
        var vb = vdd - args.VsbV;
        var vd = vdd - args.VdsV;

        return $@"    ground GND = 0V
    bias S = {Volts(vdd)}
    bias B = {Volts(vb)}
    bias D = {Volts(vd)}
    bias G = {Volts(gateStart)}
    sweep G [{Volts(gateStart)}:{Volts(gateStep)}:{Volts(gateStop)}]";
    }

    private static string RenderSpecJson(CharGenArgs args, string resolvedModelName)
    {
        var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["model_name"] = resolvedModelName,
            ["corner"] = args.Corner,
            ["backend"] = args.Backend,
            ["w_m"] = args.WidthM,
            ["l_m"] = args.LengthM,
            ["nf"] = args.Nf,
            ["vds_fixed"] = args.VdsV,
            ["vsb_fixed"] = args.VsbV,
            ["temperature_c"] = args.TemperatureC,
            ["device_name"] = args.DeviceName,
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }
}
