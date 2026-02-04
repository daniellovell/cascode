using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var pdkSet = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(10),
                _cascodeHome,
                "pdk",
                "set-dir",
                pdkRoot
            );
            CliIntegrationTestHelper.AssertSuccess(pdkSet, "pdk set-dir failed");

            var scan = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(90),
                _cascodeHome,
                "pdk",
                "scan",
                pdkRoot
            );
            CliIntegrationTestHelper.AssertSuccess(scan, "pdk scan failed");

            var runWithPdk = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(120),
                _cascodeHome,
                "bench",
                "run",
                cascodePath,
                "-o",
                _outputDir
            );
            CliIntegrationTestHelper.AssertSuccess(runWithPdk, "bench run failed");

            await AssertRunArtifactsAndResults(doc, plans, pdkName);
            return;
        }

        var run = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(90),
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
        var renderDir = Path.Combine(_outputDir, "render");
        Directory.CreateDirectory(renderDir);

        var render = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "render",
            cascodePath,
            "--output",
            renderDir
        );
        CliIntegrationTestHelper.AssertSuccess(render, "render failed");

        var doc = LoadAndLinkIfNeededForTest(cascodePath);
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
                        Assert.False(double.IsNaN(m.Value));
                        Assert.False(double.IsInfinity(m.Value));
                    }
                }
            );

            // Bench-contract check: for the standard Diff->Diff transfer bench, ensure the
            // load impedance is split per-leg using DiffToShunt().
            await AssertDiffToDiffLoadSplitIfApplicable(plan, tbPath);
        }

        // For every EL circuit, ensure its numeric constraints resolve to measured values (not missing/error).
        // This is the core stress invariant: adding a new constraint should fail CI until the backend exists.
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

        // Today we only allow/ship sky130 and gpdk045 fixtures. If a stress file uses a PDK primitive,
        // it must be one of these.
        var deviceKeys = doc
            .Primitives.Select(p => p.Device)
            .Where(s => !string.IsNullOrWhiteSpace(s));
        if (deviceKeys.Any(d => d.Contains("sky130_", StringComparison.OrdinalIgnoreCase)))
        {
            pdkMarker = "sky130.lib.spice";
            pdkRoot = Path.Combine(_repoRoot, "tests", "fixtures", "pdk", "sky130");
            return true;
        }

        if (deviceKeys.Any(d => d.Contains("gpdk045", StringComparison.OrdinalIgnoreCase)))
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
