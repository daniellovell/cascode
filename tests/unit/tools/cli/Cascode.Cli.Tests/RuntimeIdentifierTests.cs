using System;
using Cascode.Cli.Services;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class RuntimeIdentifierTests
{
    [Fact]
    public void ResolveCurrentRid_NormalizesSupportedOverride()
    {
        var rid = RuntimeIdentifier.ResolveCurrentRid("LiNuX-ArM64");

        Assert.Equal("linux-arm64", rid);
    }

    [Fact]
    public void ResolveCurrentRid_ThrowsForInvalidOverride()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RuntimeIdentifier.ResolveCurrentRid("linux-ppc64")
        );

        Assert.Contains("CASCODE_RUNTIME_RID", ex.Message);
        Assert.Contains("linux-ppc64", ex.Message);
    }
}
