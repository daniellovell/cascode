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
}
