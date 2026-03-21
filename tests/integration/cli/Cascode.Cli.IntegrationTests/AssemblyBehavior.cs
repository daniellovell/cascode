using Xunit;

// These tests launch real CLI and simulator subprocesses; serial execution avoids
// spurious timeout failures from resource contention on slower machines.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
