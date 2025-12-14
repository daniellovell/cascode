using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Bench.Tests;

/// <summary>
/// Architecture tests that validate ngspice template syntax to prevent common errors.
/// </summary>
public class NgspiceTemplateSyntaxTests
{
    [Fact]
    public void NgspiceTemplates_ShouldNotUse_MeasDcParamWithVectors()
    {
        // This test prevents the bug where `meas dc ... param='v(...)*i(...)'` was used
        // inside .control blocks. The param= form cannot contain simulation vectors like
        // v() or i() - those must use `let` statements instead.

        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var templatesDir = Path.Combine(repoRoot, "lib", "std", "amp", "benches");
        var ngspiceTemplates = Directory.GetFiles(templatesDir, "*.ngspice.tpl");

        Assert.NotEmpty(ngspiceTemplates); // Ensure we found templates to test

        // Pattern matches: meas dc <varname> param='...<v(...)>...' or similar with i(...)
        // This catches the invalid syntax where vectors are used in param expressions
        var invalidPattern = new Regex(@"meas\s+dc\s+\w+\s+param\s*=\s*['""][^'""]*[vi]\([^'""]+\)['""]",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (var templatePath in ngspiceTemplates)
        {
            var content = File.ReadAllText(templatePath);
            var matches = invalidPattern.Matches(content);

            Assert.Empty(matches);
        }
    }

    [Fact]
    public void NgspiceTemplates_PowerMeasurements_UseLet()
    {
        // Positive test: verify that power measurements use the correct `let` syntax
        // instead of the invalid `meas dc param=` syntax with vectors

        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var templatesDir = Path.Combine(repoRoot, "lib", "std", "amp", "benches");
        var ngspiceTemplates = Directory.GetFiles(templatesDir, "*.ngspice.tpl");

        // Pattern matches: let pwr_... = v(...)*(-i(V...))
        // Accounts for Scriban template syntax like {{ supply.net }}
        // Character class includes \w (word chars), {}, ., and spaces
        var validLetPattern = new Regex(@"let\s+pwr_[\w\{\}\.\s]+\s*=\s*v\([^)]+\)\s*\*\s*\(\s*-i\([^)]+\)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        var dcBenchTemplates = ngspiceTemplates.Where(t => t.Contains("DCBench")).ToArray();
        Assert.NotEmpty(dcBenchTemplates); // Ensure we have DC bench templates to test

        foreach (var templatePath in dcBenchTemplates)
        {
            var content = File.ReadAllText(templatePath);
            var matches = validLetPattern.Matches(content);

            // DC bench templates should have at least one power measurement using let
            Assert.NotEmpty(matches);
        }
    }

    [Fact]
    public void NgspiceTemplates_EchoStatements_UseShellVariableSyntax()
    {
        // Verify that echo statements use $& syntax for shell variables created by let
        // instead of trying to directly reference measurement names

        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var templatesDir = Path.Combine(repoRoot, "lib", "std", "amp", "benches");
        var ngspiceTemplates = Directory.GetFiles(templatesDir, "*.ngspice.tpl");

        // Pattern matches: echo "RESULT: QuiescentPower = " $&pwr_... " W"
        // Accounts for Scriban template syntax like {{ supply.net }}
        var validEchoPattern = new Regex(@"echo\s+""RESULT:\s*QuiescentPower\s*=\s*""\s+\$&pwr_[\w{}.]+",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        var dcBenchTemplates = ngspiceTemplates.Where(t => t.Contains("DCBench")).ToArray();

        foreach (var templatePath in dcBenchTemplates)
        {
            var content = File.ReadAllText(templatePath);
            var matches = validEchoPattern.Matches(content);

            // DC bench templates should have at least one echo using $& syntax for power
            Assert.NotEmpty(matches);
        }
    }
}
