using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cascode.Cli.IntegrationTests.Infrastructure;
using Xunit;

namespace Cascode.Cli.IntegrationTests;

public class RenderIntegrationTests
{
    [Fact]
    public async Task Render_OTA5TSingleEnded_ProducesValidSvg()
    {
        // Arrange
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var home = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "render");
        var outputPath = Path.Combine(home.Path, "ota5t.svg");
        var inputPath = "tests/golden/acir/ota/OTA5TSingleEnded.el.cir";

        // Act
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            home,
            "render",
            inputPath,
            "--output",
            outputPath
        );

        // Assert
        CliIntegrationTestHelper.AssertSuccess(result);
        Assert.True(File.Exists(outputPath), "SVG file should be created");

        var svgContent = await File.ReadAllTextAsync(outputPath);

        // Verify SVG structure
        Assert.Contains("<svg", svgContent);
        Assert.Contains("</svg>", svgContent);

        // Verify expected device IDs are present
        Assert.Contains("M_TAP0", svgContent);
        Assert.Contains("M_SENSE", svgContent);
        Assert.Contains("M_N", svgContent);
        Assert.Contains("M_P", svgContent);
        Assert.Contains("M_TAIL", svgContent);

        // Verify expected ports are present
        Assert.Contains("IN.P", svgContent);
        Assert.Contains("IN.N", svgContent);
        Assert.Contains("OUT", svgContent);
        Assert.Contains("VTAIL", svgContent);
    }

    [Fact]
    public async Task Render_OTA5T_DiffPairAtSameYPosition()
    {
        // Arrange
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var home = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "render_align");
        var outputPath = Path.Combine(home.Path, "ota5t.svg");
        var inputPath = "tests/golden/acir/ota/OTA5TSingleEnded.el.cir";

        // Act
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            home,
            "render",
            inputPath,
            "--output",
            outputPath
        );

        // Assert
        CliIntegrationTestHelper.AssertSuccess(result);
        var svgContent = await File.ReadAllTextAsync(outputPath);

        // Extract device transforms using regex
        // Diff pair devices (M_N and M_P) should be at the same Y position

        // Find M_N transform
        var mnMatch = Regex.Match(
            svgContent,
            @"id=""[^""]*M_N""[^>]*transform=""translate\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)\)"""
        );
        Assert.True(mnMatch.Success, "M_N transform should be found");
        var mnY = double.Parse(mnMatch.Groups[2].Value);

        // Find M_P transform
        var mpMatch = Regex.Match(
            svgContent,
            @"id=""[^""]*M_P""[^>]*transform=""translate\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)\)"""
        );
        Assert.True(mpMatch.Success, "M_P transform should be found");
        var mpY = double.Parse(mpMatch.Groups[2].Value);

        // Verify diff pair is at same Y position (horizontal alignment)
        Assert.Equal(mnY, mpY);

        // Find M_TAIL transform - should be below the diff pair
        var tailMatch = Regex.Match(
            svgContent,
            @"id=""[^""]*M_TAIL""[^>]*transform=""translate\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)\)"""
        );
        Assert.True(tailMatch.Success, "M_TAIL transform should be found");
        var tailY = double.Parse(tailMatch.Groups[2].Value);

        // Verify tail is below diff pair (higher Y value)
        Assert.True(tailY > mnY, "Tail device should be below diff pair");
    }

    [Fact]
    public async Task Render_CSAmpResistive_ResistorAboveMosfet()
    {
        // Arrange
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var home = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "render_resistor");
        var outputPath = Path.Combine(home.Path, "cs_resistive.svg");
        var inputPath = "tests/golden/acir/cs/CSAmpResistive.el.cir";

        // Act
        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            home,
            "render",
            inputPath,
            "--output",
            outputPath
        );

        // Assert
        CliIntegrationTestHelper.AssertSuccess(result);
        var svgContent = await File.ReadAllTextAsync(outputPath);

        // Find R_load transform - should be above M_in (lower Y value)
        var resistorMatch = Regex.Match(
            svgContent,
            @"id=""R_load""[^>]*transform=""translate\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)\)"""
        );
        Assert.True(resistorMatch.Success, "R_load transform should be found");
        var resistorY = double.Parse(resistorMatch.Groups[2].Value);

        // Find M_in transform
        var mosfetMatch = Regex.Match(
            svgContent,
            @"id=""M_in""[^>]*transform=""translate\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)\)"""
        );
        Assert.True(mosfetMatch.Success, "M_in transform should be found");
        var mosfetY = double.Parse(mosfetMatch.Groups[2].Value);

        // Verify R_load is above M_in (lower Y value = higher on screen)
        Assert.True(
            resistorY < mosfetY,
            $"R_load (Y={resistorY}) should be above M_in (Y={mosfetY})"
        );

        // Verify minimum vertical separation (device height with labels)
        Assert.True(
            mosfetY - resistorY >= 50,
            $"Minimum vertical separation should be at least 50px, got {mosfetY - resistorY}"
        );
    }
}
