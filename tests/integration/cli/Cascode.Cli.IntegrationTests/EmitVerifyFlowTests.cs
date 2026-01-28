using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

/// <summary>
/// Integration tests for the emit and verify CLI commands.
/// Tests the full flow from ACIR to bench generation to constraint verification.
/// </summary>
public partial class EmitVerifyFlowTests : IDisposable
{
    private const string FloatingPointPattern = @"[-+]?(\d+(\.\d*)?|\.\d+)([eE][-+]?\d+)?";

    private readonly string _repoRoot;
    private readonly string _outputDir;
    private readonly CascodeHomeScope _cascodeHome;

    public EmitVerifyFlowTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "cascode-emit-verify-test-" + Guid.NewGuid().ToString("N")[..8]
        );
        Directory.CreateDirectory(_outputDir);
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "emit-verify");
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

    [Fact]
    public async Task Emit_OTA_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Design netlist:", result.Stdout);
        Assert.Contains("Testbench:", result.Stdout);
        Assert.Contains("Emitted 1 design(s) and 3 testbench(es)", result.Stdout);

        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TSingleEnded.sp")),
            "Design netlist not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TSingleEnded_ACBench.sp")),
            "AC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TSingleEnded_DCBench.sp")),
            "DC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TSingleEnded_TranBench.sp")),
            "Tran testbench not found"
        );
    }

    [Fact]
    public async Task Emit_CS_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/cs/CSAmpResistive.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Emitted 1 design(s) and 1 testbench(es)", result.Stdout);

        Assert.True(
            File.Exists(Path.Combine(_outputDir, "CSAmpResistive.sp")),
            "Design netlist not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "CSAmpResistive_ACBench.sp")),
            "Testbench not found"
        );
    }

    [Fact]
    public async Task Verify_WithPassingResults_ReturnsSuccess()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");
        var resultsPath = Path.Combine(
            _repoRoot,
            "tests/golden/results/ota/OTA5TSingleEnded_ACBench_results.json"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            "--acir",
            acirPath,
            "--results",
            resultsPath
        );

        CliIntegrationTestHelper.AssertSuccess(result, "verify command failed");
        Assert.Contains("Constraint Compliance Report", result.Stdout);
        Assert.Contains("3/3 constraints satisfied", result.Stdout);
        Assert.Contains("Note: 1 constraint (c_pwr) measured by DCBench.", result.Stdout);
        Assert.Contains("Note: 1 constraint (c_swing) measured by TranBench.", result.Stdout);
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
            bench = "ACBench",
            measurements = new
            {
                gain = new
                {
                    metric = "PassbandGain",
                    value = 30.0,
                    unit = "dB",
                    node = "OUT",
                },
                gbw = new
                {
                    metric = "GainBandwidth",
                    value = 50e6,
                    unit = "Hz",
                    node = "OUT",
                },
                pm = new
                {
                    metric = "PhaseMargin",
                    value = 45.0,
                    unit = "deg",
                    node = "OUT",
                },
            },
        };
        await File.WriteAllTextAsync(
            failingResultsPath,
            JsonSerializer.Serialize(
                failingResults,
                new JsonSerializerOptions { WriteIndented = true }
            )
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            "--acir",
            acirPath,
            "--results",
            failingResultsPath
        );

        // Verify command should return non-zero exit code for failing constraints
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Constraint Compliance Report", result.Stdout);
        Assert.Contains("FAIL", result.Stdout);
    }

    [Fact]
    public async Task Verify_CSAmp_WithPassingResults_ReturnsSuccess()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/cs/CSAmpResistive.el.cir");
        var resultsPath = Path.Combine(
            _repoRoot,
            "tests/golden/results/cs/CSAmpResistive_ACBench_results.json"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            "--acir",
            acirPath,
            "--results",
            resultsPath
        );

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
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        // Verify generated files exist and have content
        var designPath = Path.Combine(_outputDir, "OTA5TSingleEnded.sp");
        var benchPath = Path.Combine(_outputDir, "OTA5TSingleEnded_ACBench.sp");

        Assert.True(File.Exists(designPath), "Design netlist not found");
        Assert.True(File.Exists(benchPath), "Testbench not found");

        var benchContent = await File.ReadAllTextAsync(benchPath);
        Assert.Contains(".title OTA5TSingleEnded_ACBench", benchContent);
        Assert.Contains(".include \"OTA5TSingleEnded.sp\"", benchContent);
        Assert.Contains("XDUT", benchContent);
        Assert.Contains(".control", benchContent);

        // Step 2: Create mock results (as if simulation ran)
        // AC bench only measures AC metrics - no power measurement
        var resultsPath = Path.Combine(_outputDir, "OTA5TSingleEnded_ACBench_results.json");
        var mockResults = new
        {
            circuit = "OTA5TSingleEnded",
            bench = "ACBench",
            measurements = new
            {
                gain = new
                {
                    metric = "PassbandGain",
                    value = 45.2,
                    unit = "dB",
                    node = "OUT",
                },
                gbw = new
                {
                    metric = "GainBandwidth",
                    value = 150e6,
                    unit = "Hz",
                    node = "OUT",
                },
                pm = new
                {
                    metric = "PhaseMargin",
                    value = 65.3,
                    unit = "deg",
                    node = "OUT",
                },
            },
        };
        await File.WriteAllTextAsync(
            resultsPath,
            JsonSerializer.Serialize(
                mockResults,
                new JsonSerializerOptions { WriteIndented = true }
            )
        );

        // Step 3: Verify
        var verifyResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            "--acir",
            acirPath,
            "--results",
            resultsPath
        );

        CliIntegrationTestHelper.AssertSuccess(verifyResult, "verify command failed");
        Assert.Contains("3/3 constraints satisfied", verifyResult.Stdout);
        Assert.Contains("Note: 1 constraint (c_pwr) measured by DCBench.", verifyResult.Stdout);
        Assert.Contains("Note: 1 constraint (c_swing) measured by TranBench.", verifyResult.Stdout);
    }

    [Fact]
    public async Task Verify_MissingACIR_ReturnsError()
    {
        var nonExistentPath = Path.Combine(_outputDir, "nonexistent.cir");
        var resultsPath = Path.Combine(
            _repoRoot,
            "tests/golden/results/ota/OTA5TSingleEnded_ACBench_results.json"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            "--acir",
            nonExistentPath,
            "--results",
            resultsPath
        );

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
            "verify",
            "--acir",
            acirPath,
            "--results",
            nonExistentResults
        );

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
            "emit",
            acirPath,
            "--out",
            _outputDir
        );

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
            "emit",
            acirPath,
            "--out",
            _outputDir
        );

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
            "emit",
            acirPath,
            "--out",
            _outputDir
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("EMIT-007", result.Stdout);
        Assert.Contains("missing required size", result.Stdout);
    }

    [Fact]
    public async Task Emit_InvalidCircuit_MLLevel_ReturnsExitCode2()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/ml_level.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir
        );

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
            "erc",
            acirPath
        );

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
            "erc",
            acirPath
        );

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
            "erc",
            acirPath
        );

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
            "erc",
            acirPath
        );

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
            "erc",
            acirPath
        );

        CliIntegrationTestHelper.AssertSuccess(
            resultWithoutFlag,
            "erc should pass without --require-pdk"
        );

        // With --require-pdk, should fail because CSAmpResistive uses generic nmos/pmos
        var resultWithFlag = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath,
            "--require-pdk"
        );

        Assert.NotEqual(0, resultWithFlag.ExitCode);
        Assert.Contains("ERC-005", resultWithFlag.Stdout);
    }

    [Fact]
    public async Task Erc_Usage_ShowsHelp()
    {
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "erc without args should show usage");
        Assert.Contains("Usage: erc", result.Stdout);
        Assert.Contains("--require-pdk", result.Stdout);
        Assert.Contains("--json", result.Stdout);
    }

    [Fact]
    public async Task Erc_JsonOutput_ReturnsValidJson()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath,
            "--json"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "erc --json should succeed");

        using var json = CliIntegrationTestHelper.ParseJsonFromOutput(result.Stdout);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task Erc_JsonOutput_WithErrors_ReturnsValidJson()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/floating_gate.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath,
            "--json"
        );

        Assert.Equal(1, result.ExitCode);

        using var json = CliIntegrationTestHelper.ParseJsonFromOutput(result.Stdout);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("exitCode").GetInt32());

        var errors = json.RootElement.GetProperty("errors").EnumerateArray().ToList();
        Assert.Contains(errors, e => e.GetProperty("code").GetString() == "ERC-001");
    }

    [Fact]
    public async Task Erc_PassiveShort_ReturnsERC007()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/passive_short.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERC-007", result.Stdout);
        Assert.Contains("bridges supply rails", result.Stdout);
    }

    // ML-level ERC tests (same topology checks work on unsized circuits)

    [Fact]
    public async Task Erc_ML_ValidCircuit_ReturnsSuccess()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/ota/OTA5TSingleEnded_unsized.ml.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath
        );

        CliIntegrationTestHelper.AssertSuccess(result, "erc command failed on valid ML circuit");
        Assert.Contains("ERC passed", result.Stdout);
    }

    [Fact]
    public async Task Erc_ML_FloatingGate_ReturnsExitCode1()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/floating_gate.ml.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERC-001", result.Stdout);
        Assert.Contains("Floating gate", result.Stdout);
    }

    [Fact]
    public async Task Erc_ML_MissingGateBinding_ReturnsExitCode1()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/missing_gate.ml.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERC-001", result.Stdout);
        Assert.Contains("Missing gate binding", result.Stdout);
    }

    [Fact]
    public async Task Erc_ML_VddGndShort_ReturnsExitCode1()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/vdd_gnd_short.ml.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERC-002", result.Stdout);
        Assert.Contains("VDD-GND short", result.Stdout);
    }

    [Fact]
    public async Task Erc_ML_PassiveShort_ReturnsERC007()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/passive_short.ml.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath
        );

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ERC-007", result.Stdout);
        Assert.Contains("bridges supply rails", result.Stdout);
    }

    [Fact]
    public async Task Erc_ML_TelescopicCascodeSingleEnded_Attach_ReturnsSuccess()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/TelescopicCascodeSingleEnded_Attach_unsized.ml.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "erc",
            acirPath
        );

        CliIntegrationTestHelper.AssertSuccess(
            result,
            "erc command failed on valid ML hierarchical circuit"
        );
        Assert.Contains("ERC passed", result.Stdout);
    }

    [Fact]
    public async Task Emit_JsonOutput_ReturnsValidJson()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--json"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit --json should succeed");

        using var json = CliIntegrationTestHelper.ParseJsonFromOutput(result.Stdout);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.NotEmpty(json.RootElement.GetProperty("designPaths").EnumerateArray());
    }

    [Fact]
    public async Task Emit_JsonOutput_WithErrors_ReturnsValidJson()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/missing_terminal.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--json"
        );

        Assert.Equal(2, result.ExitCode);

        using var json = CliIntegrationTestHelper.ParseJsonFromOutput(result.Stdout);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Emit_CSAmp_DCSwept_GeneratesTestbenchWithSweep()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/cs/CSAmpResistive_DCSwept.el.cir"
        );
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Emitted 1 design(s) and 1 testbench(es)", result.Stdout);

        var benchPath = Path.Combine(_outputDir, "CSAmpResistive_DCSwept_DCBench.sp");
        Assert.True(File.Exists(benchPath), "DC testbench not found");

        var content = await File.ReadAllTextAsync(benchPath);
        Assert.Contains("while bias_val <= bias_stop", content);
        Assert.Contains("alter VIN DC=$&bias_val", content);
        Assert.Contains("let out_dc = v(", content);
        Assert.Contains("echo CASCODE_POINT", content);

        AssertApproximatelyEqual(0.3, ExtractLetValue(content, "bias_start"));
        AssertApproximatelyEqual(1.5, ExtractLetValue(content, "bias_stop"));
        AssertApproximatelyEqual(0.1, ExtractLetValue(content, "bias_step"));
    }

    [Fact]
    public async Task Emit_OTA_DCSwept_GeneratesTestbenchWithICMRSweep()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/ota/OTA5TSingleEnded_DCSwept.el.cir"
        );
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5TSingleEnded_DCSwept_DCBench.sp");
        Assert.True(File.Exists(benchPath), "DC testbench not found");

        var content = await File.ReadAllTextAsync(benchPath);
        Assert.Contains("while cm_val <= cm_stop", content);
        Assert.Contains("alter VIN_CM DC=$&cm_val", content);
        Assert.Contains("EIN_N", content); // VCVS ties IN_N to IN_P for true common-mode

        AssertApproximatelyEqual(0.4, ExtractLetValue(content, "cm_start"));
        AssertApproximatelyEqual(1.4, ExtractLetValue(content, "cm_stop"));
        AssertApproximatelyEqual(0.1, ExtractLetValue(content, "cm_step"));
    }

    [Fact]
    public async Task Verify_CSAmp_DCSwept_WithPassingResults_ReturnsSuccess()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/cs/CSAmpResistive_DCSwept.el.cir"
        );
        var resultsPath = Path.Combine(
            _repoRoot,
            "tests/golden/results/cs/CSAmpResistive_DCSwept_DCBench_results.json"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            "--acir",
            acirPath,
            "--results",
            resultsPath
        );

        CliIntegrationTestHelper.AssertSuccess(result, "verify command failed");
        Assert.Contains("constraints satisfied", result.Stdout);
    }

    [Fact]
    public async Task Emit_InvalidAutoAtEL_ReturnsExitCode2()
    {
        // Test that [Auto] at EL level is rejected
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/invalid/auto_sweep_el.el.cir");
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir
        );

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("EMIT-006", result.Stdout);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_CSAmp_DCSwept_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/cs/CSAmpResistive_DCSwept.el.cir"
        );

        // Emit SPICE files
        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "CSAmpResistive_DCSwept_DCBench.sp");
        Assert.True(File.Exists(benchPath), "DC testbench not found");

        // Verify ngspice can simulate the generated SPICE file
        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_OTA_DCSwept_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/ota/OTA5TSingleEnded_DCSwept.el.cir"
        );

        // Emit SPICE files
        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5TSingleEnded_DCSwept_DCBench.sp");
        Assert.True(File.Exists(benchPath), "DC testbench not found");

        // Verify ngspice can simulate the generated SPICE file
        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_CommonSourceAmp_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/cs/CommonSourceAmp.el.cir");

        // Emit SPICE files
        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "CommonSourceAmp_ACBench.sp");
        Assert.True(File.Exists(benchPath), "AC testbench not found");

        // Verify ngspice can simulate the generated SPICE file
        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_PdkVariants_UseSky130ModelsAndSimulate()
    {
        var scanResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            _cascodeHome,
            "pdk",
            "scan",
            "tests/fixtures/pdk/sky130"
        );
        CliIntegrationTestHelper.AssertSuccess(scanResult, "pdk scan failed");

        var cases = new (string AcirRel, string BenchFile)[]
        {
            ("tests/golden/acir/cs/CommonSourceAmp_Pdk.el.cir", "CommonSourceAmp_Pdk_ACBench.sp"),
            (
                "tests/golden/acir/ota/OTA5TSingleEnded_Pdk.el.cir",
                "OTA5TSingleEnded_Pdk_ACBench.sp"
            ),
            (
                "tests/golden/acir/hierarchy/OTA5T_Hierarchical_Attach_Pdk.el.cir",
                "OTA5T_Hierarchical_Attach_Pdk_ACBench.sp"
            ),
        };

        var libPattern = Sky130LibIncludePattern();

        foreach (var (acirRel, benchFile) in cases)
        {
            var outputDir = Path.Combine(_outputDir, "pdk-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(outputDir);
            var acirPath = Path.Combine(_repoRoot, acirRel);

            var emitResult = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(30),
                _cascodeHome,
                "--workspace",
                "tests/fixtures/pdk/sky130",
                "emit",
                acirPath,
                "--out",
                outputDir,
                "--backend",
                "ngspice"
            );

            CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

            var benchPath = Path.Combine(outputDir, benchFile);
            Assert.True(File.Exists(benchPath), "PDK testbench not found");

            var content = await File.ReadAllTextAsync(benchPath);
            Assert.Matches(libPattern, content);

            var ngspiceResult = await RunNgspiceAsync(benchPath);
            Assert.True(
                ngspiceResult.Success,
                $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
            );
        }
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_OTA_ACBench_PrintsNumericResultValues()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TSingleEnded.el.cir");

        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5TSingleEnded_ACBench.sp");
        Assert.True(File.Exists(benchPath), "AC testbench not found");

        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );

        Assert.Matches(CreateNumericResultRegex("PassbandGain"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("GainBandwidth"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("PhaseMargin"), ngspiceResult.Stdout);
    }

    [Fact]
    public async Task Emit_FD_OTA_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TFullyDiff.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Design netlist:", result.Stdout);
        Assert.Contains("Testbench:", result.Stdout);
        Assert.Contains("Emitted 1 design(s) and 3 testbench(es)", result.Stdout);

        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TFullyDiff.sp")),
            "Design netlist not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TFullyDiff_ACBench.sp")),
            "AC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TFullyDiff_DCBench.sp")),
            "DC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5TFullyDiff_TranBench.sp")),
            "Tran testbench not found"
        );
    }

    [Fact]
    public async Task Emit_FD_OTA_DCBench_GeneratesSplitDifferentialLoads()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TFullyDiff.el.cir");

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");

        var dcBenchPath = Path.Combine(_outputDir, "OTA5TFullyDiff_DCBench.sp");
        Assert.True(File.Exists(dcBenchPath), "DC testbench not found");

        var content = await File.ReadAllTextAsync(dcBenchPath);

        // The circuit has "load OUT C=1pF" - for differential bench this should be
        // split into halved values (0.5pF) on both _P and _N outputs
        Assert.Contains("COUT_P_load OUT_P 0 0.5p", content);
        Assert.Contains("COUT_N_load OUT_N 0 0.5p", content);
    }

    [Fact]
    public async Task Emit_FD_OTA_DCSwept_GeneratesTestbenchWithICMRSweep()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/ota/OTA5TFullyDiff_DCSwept.el.cir"
        );
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5TFullyDiff_DCSwept_DCBench.sp");
        Assert.True(File.Exists(benchPath), "DC testbench not found");

        var content = await File.ReadAllTextAsync(benchPath);
        Assert.Contains("while cm_val <= cm_stop", content);
        Assert.Contains("alter VIN_CM DC=$&cm_val", content);
        Assert.Contains("EIN_N", content); // VCVS ties IN_N to IN_P for true common-mode
    }

    [Fact]
    public async Task Verify_FD_OTA_WithPassingResults_ReturnsSuccess()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TFullyDiff.el.cir");
        var resultsPath = Path.Combine(
            _repoRoot,
            "tests/golden/results/ota/OTA5TFullyDiff_ACBench_results.json"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "verify",
            "--acir",
            acirPath,
            "--results",
            resultsPath
        );

        CliIntegrationTestHelper.AssertSuccess(result, "verify command failed");
        Assert.Contains("Constraint Compliance Report", result.Stdout);
        Assert.Contains("3/3 constraints satisfied", result.Stdout);
        Assert.Contains("Note: 1 constraint (c_pwr) measured by DCBench.", result.Stdout);
        Assert.Contains("Note: 1 constraint (c_swing) measured by TranBench.", result.Stdout);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_FD_OTA_ACBench_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(_repoRoot, "tests/golden/acir/ota/OTA5TFullyDiff.el.cir");

        // Emit SPICE files
        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5TFullyDiff_ACBench.sp");
        Assert.True(File.Exists(benchPath), "AC testbench not found");

        // Verify ngspice can simulate the generated SPICE file
        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );

        // Verify RESULT lines contain valid numeric values (not empty)
        Assert.Matches(CreateNumericResultRegex("PassbandGain"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("GainBandwidth"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("PhaseMargin"), ngspiceResult.Stdout);
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_FD_OTA_DCSwept_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/ota/OTA5TFullyDiff_DCSwept.el.cir"
        );

        // Emit SPICE files
        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5TFullyDiff_DCSwept_DCBench.sp");
        Assert.True(File.Exists(benchPath), "DC testbench not found");

        // Verify ngspice can simulate the generated SPICE file
        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );
    }

    [Fact]
    public async Task Emit_OTA5T_Hierarchical_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Design netlist:", result.Stdout);
        Assert.Contains("Testbench:", result.Stdout);

        // Should emit 4 designs (all circuits including inline) and 2 testbenches
        Assert.Contains("Emitted 2 design(s) and 2 testbench(es)", result.Stdout);

        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5T_Hierarchical.sp")),
            "Design netlist not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "CurrentMirror.sp")),
            "CurrentMirror subcircuit not found"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "DiffPair.sp")),
            "DiffPair should not be emitted as a standalone subcircuit when marked inline"
        );

        var designText = File.ReadAllText(Path.Combine(_outputDir, "OTA5T_Hierarchical.sp"));
        Assert.Contains("* Inline expansion of dp : DiffPair", designText);
        Assert.Contains("Mdp__M_N", designText);
        Assert.Contains("Mdp__M_P", designText);
        Assert.Contains("Mdp__M_TAIL", designText);
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5T_Hierarchical_ACBench.sp")),
            "AC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5T_Hierarchical_DCBench.sp")),
            "DC testbench not found"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_OTA5T_Hierarchical_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical.el.cir"
        );

        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5T_Hierarchical_ACBench.sp");
        Assert.True(File.Exists(benchPath), "AC testbench not found");

        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );

        Assert.Matches(CreateNumericResultRegex("PassbandGain"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("GainBandwidth"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("PhaseMargin"), ngspiceResult.Stdout);
    }

    [Fact]
    public async Task Emit_OTA5T_Hierarchical_Attach_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical_Attach.el.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Design netlist:", result.Stdout);
        Assert.Contains("Testbench:", result.Stdout);

        // Should emit 2 designs (top-level + 1 child circuit) and 2 testbenches
        Assert.Contains("Emitted 2 design(s) and 2 testbench(es)", result.Stdout);

        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5T_Hierarchical_Attach.sp")),
            "Design netlist not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "CurrentMirror.sp")),
            "CurrentMirror subcircuit not found"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "DiffPair.sp")),
            "DiffPair should not be emitted as a standalone subcircuit when marked inline"
        );

        var designText = File.ReadAllText(Path.Combine(_outputDir, "OTA5T_Hierarchical_Attach.sp"));
        Assert.Contains("* Inline expansion of dp : DiffPair", designText);
        Assert.Contains("Mdp__M_N", designText);
        Assert.Contains("Mdp__M_P", designText);
        Assert.Contains("Mdp__M_TAIL", designText);
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5T_Hierarchical_Attach_ACBench.sp")),
            "AC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "OTA5T_Hierarchical_Attach_DCBench.sp")),
            "DC testbench not found"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_OTA5T_Hierarchical_Attach_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/OTA5T_Hierarchical_Attach.el.cir"
        );

        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "OTA5T_Hierarchical_Attach_ACBench.sp");
        Assert.True(File.Exists(benchPath), "AC testbench not found");

        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );

        Assert.Matches(CreateNumericResultRegex("PassbandGain"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("GainBandwidth"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("PhaseMargin"), ngspiceResult.Stdout);
    }

    [Fact]
    public async Task Emit_TelescopicCascodeFullyDiff_Attach_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/TelescopicCascodeFullyDiff_Attach.el.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Design netlist:", result.Stdout);
        Assert.Contains("Testbench:", result.Stdout);

        // Should emit 1 design (top-level only) and 2 testbenches
        Assert.Contains("Emitted 1 design(s) and 2 testbench(es)", result.Stdout);

        Assert.True(
            File.Exists(Path.Combine(_outputDir, "TelescopicCascodeFullyDiff_Attach.sp")),
            "Design netlist not found"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "NLoadPair.sp")),
            "NLoadPair should not be emitted as a standalone subcircuit when marked inline"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "PLoadPair.sp")),
            "PLoadPair should not be emitted as a standalone subcircuit when marked inline"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "DiffPair.sp")),
            "DiffPair should not be emitted as a standalone subcircuit when marked inline"
        );

        var designText = File.ReadAllText(
            Path.Combine(_outputDir, "TelescopicCascodeFullyDiff_Attach.sp")
        );
        Assert.Contains("* Inline expansion of dp : DiffPair", designText);
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "TelescopicCascodeFullyDiff_Attach_ACBench.sp")),
            "AC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "TelescopicCascodeFullyDiff_Attach_DCBench.sp")),
            "DC testbench not found"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_TelescopicCascodeFullyDiff_Attach_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/TelescopicCascodeFullyDiff_Attach.el.cir"
        );

        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "TelescopicCascodeFullyDiff_Attach_ACBench.sp");
        Assert.True(File.Exists(benchPath), "AC testbench not found");

        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );

        Assert.Matches(CreateNumericResultRegex("PassbandGain"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("GainBandwidth"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("PhaseMargin"), ngspiceResult.Stdout);
    }

    [Fact]
    public async Task Emit_TelescopicCascodeSingleEnded_Attach_GeneratesDesignAndTestbench()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/TelescopicCascodeSingleEnded_Attach.el.cir"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(result, "emit command failed");
        Assert.Contains("Design netlist:", result.Stdout);
        Assert.Contains("Testbench:", result.Stdout);

        // Should emit 1 design (top-level only, all children inline) and 2 testbenches
        Assert.Contains("Emitted 1 design(s) and 2 testbench(es)", result.Stdout);

        Assert.True(
            File.Exists(Path.Combine(_outputDir, "TelescopicCascodeSingleEnded_Attach.sp")),
            "Design netlist not found"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "LoadPair_N.sp")),
            "LoadPair_N should not be emitted as a standalone subcircuit when marked inline"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "CurrentMirror_P.sp")),
            "CurrentMirror_P should not be emitted as a standalone subcircuit when marked inline"
        );
        Assert.False(
            File.Exists(Path.Combine(_outputDir, "DiffPair.sp")),
            "DiffPair should not be emitted as a standalone subcircuit when marked inline"
        );

        var designText = File.ReadAllText(
            Path.Combine(_outputDir, "TelescopicCascodeSingleEnded_Attach.sp")
        );
        Assert.Contains("* Inline expansion of dp : DiffPair", designText);
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "TelescopicCascodeSingleEnded_Attach_ACBench.sp")),
            "AC testbench not found"
        );
        Assert.True(
            File.Exists(Path.Combine(_outputDir, "TelescopicCascodeSingleEnded_Attach_DCBench.sp")),
            "DC testbench not found"
        );
    }

    [Fact]
    [Trait("Category", "Simulation")]
    public async Task Emit_TelescopicCascodeSingleEnded_Attach_SpiceSimulatesSuccessfully()
    {
        var acirPath = Path.Combine(
            _repoRoot,
            "tests/golden/acir/hierarchy/TelescopicCascodeSingleEnded_Attach.el.cir"
        );

        var emitResult = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromSeconds(30),
            _cascodeHome,
            "emit",
            acirPath,
            "--out",
            _outputDir,
            "--backend",
            "ngspice"
        );

        CliIntegrationTestHelper.AssertSuccess(emitResult, "emit command failed");

        var benchPath = Path.Combine(_outputDir, "TelescopicCascodeSingleEnded_Attach_ACBench.sp");
        Assert.True(File.Exists(benchPath), "AC testbench not found");

        var ngspiceResult = await RunNgspiceAsync(benchPath);
        Assert.True(
            ngspiceResult.Success,
            $"ngspice simulation failed: {ngspiceResult.ErrorMessage}\nstdout:\n{ngspiceResult.Stdout}\nstderr:\n{ngspiceResult.Stderr}"
        );

        Assert.Matches(CreateNumericResultRegex("PassbandGain"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("GainBandwidth"), ngspiceResult.Stdout);
        Assert.Matches(CreateNumericResultRegex("PhaseMargin"), ngspiceResult.Stdout);
    }

    /// <summary>
    /// Creates a regex pattern for matching RESULT lines with numeric values.
    /// </summary>
    /// <param name="metricName">The metric name (e.g., "PassbandGain", "GainBandwidth").</param>
    /// <returns>A Regex pattern that matches RESULT lines for the specified metric with a floating-point value.</returns>
    private static Regex CreateNumericResultRegex(string metricName)
    {
        return new Regex(
            $@"RESULT:\s*{Regex.Escape(metricName)}\s*=\s*{FloatingPointPattern}",
            RegexOptions.Compiled
        );
    }

    private static double ExtractLetValue(string content, string variableName)
    {
        var match = Regex.Match(
            content,
            $@"(?m)^\s*let\s+{Regex.Escape(variableName)}\s*=\s*(?<value>{FloatingPointPattern})\s*$"
        );
        Assert.True(match.Success, $"Expected 'let {variableName} = <number>' in emitted bench.");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static void AssertApproximatelyEqual(
        double expected,
        double actual,
        double tolerance = 1e-6
    )
    {
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }

    /// <summary>
    /// Runs ngspice in batch mode on a SPICE netlist file.
    /// </summary>
    /// <param name="spiceFile">Path to the SPICE netlist file.</param>
    /// <returns>Result indicating success or failure with error message.</returns>
    private static async Task<(
        bool Success,
        string Stdout,
        string Stderr,
        string ErrorMessage
    )> RunNgspiceAsync(string spiceFile)
    {
        if (!File.Exists(spiceFile))
        {
            return (false, string.Empty, string.Empty, $"SPICE file not found: {spiceFile}");
        }

        // Check if ngspice is available
        try
        {
            using var checkProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ngspice",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            checkProcess.Start();
            await checkProcess.WaitForExitAsync();

            if (checkProcess.ExitCode != 0)
            {
                return (
                    false,
                    string.Empty,
                    string.Empty,
                    "ngspice not found or not working. Install with: conda env create -f tests/simulation/environment.yml"
                );
            }
        }
        catch (Exception ex)
            when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException)
        {
            return (
                false,
                string.Empty,
                string.Empty,
                $"ngspice not found in PATH: {ex.Message}. Install with: conda env create -f tests/simulation/environment.yml"
            );
        }

        // Run ngspice in batch mode (-b flag)
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ngspice",
                    Arguments = $"-b \"{spiceFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory =
                        Path.GetDirectoryName(spiceFile) ?? Directory.GetCurrentDirectory(),
                },
            };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                var stdoutTimedOut = await stdoutTask;
                var stderrTimedOut = await stderrTask;
                return (
                    false,
                    stdoutTimedOut,
                    stderrTimedOut,
                    "ngspice simulation timed out after 60 seconds"
                );
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                return (true, stdout, stderr, string.Empty);
            }

            return (false, stdout, stderr, $"ngspice exited with code {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, string.Empty, $"Failed to run ngspice: {ex.Message}");
        }
    }

    [GeneratedRegex(@"\.lib\s+""[^""]*sky130\.lib\.spice""\s+tt", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Sky130LibIncludePattern();
}
