using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Cascode.TestSupport;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public sealed class ConvertCommandIntegrationTests : IDisposable
{
    private readonly string _repoRoot;
    private readonly CascodeHomeScope _cascodeHome;
    private readonly string _outputDir;

    public ConvertCommandIntegrationTests()
    {
        _repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        _cascodeHome = CliIntegrationTestHelper.CreateCascodeHome(_repoRoot, "convert-golden");
        _outputDir = Path.Combine(_cascodeHome.Path, "out");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        _cascodeHome.Dispose();
        if (Directory.Exists(_outputDir))
        {
            try
            {
                Directory.Delete(_outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task AllGoldenJsonFiles_ConvertBidirectionally_Succeeds()
    {
        var jsonDir = Path.Combine(_repoRoot, "tests", "golden", "acir", "json");
        Assert.True(Directory.Exists(jsonDir), $"Golden JSON directory not found: {jsonDir}");

        var jsonFiles = Directory.GetFiles(jsonDir, "*.json").OrderBy(Path.GetFileName).ToList();
        Assert.NotEmpty(jsonFiles);

        var failures = new List<string>();

        foreach (var jsonFile in jsonFiles)
        {
            var match = FindMatchingCasFile(jsonFile, failures);
            if (match == null)
            {
                continue;
            }

            var jsonToAcirOutput = Path.Combine(
                _outputDir,
                $"{Path.GetFileNameWithoutExtension(jsonFile)}.roundtrip.el.cas"
            );
            var jsonToAcir = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(30),
                _cascodeHome,
                "convert",
                jsonFile,
                "--cascode",
                "-o",
                jsonToAcirOutput
            );
            if (jsonToAcir.ExitCode != 0)
            {
                failures.Add(
                    $"{Path.GetFileName(jsonFile)} -> ACIR failed (Exit {jsonToAcir.ExitCode}): {jsonToAcir.Stderr}"
                );
            }
            else if (!File.Exists(jsonToAcirOutput))
            {
                failures.Add(
                    $"{Path.GetFileName(jsonFile)} -> ACIR did not produce {jsonToAcirOutput}"
                );
            }

            var acirToJsonOutput = Path.Combine(
                _outputDir,
                $"{Path.GetFileNameWithoutExtension(match)}.roundtrip.el.json"
            );
            var acirToJson = await CliIntegrationTestHelper.RunCliAsync(
                TimeSpan.FromSeconds(30),
                _cascodeHome,
                "convert",
                match,
                "--json",
                "-o",
                acirToJsonOutput
            );
            if (acirToJson.ExitCode != 0)
            {
                failures.Add(
                    $"{Path.GetFileName(match)} -> JSON failed (Exit {acirToJson.ExitCode}): {acirToJson.Stderr}"
                );
            }
            else if (!File.Exists(acirToJsonOutput))
            {
                failures.Add(
                    $"{Path.GetFileName(match)} -> JSON did not produce {acirToJsonOutput}"
                );
            }
        }

        if (failures.Count != 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, failures));
        }
    }

    private string? FindMatchingCasFile(string jsonFile, List<string> failures)
    {
        var acirRoot = Path.Combine(_repoRoot, "tests", "golden", "acir");
        var baseName = Path.GetFileNameWithoutExtension(jsonFile);
        var targetFileName = $"{baseName}.cas";

        var matches = Directory
            .GetFiles(acirRoot, targetFileName, SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}json{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .OrderBy(Path.GetFullPath)
            .ToList();

        if (matches.Count == 0)
        {
            failures.Add($"{Path.GetFileName(jsonFile)}: No matching .el.cas found");
            return null;
        }

        if (matches.Count > 1)
        {
            failures.Add(
                $"{Path.GetFileName(jsonFile)}: Multiple .el.cas matches: {string.Join(", ", matches)}"
            );
            return null;
        }

        return matches[0];
    }
}
