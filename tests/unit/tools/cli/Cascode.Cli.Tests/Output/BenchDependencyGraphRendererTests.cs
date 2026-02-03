using System.Collections.Generic;
using Cascode.Cli.Output;
using Cascode.Language;
using Cascode.Language.BenchRuntime;
using Xunit;

namespace Cascode.Cli.Tests.Output;

public sealed class BenchDependencyGraphRendererTests
{
    [Fact]
    public void Render_WithEmptyGraph_DoesNothing()
    {
        var output = new TestCliOutput();
        var graph = CreateEmptyGraph();

        BenchDependencyGraphRenderer.Render(graph, output);

        Assert.Empty(output.ErrorLines);
    }

    [Fact]
    public void Render_WithSingleRoot_RendersPlainTree()
    {
        var output = new TestCliOutput();
        var circuit = CreateTestCircuit();
        var constraints = new List<NumericConstraint>
        {
            new()
            {
                Id = "c1",
                BenchBase = "transfer",
                Bench = "transfer",
                Metric = "Gain",
                Op = ">=",
                Value = "10",
                Unit = "dB",
            },
        };
        var benchByBindingAlias = new Dictionary<string, BenchDefinition>
        {
            ["transfer"] = CreateTestBench("TransferBench", "Gain"),
        };

        Assert.True(
            BenchDependencyGraph.TryBuild(
                circuit,
                constraints,
                benchByBindingAlias,
                new Dictionary<
                    string,
                    IReadOnlyDictionary<string, BenchBindingMeasurementExport>
                >(),
                out var graph,
                out _
            )
        );

        BenchDependencyGraphRenderer.Render(graph, output);

        Assert.Contains("Dependency Graph (1 measurements)", output.ErrorLines);
        Assert.Contains("transfer/Gain", output.ErrorLines);
    }

    private static BenchDependencyGraph CreateEmptyGraph()
    {
        var circuit = CreateTestCircuit();
        BenchDependencyGraph.TryBuild(
            circuit,
            new List<NumericConstraint>(),
            new Dictionary<string, BenchDefinition>(),
            new Dictionary<string, IReadOnlyDictionary<string, BenchBindingMeasurementExport>>(),
            out var graph,
            out _
        );
        return graph;
    }

    private static Circuit CreateTestCircuit()
    {
        return new Circuit { Name = "TestCircuit", Level = CascodeLevel.EL };
    }

    private static BenchDefinition CreateTestBench(string name, params string[] measurementNames)
    {
        var measurements = new List<MeasurementDefinition>();
        foreach (var m in measurementNames)
        {
            measurements.Add(
                new MeasurementDefinition
                {
                    Name = m,
                    Unit = "V",
                    Body = new List<BenchStatement> { new BenchReturn(new MeasurementNumber("0")) },
                }
            );
        }

        return new BenchDefinition { Name = name, Measurements = measurements };
    }

    private sealed class TestCliOutput : ICliOutput
    {
        public CliOutputMode Mode => CliOutputMode.Plain;
        public Spectre.Console.IAnsiConsole? Out => null;
        public Spectre.Console.IAnsiConsole? Err => null;

        public List<string> OutputLines { get; } = new();
        public List<string> ErrorLines { get; } = new();

        public void WriteLine(string text) => OutputLines.Add(text);

        public void WriteErrorLine(string text) => ErrorLines.Add(text);

        public void Info(string text) => OutputLines.Add(text);

        public void Success(string text) => OutputLines.Add(text);

        public void Warning(string text) => ErrorLines.Add(text);

        public void Error(string text) => ErrorLines.Add(text);

        public T RunWithProgress<T>(string initialStatus, System.Func<System.Action<string>, T> run)
        {
            ErrorLines.Add(initialStatus);
            return run(msg => ErrorLines.Add(msg));
        }

        public T RunWithMultiTaskProgress<T>(System.Func<IBenchProgressContext, T> run)
        {
            return run(new TestProgressContext());
        }

        private sealed class TestProgressContext : IBenchProgressContext
        {
            public IBenchTask AddTask(string description) => new TestTask();
        }

        private sealed class TestTask : IBenchTask
        {
            public void UpdateDescription(string description) { }

            public void StartTask() { }

            public void StopTask() { }
        }
    }
}
