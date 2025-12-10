using Cascode.Bench;
using Xunit;

namespace Cascode.Bench.Tests;

public class TemplateRendererTests
{
    [Fact]
    public void TemplateRenderer_PreprocessesScribanBlocks()
    {
        var model = new
        {
            spec = new { name = "demo" },
            items = new[] { "alpha", "beta" },
        };

        const string conditionalTemplate = "{{ if true }}value{{ end }}";
        const string loopTemplate = "{{ for item in items }}{{ item }}{{ end }}";

        var conditionalRender = TemplateRenderer.Render(conditionalTemplate, model);
        var loopRender = TemplateRenderer.Render(loopTemplate, model);

        Assert.Equal("value", conditionalRender);
        Assert.Equal("alphabeta", loopRender);
    }
}
