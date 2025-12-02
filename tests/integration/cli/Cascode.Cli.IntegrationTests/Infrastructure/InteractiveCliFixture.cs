using System.Threading.Tasks;
using Xunit;

namespace Cascode.Cli.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
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
