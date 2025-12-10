using System;
using System.Collections.Generic;
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
    }
}
