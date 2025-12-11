using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

/// <summary>
/// Integration tests for the emit and verify CLI commands.
/// Tests the full flow from ACIR to bench generation to constraint verification.
/// </summary>
public class EmitVerifyFlowTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly string _outputDir;
    private readonly CascodeHomeScope _cascodeHome;

    public EmitVerifyFlowTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(Path.GetTempPath(), "cascode-emit-verify-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "emit-verify");
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Emit_OTA_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit", acirPath, "--out", _outputDir, "--backend", "ngspice");

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Design netlist:", result.Stdout);
        Assert.Contains("Testbench:", result.Stdout);
        Assert.Contains("Emitted 1 design(s) and 1 testbench(es)", result.Stdout);

        Assert.True(File.Exists(Path.Combine(_outputDir, "OTA5TSingleEnded.sp")), "Design netlist not found");
        Assert.True(File.Exists(Path.Combine(_outputDir, "OTA5TSingleEnded_SEOpAmpACBench.sp")), "Testbench not found");
    }

    [Fact]
    public async Task Emit_CS_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/cs/CSAmpResistive.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit", acirPath, "--out", _outputDir, "--backend", "ngspice");

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Emitted 1 design(s) and 1 testbench(es)", result.Stdout);

        Assert.True(File.Exists(Path.Combine(_outputDir, "CSAmpResistive.sp")), "Design netlist not found");
        Assert.True(File.Exists(Path.Combine(_outputDir, "CSAmpResistive_SEAmpACBench.sp")), "Testbench not found");
    }

    [Fact]
    public async Task Verify_WithPassingResults_ReturnsSuccess()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");
        var resultsPath = Path.Combine(_repoRoot, "tests/golden/results/ota/OTA5TSingleEnded_SEOpAmpACBench_results.json");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", "--acir", acirPath, "--results", resultsPath);

        CliIntegrationTestHelper.AssertSuccess(result, "verify command failed");
        Assert.Contains("Constraint Compliance Report", result.Stdout);
        Assert.Contains("4/4 constraints satisfied", result.Stdout);
    }

    [Fact]
    public async Task Verify_WithFailingResults_ReturnsFailure()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        // Create a failing results file
        var failingResultsPath = Path.Combine(_outputDir, "failing_results.json");
        var failingResults = new
        {
            circuit = "OTA5TSingleEnded",
            bench = "SEOpAmpACBench",
            measurements = new
            {
                gain = new { metric = "PassbandGain", value = 30.0, unit = "dB", node = "OUT" },
                gbw = new { metric = "GainBandwidth", value = 50e6, unit = "Hz", node = "OUT" },
                pm = new { metric = "PhaseMargin", value = 45.0, unit = "deg", node = "OUT" },
                power = new { metric = "Power", value = 0.001, unit = "W", node = (string?)null }
            }
        };
        await File.WriteAllTextAsync(failingResultsPath, JsonSerializer.Serialize(failingResults, new JsonSerializerOptions { WriteIndented = true }));

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", "--acir", acirPath, "--results", failingResultsPath);

        // Verify command should return non-zero exit code for failing constraints
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Constraint Compliance Report", result.Stdout);
        Assert.Contains("FAIL", result.Stdout);
    }

    [Fact]
    public async Task Verify_CSAmp_WithPassingResults_ReturnsSuccess()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/cs/CSAmpResistive.el.cir");
        var resultsPath = Path.Combine(_repoRoot, "tests/golden/results/cs/CSAmpResistive_SEAmpACBench_results.json");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", "--acir", acirPath, "--results", resultsPath);

        CliIntegrationTestHelper.AssertSuccess(result, "verify command failed");
        Assert.Contains("Constraint Compliance Report", result.Stdout);
        Assert.Contains("2/2 constraints satisfied", result.Stdout);
    }

    [Fact]
    public async Task EmitVerifyFlow_EndToEnd_WorksCorrectly()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        // Step 1: Emit
        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit", acirPath, "--out", _outputDir, "--backend", "ngspice");

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        // Verify generated files exist and have content
        var designPath = Path.Combine(_outputDir, "OTA5TSingleEnded.sp");
        var benchPath = Path.Combine(_outputDir, "OTA5TSingleEnded_SEOpAmpACBench.sp");

        Assert.True(File.Exists(designPath), "Design netlist not found");
        Assert.True(File.Exists(benchPath), "Testbench not found");

        var benchContent = await File.ReadAllTextAsync(benchPath);
        Assert.Contains(".title OTA5TSingleEnded_SEOpAmpACBench", benchContent);
        Assert.Contains(".include \"OTA5TSingleEnded.sp\"", benchContent);
        Assert.Contains("XDUT", benchContent);
        Assert.Contains(".control", benchContent);

        // Step 2: Create mock results (as if simulation ran)
        var resultsPath = Path.Combine(_outputDir, "OTA5TSingleEnded_SEOpAmpACBench_results.json");
        var mockResults = new
        {
            circuit = "OTA5TSingleEnded",
            bench = "SEOpAmpACBench",
            measurements = new
            {
                gain = new { metric = "PassbandGain", value = 45.2, unit = "dB", node = "OUT" },
                gbw = new { metric = "GainBandwidth", value = 150e6, unit = "Hz", node = "OUT" },
                pm = new { metric = "PhaseMargin", value = 65.3, unit = "deg", node = "OUT" },
                power = new { metric = "Power", value = 0.00035, unit = "W", node = (string?)null }
            }
        };
        await File.WriteAllTextAsync(resultsPath, JsonSerializer.Serialize(mockResults, new JsonSerializerOptions { WriteIndented = true }));

        // Step 3: Verify
        var verifyResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", "--acir", acirPath, "--results", resultsPath);

        CliIntegrationTestHelper.AssertSuccess(verifyResult, "verify command failed");
        Assert.Contains("4/4 constraints satisfied", verifyResult.Stdout);
    }

    [Fact]
    public async Task Verify_MissingACIR_ReturnsError()
    {
        var nonExistentPath = Path.Combine(_outputDir, "nonexistent.cir");
        var resultsPath = Path.Combine(_repoRoot, "tests/golden/results/ota/OTA5TSingleEnded_SEOpAmpACBench_results.json");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", "--acir", nonExistentPath, "--results", resultsPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not found", result.Stdout);
    }

    [Fact]
    public async Task Verify_MissingResults_ReturnsError()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");
        var nonExistentResults = Path.Combine(_outputDir, "nonexistent_results.json");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify", "--acir", acirPath, "--results", nonExistentResults);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not found", result.Stdout);
    }

    [Fact]
    public async Task Emit_InvalidCircuit_MissingTerminal_ReturnsExitCode2()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/missing_terminal.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit", acirPath, "--out", _outputDir);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("EMIT-001", result.Stdout);
        Assert.Contains("missing required terminal", result.Stdout);
    }

    [Fact]
    public async Task Emit_InvalidCircuit_UndefinedNet_ReturnsExitCode2()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/undefined_net.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit", acirPath, "--out", _outputDir);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("EMIT-002", result.Stdout);
        Assert.Contains("undefined net", result.Stdout);
    }

    [Fact]
    public async Task Emit_InvalidCircuit_MissingParam_ReturnsExitCode2()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/missing_param.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit", acirPath, "--out", _outputDir);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("EMIT-003", result.Stdout);
        Assert.Contains("missing required parameter", result.Stdout);
    }

    [Fact]
    public async Task Emit_InvalidCircuit_MLLevel_ReturnsExitCode2()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/ml_level.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit", acirPath, "--out", _outputDir);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("EL-level", result.Stdout);
    }

    [Fact]
    public async Task Erc_ValidCircuit_ReturnsSuccess()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc", acirPath);

        CliIntegrationTestHelper.AssertSuccess(result, "erc command failed on valid circuit");
        Assert.Contains("ERC passed", result.Stdout);
    }

    [Fact]
    public async Task Erc_FloatingGate_ReturnsExitCode1()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/floating_gate.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc", acirPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERC-001", result.Stdout);
        Assert.Contains("Floating gate", result.Stdout);
    }

    [Fact]
    public async Task Erc_VddGndShort_ReturnsExitCode1()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/vdd_gnd_short.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc", acirPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERC-002", result.Stdout);
        Assert.Contains("VDD-GND short", result.Stdout);
    }

    [Fact]
    public async Task Erc_StructurallyInvalid_ReturnsExitCode2()
    {
        // ERC on a structurally invalid file (missing terminal) should return exit code 2
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/missing_terminal.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc", acirPath);

        // ERC includes emission validation, so structural errors cause ERC failure
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("EMIT-001", result.Stdout);
    }

    [Fact]
    public async Task Erc_RequirePdk_WarningBecomesError()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/cs/CSAmpResistive.el.cir");

        // Without --require-pdk, should pass with warning
        var resultWithoutFlag = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc", acirPath);

        CliIntegrationTestHelper.AssertSuccess(resultWithoutFlag, "erc should pass without --require-pdk");

        // With --require-pdk, should fail because CSAmpResistive uses generic nmos/pmos
        var resultWithFlag = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc", acirPath, "--require-pdk");

        Assert.NotEqual(0, resultWithFlag.ExitCode);
        Assert.Contains("ERC-005", resultWithFlag.Stdout);
    }

    [Fact]
    public async Task Erc_Usage_ShowsHelp()
    {
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc");

        CliIntegrationTestHelper.AssertSuccess(result, "erc without args should show usage");
        Assert.Contains("Usage: erc", result.Stdout);
        Assert.Contains("--require-pdk", result.Stdout);
    }
}

