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
        double VgsStepV
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

        var primitivesDir = Path.Combine(workspaceRoot, "lib", "pdk");
        var primitivesFiles = Directory.Exists(primitivesDir)
            ? Directory
                .EnumerateFiles(primitivesDir, "*_Primitives.cas", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        if (primitivesFiles.Count == 0)
        {
            return new CharGenResult(
                Succeeded: false,
                Message: "No PDK primitive library found under lib/pdk. Run 'pdk emit primitives' first."
            );
        }
        if (primitivesFiles.Count > 1)
        {
            return new CharGenResult(
                Succeeded: false,
                Message: "Multiple PDK primitive libraries found under lib/pdk. Keep exactly one '*_Primitives.cas' file for now."
            );
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

        var primitiveName = PrimitiveNameFromModelName(model.Name);
        var circuitName = SanitizeIdentifier("CharGmId_" + primitiveName);
        var deviceKind = model.DeviceClass == DeviceClass.Nmos ? "NMOS" : "PMOS";
        var isPmos = model.DeviceClass == DeviceClass.Pmos;
        var benchName = model.DeviceClass == DeviceClass.Nmos ? "GmIdNmos" : "GmIdPmos";
        var bindingAlias = "gm_id";

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
                primitiveName
            )
        );

        var specPath = Path.Combine(args.OutputDir, "spec.json");
        File.WriteAllText(specPath, RenderSpecJson(args, model.Name));

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

        var matches = models
            .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string RenderWrapperCascode(
        CharGenArgs args,
        string circuitName,
        string deviceKind,
        bool isPmos,
        string benchName,
        string bindingAlias,
        string primitiveName
    )
    {
        static string F(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

        var sizeExpr =
            $"size(W={F(args.WidthM)}, L={F(args.LengthM)}, M={args.Mult.ToString(CultureInfo.InvariantCulture)}, NF={args.Nf.ToString(CultureInfo.InvariantCulture)})";

        var harness = isPmos ? RenderPmosHarness(args) : RenderNmosHarness(args);

        return $@"VERSION {CascodeVersion.Current}

include lib.char
include lib.pdk

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

    private static string RenderNmosHarness(CharGenArgs args)
    {
        static string F(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
        static string V(double v) => F(v) + "V";

        return $@"    ground GND = 0V
    bias S = 0V
    bias B = {V(args.VsbV)}
    bias D = {V(args.VdsV)}
    bias G = {V(args.VgsStartV)}
    sweep G [{V(args.VgsStartV)}:{V(args.VgsStepV)}:{V(args.VgsStopV)}]";
    }

    private static string RenderPmosHarness(CharGenArgs args)
    {
        static string F(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
        static string V(double v) => F(v) + "V";

        var vdd = args.VgsStopV;
        var gateStart = vdd - args.VgsStartV;
        var gateStop = vdd - args.VgsStopV;
        var gateStep = -args.VgsStepV;

        // Interpret Vsb as Vs - Vb (a positive reverse-bias value).
        var vb = vdd - args.VsbV;
        var vd = vdd - args.VdsV;

        return $@"    ground GND = 0V
    bias S = {V(vdd)}
    bias B = {V(vb)}
    bias D = {V(vd)}
    bias G = {V(gateStart)}
    sweep G [{V(gateStart)}:{V(gateStep)}:{V(gateStop)}]";
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
            ["temperature_c"] = 27.0,
            ["device_name"] = args.DeviceName,
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string PrimitiveNameFromModelName(string modelName)
    {
        var name = modelName ?? string.Empty;
        var modelMarker = name.IndexOf("__model", StringComparison.OrdinalIgnoreCase);
        if (modelMarker >= 0)
        {
            name = name.Substring(0, modelMarker);
        }

        var lastSep = name.LastIndexOf("__", StringComparison.Ordinal);
        if (lastSep >= 0 && lastSep + 2 < name.Length)
        {
            name = name[(lastSep + 2)..];
        }

        name = name.Replace('.', '_');
        name = SanitizeIdentifier(name);
        return string.IsNullOrWhiteSpace(name) ? "Primitive" : name;
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var chars = name.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
            .ToArray();
        var sanitized = new string(chars);
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]) && sanitized[0] != '_')
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }
}
