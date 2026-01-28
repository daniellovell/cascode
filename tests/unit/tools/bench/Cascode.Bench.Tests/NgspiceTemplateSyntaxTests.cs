using System.Linq;
using System.Text.RegularExpressions;
using Cascode.Bench;
using Xunit;

namespace Cascode.Bench.Tests;

/// <summary>
/// Architecture tests that validate ngspice template syntax to prevent common errors.
/// </summary>
public partial class NgspiceTemplateSyntaxTests
{
    [Fact]
    public void NgspiceTemplates_ShouldNotUse_MeasDcParamWithVectors()
    {
        // This test prevents the bug where `meas dc ... param='v(...)*i(...)'` was used
        // inside .control blocks. The param= form cannot contain simulation vectors like
        // v() or i() - those must use `let` statements instead.

        var ngspiceTemplates = LoadNgspiceTemplates();
        Assert.NotEmpty(ngspiceTemplates); // Ensure we found templates to test

        // Pattern matches: meas dc <varname> param='...<v(...)>...' or similar with i(...)
        // This catches the invalid syntax where vectors are used in param expressions
        var invalidPattern = InvalidMeasParamVectorPattern();

        foreach (var template in ngspiceTemplates)
        {
            var content = template.Text;
            var matches = invalidPattern.Matches(content);

            Assert.Empty(matches);
        }
    }

    [Fact]
    public void NgspiceTemplates_PowerMeasurements_UseLet()
    {
        // Positive test: verify that power measurements use the correct `let` syntax
        // instead of the invalid `meas dc param=` syntax with vectors

        var ngspiceTemplates = LoadNgspiceTemplates();

        // Pattern matches: let pwr_... = v(...)*(-i(V...))
        // Accounts for Scriban template syntax like {{ supply.net }}
        // Character class includes \w (word chars), {}, ., and spaces
        var validLetPattern = ValidPowerLetPattern();

        var dcBenchTemplates = ngspiceTemplates
            .Where(t => t.Name.Contains("DCBench", System.StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(dcBenchTemplates); // Ensure we have DC bench templates to test

        foreach (var template in dcBenchTemplates)
        {
            var content = template.Text;
            var matches = validLetPattern.Matches(content);

            // DC bench templates should have at least one power measurement using let
            Assert.NotEmpty(matches);
        }
    }

    [Fact]
    public void NgspiceTemplates_ResultEchoStatements_UseShellVariableSyntax()
    {
        // Verify that RESULT echo statements use $& syntax for ngspice variables.
        // Without $&, ngspice prints literal tokens like "gain_dc" instead of the value.

        var ngspiceTemplates = LoadNgspiceTemplates();
        var resultEchoLineCount = 0;

        foreach (var template in ngspiceTemplates)
        {
            var content = template.Text;
            var lines = content.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("echo \"RESULT:", System.StringComparison.Ordinal))
                    continue;

                Assert.Contains("$&", trimmed);
                resultEchoLineCount++;
            }
        }

        Assert.True(
            resultEchoLineCount > 0,
            "No RESULT echo statements were found in the ngspice templates; test did not validate anything."
        );
    }

    [Fact]
    public void NgspiceTemplates_ShouldNotUse_ForeachForStartStopStepRanges()
    {
        // ngspice foreach iterates literal tokens; it does not expand start:stop:step ranges.
        // Range sweeps must be implemented using while loops that increment a variable.

        var ngspiceTemplates = LoadNgspiceTemplates();

        var invalidForeachRangePattern = InvalidForeachRangePattern();

        foreach (var template in ngspiceTemplates)
        {
            var content = template.Text;
            var matches = invalidForeachRangePattern.Matches(content);
            Assert.Empty(matches);
        }
    }

    [Fact]
    public void NgspiceTemplates_ShouldNotUse_MeasTranWithDifferentialVoltageForm()
    {
        // ngspice `meas tran ... MAX/MIN` does not accept v(node_pos, node_neg) directly.
        // Use a `let` to define a vector (e.g., let vdiff = v(a) - v(b)), then measure that.

        var ngspiceTemplates = LoadNgspiceTemplates();

        var invalidMeasTranDifferentialVPattern = InvalidMeasTranDifferentialVPattern();

        foreach (var template in ngspiceTemplates)
        {
            var content = template.Text;
            var matches = invalidMeasTranDifferentialVPattern.Matches(content);
            Assert.Empty(matches);
        }
    }

    [GeneratedRegex(
        @"meas\s+dc\s+\w+\s+param\s*=\s*['""][^'""]*[vi]\([^'""]+\)['""]",
        RegexOptions.IgnoreCase | RegexOptions.Multiline,
        "en-US"
    )]
    private static partial Regex InvalidMeasParamVectorPattern();

    [GeneratedRegex(
        @"meas\s+tran\s+\w+\s+(max|min)\s+v\([^)]*,[^)]*\)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline,
        "en-US"
    )]
    private static partial Regex InvalidMeasTranDifferentialVPattern();

    [GeneratedRegex(
        @"let\s+pwr_[\w\{\}\.\s]+\s*=\s*v\([^)]+\)\s*\*\s*\(\s*-i\([^)]+\)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline,
        "en-US"
    )]
    private static partial Regex ValidPowerLetPattern();

    [GeneratedRegex(
        @"^\s*foreach\s+\w+\s+\$&\w+_start\s+\$&\w+_stop\s+\$&\w+_step\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline,
        "en-US"
    )]
    private static partial Regex InvalidForeachRangePattern();

    private static IReadOnlyList<(string Name, string Text)> LoadNgspiceTemplates()
    {
        var templates = BenchTemplateLibrary
            .GetBenchNames()
            .Select(benchName =>
            {
                var found = BenchTemplateLibrary.TryGetTemplate(
                    benchName,
                    BenchBackendType.Ngspice,
                    out var templateText
                );
                return (Found: found, Name: benchName, Text: templateText);
            })
            .Where(t => t.Found)
            .Select(t => (t.Name, t.Text))
            .ToArray();

        return templates;
    }
}
