using Cascode.Cli.Output;
using Xunit;

namespace Cascode.Cli.Tests.Output;

public sealed class SpectreCliOutputTests
{
    [Fact]
    public void RunWithProgress_EscapesMarkupInStatusText()
    {
        var output = new SpectreCliOutput();

        // Markup characters that would cause issues if not escaped
        var result = output.RunWithProgress(
            "[red]initial[/]",
            progress =>
            {
                progress("[green]step[/] with [[brackets]]");
                return 42;
            }
        );

        Assert.Equal(42, result);
    }
}
