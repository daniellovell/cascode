using System.Threading.Tasks;
using Xunit;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

// Disable test parallelization to prevent race conditions when modifying global environment variables (e.g., CASCODE_HOME) and shared file system state.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InteractiveCliCollection : ICollectionFixture<InteractiveCliFixture>
{
    public const string Name = "Interactive CLI Collection";
}

public sealed class InteractiveCliFixture : IAsyncLifetime
{
    public InteractiveCliFixture()
    {
        RepoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
    }

    public string RepoRoot { get; }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;
}
