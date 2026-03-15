using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cascode.Bench;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.Cli.Services;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Cascode.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class StressFolderIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _outputDir;
    private readonly CascodeHomeScope _cascodeHome;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public StressFolderIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-stress-folder-test-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "stress-folder");
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (Directory.Exists(_outputDir))
        {
            try
            {
                Directory.Delete(_outputDir, recursive: true);
            }
            catch { }
        }
    }

    public static IEnumerable<object[]> StressCases()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        var stressDir = Path.Combine(repoRoot, "tests", "golden", "cas", "stress");
        foreach (var path in Directory.GetFiles(stressDir, "*.cas", SearchOption.TopDirectoryOnly))
        {
            yield return new object[] { path };
        }
    }

    [Theory]
    [MemberData(nameof(StressCases))]
    [Trait("Category", "Simulation")]
    public async Task StressFolder_AllCasFiles_RunAllBenches_ProduceResults_AndNoConstraintMeasurementsMissing(
        string cascodePath
    )
    {
        // Link in-process so expectations come from the same model as the CLI.
        var doc = LoadAndLinkIfNeededForTest(cascodePath);
        var plans = BenchCompiler.CompileAllPlans(doc);
        Assert.True(plans.Count > 0, "expected at least one bench plan");

        // If the stress case uses a PDK device, set+scan the fixture PDK once for this run.
        // This keeps the stress harness self-contained: adding a .cas that uses sky130/gpdk045
        // will automatically get the required workspace DB.
        if (RequiresPdkWorkspace(doc, out var pdkName, out var pdkRoot))
        {
            await SetupPdkAndRunBench(pdkRoot, cascodePath);

            await AssertRunArtifactsAndResults(doc, plans, pdkName);
            return;
        }

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(5),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");

        await AssertRunArtifactsAndResults(doc, plans, expectedPdkMarker: null);
    }

    [Theory]
    [MemberData(nameof(StressCases))]
    public async Task StressFolder_AllCasFiles_PassErc(string cascodePath)
    {
        var erc = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            cascodePath
        );
        CliIntegrationTestHelper.AssertSuccess(erc, "erc failed");
    }

    [Theory]
    [MemberData(nameof(StressCases))]
    public async Task StressFolder_AllCasFiles_RenderSucceeds_AndProducesDevices(string cascodePath)
    {
        using var sandbox = new TemporaryDirectory();
        var sandboxedPath = StageStressRenderInput(cascodePath, sandbox.Path);

        var renderDir = Path.Combine(_outputDir, "render");
        Directory.CreateDirectory(renderDir);

        var render = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "render",
            sandboxedPath,
            "--output",
            renderDir
        );
        CliIntegrationTestHelper.AssertSuccess(render, "render failed");

        var doc = LoadAndLinkIfNeededForTest(sandboxedPath);

        foreach (
            var circuit in doc
                .Circuits.Where(c => c.Level == CascodeLevel.EL && !c.Inline)
                .OrderBy(c => c.Name, StringComparer.Ordinal)
        )
        {
            var svgPath = Path.Combine(renderDir, $"{circuit.Name}.svg");
            Assert.True(File.Exists(svgPath), $"SVG not found: {svgPath}");

            var content = await File.ReadAllTextAsync(svgPath);
            Assert.Contains("<svg", content);
            Assert.Matches(new Regex("class=\"device\\b", RegexOptions.CultureInvariant), content);
        }
    }

    private static string StageStressRenderInput(string sourceCasPath, string sandboxRoot)
    {
        var sourceDir = Path.GetDirectoryName(sourceCasPath) ?? Directory.GetCurrentDirectory();
        var sandboxDir = Path.Combine(sandboxRoot, "stress");
        Directory.CreateDirectory(sandboxDir);

        foreach (
            var sourcePath in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly)
        )
        {
            var destinationPath = Path.Combine(sandboxDir, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        return Path.Combine(sandboxDir, Path.GetFileName(sourceCasPath));
    }

    [Fact]
    public void Ota5tSky130_UsesPdkInclude_InsteadOfInlinePrimitives()
    {
        var cascodePath = Path.Combine(
            _repoRoot,
            "tests",
            "golden",
            "cas",
            "stress",
            "OTA5T_Sky130.cas"
        );

        var sourceText = File.ReadAllText(cascodePath);
        Assert.Contains("include lib.pdk.sky130", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("primitive NMOS", sourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("primitive PMOS", sourceText, StringComparison.OrdinalIgnoreCase);

        var linked = LoadAndLinkIfNeededForTest(cascodePath);
        Assert.Contains(
            linked.Primitives,
            primitive => primitive.Name.Equals("nfet_01v8", StringComparison.Ordinal)
        );
        Assert.Contains(
            linked.Primitives,
            primitive => primitive.Name.Equals("pfet_01v8", StringComparison.Ordinal)
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task CSAmpActiveLoadSky130_AllConstraintsPass() =>
        await RunConstraintCheckForCas("CSAmp_ActiveLoad_Sky130.cas", "CSAmp_ActiveLoad_Sky130");

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task CapFeedbackFDSky130_AllConstraintsPass() =>
        await RunConstraintCheckForCas("CapFeedbackFD_Sky130.cas", "CapFeedbackFD_Sky130");

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task LNA_CSCascodeInductivelyDegenerated_Sky130_AllConstraintsPass() =>
        await RunConstraintCheckForCas(
            "LNA_CSCascodeInductivelyDegenerated_Sky130.cas",
            "LNA_CSCascodeInductivelyDegenerated_Sky130"
        );

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130_AllConstraintsPass() =>
        await RunConstraintCheckForCas(
            "LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130.cas",
            "LNA_CSCascodeInductivelyDegenerated_TwoStage_Sky130"
        );

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task SST12LN01_Sky130_AllConstraintsPass() =>
        await RunConstraintCheckForCas("SST12LN01_Sky130.cas", "SST12LN01_Sky130");

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task TLC2272A_Sky130_AllConstraintsPass()
    {
        await RunConstraintCheckForCas("TLC2272A_Sky130.cas", "TLC2272A_Sky130");

        var noiseWrdataPath = Path.Combine(
            _outputDir,
            "TLC2272A_Sky130_noise_bench__noise_ac.noise.wrdata"
        );
        Assert.True(File.Exists(noiseWrdataPath), $"noise wrdata not found: {noiseWrdataPath}");

        var acWrdataPath = Path.Combine(_outputDir, "TLC2272A_Sky130_noise_bench__ac.ac.wrdata");
        Assert.True(File.Exists(acWrdataPath), $"noise AC wrdata not found: {acWrdataPath}");

        var outputNoise = NgspiceWrdataNoiseParser.Parse(noiseWrdataPath);
        var ac = NgspiceWrdataAcParser.Parse(acWrdataPath, new[] { "IN_N", "IN_P", "OUT" });

        var inputReferredSpotNoise = ComputeInputReferredNoiseAt(
            outputNoise,
            ac,
            frequencyHz: 1_000
        );
        Assert.True(
            inputReferredSpotNoise <= 9e-9,
            $"expected TLC2272A spot noise <= 9nV/rtHz, actual {inputReferredSpotNoise} V/rtHz"
        );
    }

    private static double ComputeInputReferredNoiseAt(
        NoiseDataset outputNoise,
        AcDataset ac,
        double frequencyHz
    )
    {
        var inputP = ac.NodeVoltages["IN_P"];
        var inputN = ac.NodeVoltages["IN_N"];
        var output = ac.NodeVoltages["OUT"];

        var transferMagnitudes = new double[ac.FrequenciesHz.Length];
        for (var i = 0; i < ac.FrequenciesHz.Length; i++)
        {
            var inputDiff = inputP[i] - inputN[i];
            transferMagnitudes[i] =
                inputDiff == Complex.Zero ? 0 : (output[i] / inputDiff).Magnitude;
        }

        var outputNoiseDensity = InterpolateLogFrequency(
            outputNoise.FrequenciesHz,
            outputNoise.OutputNoiseVPerRtHz,
            frequencyHz
        );
        var transferMagnitude = InterpolateLogFrequency(
            ac.FrequenciesHz,
            transferMagnitudes,
            frequencyHz
        );

        Assert.True(transferMagnitude > 0, "expected positive differential transfer magnitude");
        return outputNoiseDensity / transferMagnitude;
    }

    private static double InterpolateLogFrequency(
        IReadOnlyList<double> frequenciesHz,
        IReadOnlyList<double> values,
        double targetHz
    )
    {
        Assert.True(frequenciesHz.Count > 0, "expected at least one frequency sample");
        Assert.Equal(frequenciesHz.Count, values.Count);

        if (targetHz <= frequenciesHz[0])
        {
            return values[0];
        }

        var last = frequenciesHz.Count - 1;
        if (targetHz >= frequenciesHz[last])
        {
            return values[last];
        }

        var upper = 1;
        while (frequenciesHz[upper] < targetHz)
        {
            upper++;
        }

        var lower = upper - 1;
        var lowerHz = frequenciesHz[lower];
        var upperHz = frequenciesHz[upper];
        var lowerValue = values[lower];
        var upperValue = values[upper];

        var position =
            (Math.Log10(targetHz) - Math.Log10(lowerHz))
            / (Math.Log10(upperHz) - Math.Log10(lowerHz));
        return lowerValue + ((upperValue - lowerValue) * position);
    }

    private async Task RunConstraintCheckForCas(string casFileName, string circuitName)
    {
        var cascodePath = Path.Combine(_repoRoot, "tests", "golden", "cas", "stress", casFileName);

        var doc = LoadAndLinkIfNeededForTest(cascodePath);
        Assert.True(RequiresPdkWorkspace(doc, out _, out var pdkRoot), "expected sky130 workspace");

        await SetupPdkAndRunBench(pdkRoot, cascodePath);

        var circuit = Assert.Single(
            doc.Circuits,
            c => c.Name.Equals(circuitName, StringComparison.Ordinal)
        );
        var combinedResultsPath = Path.Combine(_outputDir, $"{circuit.Name}_results.json");
        Assert.True(
            File.Exists(combinedResultsPath),
            $"combined results not found: {combinedResultsPath}"
        );

        var combinedResults = JsonSerializer.Deserialize<BenchResult>(
            await File.ReadAllTextAsync(combinedResultsPath),
            s_jsonOptions
        );
        Assert.NotNull(combinedResults);

        var report = ComplianceChecker.Check(circuit, combinedResults!);
        var failures = report
            .Results.Where(r => !r.Passed)
            .Select(r => $"{r.Id}: {r.Message}")
            .ToArray();

        Assert.True(report.TotalCount > 0, "expected at least one numeric constraint");
        Assert.True(
            report.FailedCount == 0,
            $"expected {circuitName} to satisfy all numeric constraints, failures: "
                + string.Join(", ", failures)
        );
    }

    /// <summary>
    /// Initializes the workspace PDK and executes bench run for a stress-case input.
    /// </summary>
    private async Task SetupPdkAndRunBench(string pdkRoot, string cascodePath)
    {
        var pdkSet = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(10),
            _cascodeHome,
            "pdk",
            "set-dir",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(pdkSet, "pdk set-dir failed");

        var scan = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(3),
            _cascodeHome,
            "pdk",
            "scan",
            pdkRoot
        );
        CliIntegrationTestHelper.AssertSuccess(scan, "pdk scan failed");

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(5),
            _cascodeHome,
            "bench",
            "run",
            cascodePath,
            "-o",
            _outputDir
        );
        CliIntegrationTestHelper.AssertSuccess(run, "bench run failed");
    }

    private CascodeDocument LoadAndLinkIfNeededForTest(string inputPath)
    {
        // This helper does not “cheat” the CLI run; it only produces the same in-memory model
        // (including include linking) that the CLI uses, so test expectations line up.
        var linkArtifactsDir = Path.Combine(_outputDir, "link");
        var loaded = CascodeLoadLinkService.LoadAndLinkIfNeeded(
            inputPath,
            workspaceRootHint: _repoRoot,
            linkArtifactsDir: linkArtifactsDir,
            logger: NullLogger.Instance
        );
        return loaded.Document;
    }

    private async Task AssertRunArtifactsAndResults(
        CascodeDocument doc,
        IReadOnlyList<BenchPlan> plans,
        string? expectedPdkMarker
    )
    {
        // Assert per-bench artifacts exist and results are parseable.
        foreach (var plan in plans)
        {
            var tbPath = Path.Combine(_outputDir, $"{plan.CircuitName}_{plan.InstanceName}.sp");
            var resultsPath = Path.Combine(
                _outputDir,
                $"{plan.CircuitName}_{plan.InstanceName}_results.json"
            );

            Assert.True(File.Exists(tbPath), $"testbench not found: {tbPath}");
            Assert.True(File.Exists(resultsPath), $"results.json not found: {resultsPath}");

            if (!string.IsNullOrEmpty(expectedPdkMarker))
            {
                var tbText = await File.ReadAllTextAsync(tbPath);
                Assert.Contains(expectedPdkMarker, tbText);
            }

            var results = JsonSerializer.Deserialize<BenchResult>(
                await File.ReadAllTextAsync(resultsPath),
                s_jsonOptions
            );
            Assert.NotNull(results);
            Assert.Equal(plan.CircuitName, results!.Circuit);
            Assert.Equal(plan.InstanceName, results.Bench);
            Assert.True(results.Measurements.Count > 0);
            Assert.All(
                results.Measurements.Values,
                m =>
                {
                    if (string.IsNullOrEmpty(m.Error))
                    {
                        if (m.Values is not null)
                        {
                            Assert.True(m.Values.Length > 0);
                            Assert.All(
                                m.Values,
                                value =>
                                {
                                    Assert.False(double.IsNaN(value));
                                    Assert.False(double.IsInfinity(value));
                                }
                            );
                        }
                        else
                        {
                            Assert.True(
                                m.Value.HasValue,
                                "expected scalar Value when Values is null"
                            );
                            Assert.False(double.IsNaN(m.Value.Value));
                            Assert.False(double.IsInfinity(m.Value.Value));
                        }
                    }
                }
            );

            // Bench-contract check: for the standard Diff->Diff transfer bench, ensure the
            // load impedance is split per-leg using DiffToShunt().
            await AssertDiffToDiffLoadSplitIfApplicable(plan, tbPath);
        }

        // For every EL circuit, ensure its numeric constraints resolve to measured values (not
        // missing/error). This is the core stress invariant: adding a new constraint should fail CI
        // until the backend exists.
        foreach (
            var circuit in doc.Circuits.Where(c =>
                c.Level == CascodeLevel.EL && !c.Inline && c.Constraints?.Numeric?.Count > 0
            )
        )
        {
            var combinedResultsPath = Path.Combine(_outputDir, $"{circuit.Name}_results.json");
            Assert.True(
                File.Exists(combinedResultsPath),
                $"combined results not found: {combinedResultsPath}"
            );

            var combinedResults = JsonSerializer.Deserialize<BenchResult>(
                await File.ReadAllTextAsync(combinedResultsPath),
                s_jsonOptions
            );
            Assert.NotNull(combinedResults);

            var report = ComplianceChecker.Check(circuit, combinedResults!);
            Assert.All(
                report.Results,
                r =>
                {
                    Assert.DoesNotContain("No measurement found", r.Message);
                    Assert.DoesNotContain("Measurement error", r.Message);
                    Assert.True(r.Actual.HasValue, "expected constraint to have an actual value");
                    Assert.False(double.IsNaN(r.Actual.Value));
                    Assert.False(double.IsInfinity(r.Actual.Value));
                }
            );
        }
    }

    private async Task AssertDiffToDiffLoadSplitIfApplicable(BenchPlan plan, string tbPath)
    {
        if (!plan.BenchName.Equals("DiffToDiffTransfer", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!plan.Env.TryGetValue("LoadImpedance", out var rawZ))
        {
            return;
        }

        if (rawZ is not BenchImpedanceParallel z || z.Elements.Count == 0)
        {
            return;
        }

        // DiffToShunt => Z/2.
        var expected = ScaleImpedance(z, factor: 0.5);
        var tbText = await File.ReadAllTextAsync(tbPath);

        foreach (var element in expected.Elements)
        {
            var formatted = SiValue.FormatForBackend(element.Value, BenchBackendType.Ngspice);
            // Each element should appear at least twice in the netlist (one per output leg).
            var count = tbText.Split(formatted, StringSplitOptions.None).Length - 1;
            Assert.True(
                count >= 2,
                $"expected '{formatted}' to appear at least twice in {Path.GetFileName(tbPath)}"
            );
        }
    }

    private static BenchImpedanceParallel ScaleImpedance(BenchImpedanceParallel z, double factor)
    {
        var scaled = new BenchNumber[z.Elements.Count];
        for (var i = 0; i < z.Elements.Count; i++)
        {
            var e = z.Elements[i];
            scaled[i] = e.Kind switch
            {
                BenchNumericKind.ImpedanceOhm => new BenchNumber(e.Kind, e.Value * factor),
                BenchNumericKind.CapacitanceF => new BenchNumber(e.Kind, e.Value / factor),
                BenchNumericKind.InductanceH => new BenchNumber(e.Kind, e.Value * factor),
                _ => throw new InvalidOperationException(
                    $"invalid impedance element kind '{e.Kind.ToString()}'"
                ),
            };
        }
        return new BenchImpedanceParallel(scaled);
    }

    private bool RequiresPdkWorkspace(CascodeDocument doc, out string pdkMarker, out string pdkRoot)
    {
        pdkMarker = string.Empty;
        pdkRoot = string.Empty;

        // Today we only allow/ship sky130 and gpdk045 fixtures. Prefer include-based detection
        // (stable across primitive model naming), then fall back to primitive metadata.
        var includeNames = doc
            .Includes.Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();
        if (
            includeNames.Any(n =>
                n.StartsWith("lib.pdk.sky130", StringComparison.OrdinalIgnoreCase)
            )
            || doc.Primitives.Any(p =>
                p.Params.Values.Any(v => v.Contains("sky130_", StringComparison.OrdinalIgnoreCase))
            )
        )
        {
            pdkMarker = "sky130.lib.spice";
            pdkRoot = Path.Combine(_repoRoot, "tests", "fixtures", "pdk", "sky130");
            return true;
        }

        if (
            includeNames.Any(n =>
                n.StartsWith("lib.pdk.gpdk045", StringComparison.OrdinalIgnoreCase)
            )
            || doc.Primitives.Any(p =>
                p.Params.Values.Any(v => v.Contains("gpdk045", StringComparison.OrdinalIgnoreCase))
            )
        )
        {
            // TODO: add gpdk045 fixture and marker once we have a first-class stress case for it.
            // For now, treat as unsupported rather than silently skipping.
            throw new InvalidOperationException(
                "Stress case requires gpdk045 but no gpdk045 fixture is configured for tests."
            );
        }

        return false;
    }
}
