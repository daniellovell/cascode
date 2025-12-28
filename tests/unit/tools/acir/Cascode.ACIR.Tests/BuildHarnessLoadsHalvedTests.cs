using System.Collections.Generic;
using Xunit;
using Cascode.ACIR;

namespace Cascode.ACIR.Tests
{
    public class BuildHarnessLoadsHalvedTests
    {
        [Fact]
        public void BuildHarnessLoads_IncludesHalvedValues()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Loads = new List<LoadValue>
                    {
                        new()
                        {
                            Net = "OUT",
                            Elements = new List<LoadElement>
                            {
                                new LoadElement("C", "1pF"),
                                new LoadElement("R", "10MOhm")
                            }
                        }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessLoads(circuit);

            Assert.Single(result);
            var first = (Dictionary<string, object>)result[0];
            Assert.Equal("OUT", first["net"]);

            var cs = (List<string>)first["cs"];
            Assert.Single(cs);
            Assert.Equal("1pF", cs[0]);

            var rs = (List<string>)first["rs"];
            Assert.Single(rs);
            Assert.Equal("10MOhm", rs[0]);

            var csHalf = (List<string>)first["cs_half"];
            Assert.Single(csHalf);
            Assert.Equal("500f", csHalf[0]);

            var rsHalf = (List<string>)first["rs_half"];
            Assert.Single(rsHalf);
            Assert.Equal("5M", rsHalf[0]);
        }

        [Fact]
        public void BuildHarnessLoads_HandlesMultipleElements()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Loads = new List<LoadValue>
                    {
                        new()
                        {
                            Net = "OUT",
                            Elements = new List<LoadElement>
                            {
                                new LoadElement("C", "1pF"),
                                new LoadElement("C", "500fF"),
                                new LoadElement("R", "1MOhm"),
                                new LoadElement("R", "10MOhm")
                            }
                        }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessLoads(circuit);

            Assert.Single(result);
            var first = (Dictionary<string, object>)result[0];

            var cs = (List<string>)first["cs"];
            Assert.Equal(2, cs.Count);
            Assert.Equal("1pF", cs[0]);
            Assert.Equal("500fF", cs[1]);

            var rs = (List<string>)first["rs"];
            Assert.Equal(2, rs.Count);
            Assert.Equal("1MOhm", rs[0]);
            Assert.Equal("10MOhm", rs[1]);

            var csHalf = (List<string>)first["cs_half"];
            Assert.Equal(2, csHalf.Count);
            Assert.Equal("500f", csHalf[0]);
            Assert.Equal("250f", csHalf[1]);

            var rsHalf = (List<string>)first["rs_half"];
            Assert.Equal(2, rsHalf.Count);
            Assert.Equal("500K", rsHalf[0]);
            Assert.Equal("5M", rsHalf[1]);
        }

        [Fact]
        public void BuildHarnessLoads_HandlesCapacitorOnly()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Loads = new List<LoadValue>
                    {
                        new()
                        {
                            Net = "OUT",
                            Elements = new List<LoadElement>
                            {
                                new LoadElement("C", "2pF")
                            }
                        }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessLoads(circuit);

            Assert.Single(result);
            var first = (Dictionary<string, object>)result[0];

            Assert.True(first.ContainsKey("cs"));
            Assert.True(first.ContainsKey("cs_half"));
            Assert.False(first.ContainsKey("rs"));
            Assert.False(first.ContainsKey("rs_half"));

            var csHalf = (List<string>)first["cs_half"];
            Assert.Single(csHalf);
            Assert.Equal("1p", csHalf[0]);
        }

        [Fact]
        public void BuildHarnessLoads_HandlesResistorOnly()
        {
            var circuit = new Circuit
            {
                Name = "TestCircuit",
                Harness = new HarnessBlock
                {
                    Loads = new List<LoadValue>
                    {
                        new()
                        {
                            Net = "OUT",
                            Elements = new List<LoadElement>
                            {
                                new LoadElement("R", "100Ohm")
                            }
                        }
                    }
                }
            };

            var result = ACIRBenchAdapter.BuildHarnessLoads(circuit);

            Assert.Single(result);
            var first = (Dictionary<string, object>)result[0];

            Assert.False(first.ContainsKey("cs"));
            Assert.False(first.ContainsKey("cs_half"));
            Assert.True(first.ContainsKey("rs"));
            Assert.True(first.ContainsKey("rs_half"));

            var rsHalf = (List<string>)first["rs_half"];
            Assert.Single(rsHalf);
            Assert.Equal("50", rsHalf[0]);
        }
    }
}

