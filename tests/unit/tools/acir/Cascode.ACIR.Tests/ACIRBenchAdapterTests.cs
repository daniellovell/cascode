using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Cascode.ACIR;
using Cascode.Bench;

namespace Cascode.ACIR.Tests
{
    public class ACIRBenchAdapterTests
    {
        [Fact]
        public void ToTestbenchContext_ParsesSupplyValueAndSetsVcm()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "1.8V" }
                    }
                }
            };
            var bench = new BenchConfig { Name = "TestBench" };

            var context = ACIRBenchAdapter.ToTestbenchContext(circuit, bench, BenchBackendType.Ngspice, "out");

            Assert.True(context.Args.ContainsKey("vcm"));
            Assert.Equal(0.9, context.Args["vcm"]);
        }

        [Fact]
        public void ToTestbenchContext_ThrowsOnInvalidSupplyValue()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "INVALID" }
                    }
                }
            };
            var bench = new BenchConfig { Name = "TestBench" };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ACIRBenchAdapter.ToTestbenchContext(circuit, bench, BenchBackendType.Ngspice, "out"));

            Assert.Contains("Unable to parse supply value", ex.Message);
        }

        [Fact]
        public void ToTestbenchContext_HandlesNullSuppliesGracefully()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Supplies = null!
                }
            };
            var bench = new BenchConfig { Name = "TestBench" };

            var context = ACIRBenchAdapter.ToTestbenchContext(circuit, bench, BenchBackendType.Ngspice, "out");

            Assert.Equal(0.9, context.Args["vcm"]);
        }

        [Fact]
        public void ToTestbenchContext_HandlesEmptySuppliesGracefully()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>()
                }
            };
            var bench = new BenchConfig { Name = "TestBench" };

            var context = ACIRBenchAdapter.ToTestbenchContext(circuit, bench, BenchBackendType.Ngspice, "out");

            Assert.Equal(0.9, context.Args["vcm"]);
        }
    }

    public class BuildHarnessSuppliesAndBiasesTests
    {
        [Fact]
        public void ReturnsEmptyList_WhenNoHarness()
        {
            var circuit = new Circuit { Name = "Test" };

            var result = ACIRBenchAdapter.BuildHarnessSuppliesAndBiases(circuit);

            Assert.Empty(result);
        }

        [Fact]
        public void IncludesSupplies()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "1.8V" },
                        new() { Net = "VSS", Value = "0V" }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessSuppliesAndBiases(circuit);

            Assert.Equal(2, result.Count);
            var first = (Dictionary<string, object>)result[0];
            Assert.Equal("VDD", first["net"]);
            Assert.Equal("1.8V", first["value"]);
        }

        [Fact]
        public void IncludesBiases()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Biases = new List<BiasValue>
                    {
                        new() { Net = "VBIAS", Value = "0.6V" }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessSuppliesAndBiases(circuit);

            Assert.Single(result);
            var first = (Dictionary<string, object>)result[0];
            Assert.Equal("VBIAS", first["net"]);
            Assert.Equal("0.6V", first["value"]);
        }

        [Fact]
        public void CombinesSuppliesAndBiases()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "1.8V" }
                    },
                    Biases = new List<BiasValue>
                    {
                        new() { Net = "VBIAS", Value = "0.6V" }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessSuppliesAndBiases(circuit);

            Assert.Equal(2, result.Count);
        }
    }

    public class BuildHarnessLoadsTests
    {
        [Fact]
        public void ReturnsEmptyList_WhenNoHarness()
        {
            var circuit = new Circuit { Name = "Test" };

            var result = ACIRBenchAdapter.BuildHarnessLoads(circuit);

            Assert.Empty(result);
        }

        [Fact]
        public void IncludesLoadsWithCapacitance()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Loads = new List<LoadValue>
                    {
                        new() { Net = "OUT", C = "1p" }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessLoads(circuit);

            Assert.Single(result);
            var first = (Dictionary<string, object>)result[0];
            Assert.Equal("OUT", first["net"]);
            Assert.Equal("1p", first["c"]);
        }

        [Fact]
        public void SkipsLoadsWithoutCapacitance()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Loads = new List<LoadValue>
                    {
                        new() { Net = "OUT", C = null }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessLoads(circuit);

            Assert.Empty(result);
        }
    }

    public class DetermineOutNodeTests
    {
        [Fact]
        public void ReturnsOutPort_WhenPresent()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Ports = new List<PortDeclaration>
                {
                    new() { Name = "IN", Type = "analog" },
                    new() { Name = "OUT", Type = "analog" }
                }
            };

            var result = ACIRBenchAdapter.DetermineOutNode(circuit);

            Assert.Equal("OUT", result);
        }

        [Fact]
        public void ReturnsOutPort_CaseInsensitive()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Ports = new List<PortDeclaration>
                {
                    new() { Name = "out", Type = "analog" }
                }
            };

            var result = ACIRBenchAdapter.DetermineOutNode(circuit);

            Assert.Equal("out", result);
        }

        [Fact]
        public void ReturnsFirstPort_WhenNoOutPort()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Ports = new List<PortDeclaration>
                {
                    new() { Name = "IN", Type = "analog" },
                    new() { Name = "VOUT", Type = "analog" }
                }
            };

            var result = ACIRBenchAdapter.DetermineOutNode(circuit);

            Assert.Equal("IN", result);
        }

        [Fact]
        public void ReturnsFallback_WhenNoPorts()
        {
            var circuit = new Circuit { Name = "Test" };

            var result = ACIRBenchAdapter.DetermineOutNode(circuit);

            Assert.Equal("OUT", result);
        }
    }

    public class BuildPortListTests
    {
        [Fact]
        public void CombinesPortsSuppliesAndGrounds()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Ports = new List<PortDeclaration>
                {
                    new() { Name = "IN", Type = "analog" },
                    new() { Name = "OUT", Type = "analog" }
                },
                Supplies = new List<string> { "VDD" },
                Grounds = new List<string> { "VSS" }
            };

            var result = ACIRBenchAdapter.BuildPortList(circuit);

            Assert.Equal(4, result.Count);
            Assert.Equal(new[] { "IN", "OUT", "VDD", "VSS" }, result);
        }

        [Fact]
        public void ReturnsEmptyList_WhenEmpty()
        {
            var circuit = new Circuit { Name = "Test" };

            var result = ACIRBenchAdapter.BuildPortList(circuit);

            Assert.Empty(result);
        }
    }

    public class UsesGenericModelsTests
    {
        [Fact]
        public void ReturnsTrue_WhenNmosGeneric()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Fill = new FillBlock
                {
                    Devices = new List<DeviceDeclaration>
                    {
                        new() { Id = "M1", DeviceType = "nmos" }
                    }
                }
            };

            var result = ACIRBenchAdapter.UsesGenericModels(circuit);

            Assert.True(result);
        }

        [Fact]
        public void ReturnsTrue_WhenPmosGeneric()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Fill = new FillBlock
                {
                    Devices = new List<DeviceDeclaration>
                    {
                        new() { Id = "M1", DeviceType = "pmos" }
                    }
                }
            };

            var result = ACIRBenchAdapter.UsesGenericModels(circuit);

            Assert.True(result);
        }

        [Fact]
        public void ReturnsFalse_WhenPdkDeviceSpecified()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Fill = new FillBlock
                {
                    Devices = new List<DeviceDeclaration>
                    {
                        new() { Id = "M1", DeviceType = "nmos", PdkDevice = "sky130_fd_pr__nfet_01v8" }
                    }
                }
            };

            var result = ACIRBenchAdapter.UsesGenericModels(circuit);

            Assert.False(result);
        }

        [Fact]
        public void ReturnsFalse_WhenNoFill()
        {
            var circuit = new Circuit { Name = "Test" };

            var result = ACIRBenchAdapter.UsesGenericModels(circuit);

            Assert.False(result);
        }
    }

    public class BuildSweepDictionaryTests
    {
        [Fact]
        public void BuildSweepDictionary_ReturnsEmptyDictionary_WhenNoHarness()
        {
            var circuit = new Circuit { Name = "Test" };

            var result = ACIRBenchAdapter.BuildSweepDictionary(circuit);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildSweepDictionary_ReturnsEmptyDictionary_WhenNoSweeps()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock { Sweeps = new List<SweepCondition>() }
            };

            var result = ACIRBenchAdapter.BuildSweepDictionary(circuit);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildSweepDictionary_ParsesSweepWithExplicitStep()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Sweeps = new List<SweepCondition>
                    {
                        new() { Name = "InputDCBias", Start = "0.3V", Stop = "1.5V", Step = "100mV" }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildSweepDictionary(circuit);

            Assert.Single(result);
            Assert.True(result.ContainsKey("InputDCBias"));
            var sweepData = (Dictionary<string, object>)result["InputDCBias"]!;
            Assert.Equal(0.3, sweepData["start"]);
            Assert.Equal(1.5, sweepData["stop"]);
            Assert.Equal(0.1, sweepData["step"]);
        }

        [Fact]
        public void BuildSweepDictionary_ComputesAutoStep_WhenStepIsNull()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Sweeps = new List<SweepCondition>
                    {
                        new() { Name = "InputDCBias", Start = "0.3V", Stop = "1.5V", Step = null }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildSweepDictionary(circuit);

            var sweepData = (Dictionary<string, object>)result["InputDCBias"]!;
            // Auto step: (1.5 - 0.3) / 20 = 0.06, clamped to [0.01, 0.1]
            Assert.Equal(0.06, sweepData["step"]);
        }
    }

    public class SweepTemplateRenderingTests
    {
        [Fact]
        public void TemplateRendering_UsesPascalCaseSweepProperties()
        {
            // This test verifies that templates using PascalCase sweep properties
            // (e.g., sweep.InputDCBias.Start) render correctly.
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "1.8V" }
                    },
                    Sweeps = new List<SweepCondition>
                    {
                        new() { Name = "InputDCBias", Start = "0.3V", Stop = "1.5V", Step = "100mV" }
                    }
                }
            };

            // Build context and extract sweep data through the same pipeline as real templates
            var context = ACIRBenchAdapter.ToTestbenchContext(
                circuit,
                new BenchConfig { Name = "TestBench" },
                BenchBackendType.Spectre,
                "out");

            // Extract sweep data as ACIRTemplateHarness does
            var sweepDict = new Dictionary<string, object?>();
            foreach (var kvp in context.Args)
            {
                if (kvp.Key.StartsWith("sweep.", StringComparison.Ordinal))
                {
                    var conditionName = kvp.Key.Substring(6);
                    if (kvp.Value is Dictionary<string, object> sweepData)
                    {
                        sweepDict[conditionName] = new
                        {
                            Start = sweepData.TryGetValue("start", out var s) ? Convert.ToDouble(s) : 0.0,
                            Stop = sweepData.TryGetValue("stop", out var st) ? Convert.ToDouble(st) : 0.0,
                            Step = sweepData.TryGetValue("step", out var step) ? Convert.ToDouble(step) : (double?)null
                        };
                    }
                }
            }

            var scriptObj = new Scriban.Runtime.ScriptObject();
            foreach (var kvp in sweepDict)
            {
                scriptObj[kvp.Key] = kvp.Value;
            }

            // Create a test template using PascalCase properties
            var template = "start={{ sweep.InputDCBias.Start }} stop={{ sweep.InputDCBias.Stop }} step={{ sweep.InputDCBias.Step }}";
            var model = new { sweep = scriptObj };

            var rendered = Bench.TemplateRenderer.Render(template, model);

            Assert.Equal("start=0.3 stop=1.5 step=0.1", rendered);
        }
    }

    public class DeriveVoltageAndImpedanceTests
    {
        [Fact]
        public void ReturnsDefaults_WhenNoHarness()
        {
            var circuit = new Circuit { Name = "Test" };

            var result = ACIRBenchAdapter.DeriveVoltageAndImpedance(circuit);

            Assert.Equal(0.9, result.Vcm);
            Assert.Equal(0.9, result.BiasV);
            Assert.Equal(1e-12, result.LoadC);
            Assert.Equal(50.0, result.SourceOhms);
            Assert.Equal(1e9, result.RloadOhms);
        }

        [Fact]
        public void DerivesVcmFromSupply()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "1.8V" }
                    }
                }
            };

            var result = ACIRBenchAdapter.DeriveVoltageAndImpedance(circuit);

            Assert.Equal(0.9, result.Vcm);
            Assert.Equal(0.9, result.BiasV);
        }

        [Fact]
        public void DerivesLoadCapacitance()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Loads = new List<LoadValue>
                    {
                        new() { Net = "OUT", C = "10p" }
                    }
                }
            };

            var result = ACIRBenchAdapter.DeriveVoltageAndImpedance(circuit);

            Assert.Equal(10e-12, result.LoadC);
        }

        [Fact]
        public void DerivesSourceImpedance()
        {
            var circuit = new Circuit
            {
                Name = "Test",
                Harness = new HarnessBlock
                {
                    Sources = new List<SourceValue>
                    {
                        new() { Net = "IN", Z = "100" }
                    }
                }
            };

            var result = ACIRBenchAdapter.DeriveVoltageAndImpedance(circuit);

            Assert.Equal(100.0, result.SourceOhms);
        }

        [Fact]
        public void ThrowsOnInvalidSupplyValue()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "INVALID" }
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ACIRBenchAdapter.DeriveVoltageAndImpedance(circuit));

            Assert.Contains("Unable to parse supply value", ex.Message);
            Assert.Contains("TestCircuit", ex.Message);
        }

        [Fact]
        public void GenerateTestbench_ThrowsOnEmptyTemplate()
        {
            using var tempDir = Cascode.TestSupport.CascodeHome.CreateInTemp();
            var workspaceRoot = Path.Combine(tempDir.Path, "workspace");
            var benchesDir = Path.Combine(workspaceRoot, "lib", "std", "amp", "benches");
            Directory.CreateDirectory(benchesDir);

            // Create an empty template file
            var emptyTemplatePath = Path.Combine(benchesDir, "EmptyBench.ngspice.tpl");
            File.WriteAllText(emptyTemplatePath, "");

            var outputDir = Path.Combine(tempDir.Path, "output");
            Directory.CreateDirectory(outputDir);

            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Level = ACIRLevel.EL,
                Supplies = new List<string> { "VDD" },
                Grounds = new List<string> { "GND" },
                Ports = new List<PortDeclaration>
                {
                    new() { Name = "IN", Type = "analog" },
                    new() { Name = "OUT", Type = "analog" }
                },
                Harness = new HarnessBlock
                {
                    Supplies = new List<SupplyValue>
                    {
                        new() { Net = "VDD", Value = "1.8V" }
                    }
                }
            };

            var bench = new BenchConfig { Name = "EmptyBench" };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ACIRBenchAdapter.GenerateTestbench(circuit, bench, BenchBackendType.Ngspice, outputDir, workspaceRoot));

            Assert.Contains("Template file is empty", ex.Message);
            Assert.Contains("EmptyBench", ex.Message);
        }
    }
}
