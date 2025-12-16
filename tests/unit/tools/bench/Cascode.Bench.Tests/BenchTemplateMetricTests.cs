using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Bench.Tests;

/// <summary>
/// Tests to ensure bench templates emit all metrics declared in their .cas specifications.
/// These tests catch spec-template mismatches that would cause missing measurements.
/// </summary>
public class BenchTemplateMetricTests
{
    [Fact]
    public void ParseBenchMetrics_FDOpAmpDCBench_ParsesAllMetrics()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var casPath = Path.Combine(repoRoot, "lib/std/amp/benches/FDOpAmpDCBench.cas");

        var metrics = ParseBenchMetrics(casPath);

        Assert.Contains("InputDCCommonMode", metrics);
        Assert.Contains("OutputDCCommonMode", metrics);
        Assert.Contains("OutputDCCommonMode_min", metrics);
        Assert.Contains("OutputDCCommonMode_max", metrics);
        Assert.Contains("QuiescentPower", metrics);
        Assert.Equal(5, metrics.Count);
    }

    [Fact]
    public void TemplateEmitsAllDeclaredMetrics_FDOpAmpDCBench_Ngspice()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var casPath = Path.Combine(repoRoot, "lib/std/amp/benches/FDOpAmpDCBench.cas");
        var templatePath = Path.Combine(repoRoot, "lib/std/amp/benches/FDOpAmpDCBench.ngspice.tpl");

        AssertTemplateEmitsAllMetrics(casPath, templatePath);
    }

    [Fact]
    public void TemplateEmitsAllDeclaredMetrics_SEOpAmpDCBench_Ngspice()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var casPath = Path.Combine(repoRoot, "lib/std/amp/benches/SEOpAmpDCBench.cas");
        var templatePath = Path.Combine(repoRoot, "lib/std/amp/benches/SEOpAmpDCBench.ngspice.tpl");

        AssertTemplateEmitsAllMetrics(casPath, templatePath);
    }

    [Fact]
    public void AllStandardBenches_EmitDeclaredMetrics()
    {
        var repoRoot = TestPathUtilities.GetRepositoryRoot();
        var benchesDir = Path.Combine(repoRoot, "lib/std/amp/benches");

        // Discover all .cas bench files
        var casFiles = Directory.GetFiles(benchesDir, "*.cas")
            .Where(f => !f.EndsWith("Amplifier.cas")) // Skip trait files
            .ToList();

        Assert.NotEmpty(casFiles);

        var failures = new List<string>();

        foreach (var casFile in casFiles)
        {
            var benchName = Path.GetFileNameWithoutExtension(casFile);
            var ngspiceTemplate = Path.Combine(benchesDir, $"{benchName}.ngspice.tpl");

            // Only test if ngspice template exists
            if (File.Exists(ngspiceTemplate))
            {
                try
                {
                    AssertTemplateEmitsAllMetrics(casFile, ngspiceTemplate);
                }
                catch (Exception ex)
                {
                    failures.Add($"{benchName}: {ex.Message}");
                }
            }
        }

        if (failures.Any())
        {
            var failureMessage = "The following benches have metric emission issues:\n" +
                                 string.Join("\n", failures);
            Assert.Fail(failureMessage);
        }
    }

    /// <summary>
    /// Parses a bench .cas file to extract all declared metric names.
    /// </summary>
    private static List<string> ParseBenchMetrics(string casFilePath)
    {
        if (!File.Exists(casFilePath))
        {
            throw new FileNotFoundException($"Bench .cas file not found: {casFilePath}");
        }

        var content = File.ReadAllText(casFilePath);
        var metrics = new List<string>();

        // Match: metrics [ ... ]
        // Looking for lines like "MetricName: Unit," or "MetricName: Unit"
        var metricsBlockMatch = Regex.Match(content, @"metrics\s*\[\s*(.*?)\s*\]", RegexOptions.Singleline);
        if (!metricsBlockMatch.Success)
        {
            throw new InvalidOperationException($"No metrics block found in {casFilePath}");
        }

        var metricsBlock = metricsBlockMatch.Groups[1].Value;

        var strippedMetricsBlock = string.Join(
            "\n",
            metricsBlock
                .Split('\n')
                .Select(line =>
                {
                    var commentStart = line.IndexOf("//", StringComparison.Ordinal);
                    return commentStart >= 0 ? line[..commentStart] : line;
                }));

        // Match metric declarations: MetricName: Unit
        var metricMatches = Regex.Matches(strippedMetricsBlock, @"(\w+)\s*:\s*\w+");
        foreach (Match match in metricMatches)
        {
            metrics.Add(match.Groups[1].Value);
        }

        return metrics;
    }

    /// <summary>
    /// Extracts metric names from RESULT: echo lines in a template.
    /// </summary>
    private static HashSet<string> ExtractResultMetrics(string templatePath)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Template file not found: {templatePath}");
        }

        var content = File.ReadAllText(templatePath);
        var resultMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Match lines like: echo "RESULT: MetricName = " ...
        // or: RESULT: MetricName = value
        var resultMatches = Regex.Matches(content, @"RESULT:\s*(\w+)\s*=", RegexOptions.IgnoreCase);
        foreach (Match match in resultMatches)
        {
            resultMetrics.Add(match.Groups[1].Value);
        }

        return resultMetrics;
    }

    /// <summary>
    /// Verifies that a template emits all metrics declared in its bench spec.
    /// </summary>
    private static void AssertTemplateEmitsAllMetrics(string casPath, string templatePath)
    {
        var declaredMetrics = ParseBenchMetrics(casPath);
        var emittedMetrics = ExtractResultMetrics(templatePath);

        var missingMetrics = new List<string>();

        // Determine which metrics should have RESULT lines
        // If a base metric has _min or _max variants, skip the base metric (it's per-point only)
        var metricsWithVariants = new HashSet<string>();
        foreach (var metric in declaredMetrics)
        {
            if (metric.EndsWith("_min") || metric.EndsWith("_max"))
            {
                var baseName = metric.Substring(0, metric.LastIndexOf('_'));
                metricsWithVariants.Add(baseName);
            }
        }

        foreach (var metric in declaredMetrics)
        {
            // Skip per-point metrics that have aggregate variants
            if (metricsWithVariants.Contains(metric))
            {
                continue;
            }

            // Check if this metric should be emitted
            if (metric.EndsWith("_min") || metric.EndsWith("_max"))
            {
                // Aggregate metrics (_min, _max) should always be emitted
                if (!emittedMetrics.Contains(metric))
                {
                    missingMetrics.Add(metric);
                }
            }
            else if (!declaredMetrics.Any(m => m.StartsWith(metric + "_")))
            {
                // Standalone metrics (no variants) should be emitted unless they look like sweep inputs
                // Heuristic: metrics starting with "Input" are typically sweep parameters
                if (!metric.StartsWith("Input", StringComparison.OrdinalIgnoreCase) &&
                    !metric.Equals("Examples", StringComparison.OrdinalIgnoreCase))
                {
                    if (!emittedMetrics.Contains(metric))
                    {
                        missingMetrics.Add(metric);
                    }
                }
            }
        }

        if (missingMetrics.Any())
        {
            var benchName = Path.GetFileNameWithoutExtension(casPath);
            var templateName = Path.GetFileName(templatePath);
            Assert.Fail($"Template {templateName} for bench {benchName} is missing RESULT emission for: {string.Join(", ", missingMetrics)}");
        }
    }
}
