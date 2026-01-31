using Cascode.Cli.Output;
using Xunit;

namespace Cascode.Cli.Tests.Output;

public sealed class SpectreCliOutputTests
{
    [Fact]
    public void RunWithProgress_EscapesMarkupInStatusText()
    {
        var output = new SpectreCliOutput();
        output.RunWithProgress(
            "start",
            progress =>
            {
                progress("step");
                return 0;
            }
        );
    }
}
