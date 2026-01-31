using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Cascode.Cli.Services;

public sealed record BenchRunTimingEntry(string Name, TimeSpan Elapsed);

public sealed record BenchBenchTiming(
    string CircuitName,
    string BenchName,
    TimeSpan Simulation,
    TimeSpan ParseOutputs,
    TimeSpan EvaluateMeasurements,
    TimeSpan WriteArtifacts
)
{
    public TimeSpan Total => Simulation + ParseOutputs + EvaluateMeasurements + WriteArtifacts;
}

public sealed record BenchRunTimingReport(
    TimeSpan Total,
    IReadOnlyList<BenchRunTimingEntry> Steps,
    IReadOnlyList<BenchBenchTiming> Benches
);

internal sealed class BenchRunTimingCollector
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly List<BenchRunTimingEntry> _steps = new();
    private readonly List<BenchBenchTiming> _benches = new();

    public StepScope Step(string name)
    {
        return new StepScope(name, _steps);
    }

    public void AddBench(BenchBenchTiming timing)
    {
        _benches.Add(timing);
    }

    public BenchRunTimingReport Build()
    {
        _total.Stop();
        return new BenchRunTimingReport(_total.Elapsed, _steps.ToArray(), _benches.ToArray());
    }

    internal sealed class StepScope : IDisposable
    {
        private readonly string _name;
        private readonly Stopwatch _sw;
        private readonly List<BenchRunTimingEntry> _steps;
        private bool _stopped;

        public StepScope(string name, List<BenchRunTimingEntry> steps)
        {
            _name = name;
            _steps = steps;
            _sw = Stopwatch.StartNew();
        }

        public TimeSpan Elapsed => _sw.Elapsed;

        public void Stop()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _sw.Stop();
            _steps.Add(new BenchRunTimingEntry(_name, _sw.Elapsed));
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
