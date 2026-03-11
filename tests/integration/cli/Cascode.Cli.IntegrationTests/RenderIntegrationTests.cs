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
        var inputPath = "tests/golden/cas/ota/OTA5TSingleEnded.el.cai";

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
        var inputPath = "tests/golden/cas/ota/OTA5TSingleEnded.el.cai";

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

        // Find M_TAIL transform
        var tailMatch = Regex.Match(
            svgContent,
            @"id=""[^""]*M_TAIL""[^>]*transform=""translate\((\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)\)"""
        );
        Assert.True(tailMatch.Success, "M_TAIL transform should be found");
    }

    [Fact]
    public async Task Render_CSAmpResistive_ResistorAboveMosfet()
    {
        // Arrange
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var home = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "render_resistor");
        var outputPath = Path.Combine(home.Path, "cs_resistive.svg");
        var inputPath = "tests/golden/cas/cs/CSAmpResistive.el.cai";

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

        // Verify R_load is not below M_in.
        Assert.True(
            resistorY <= mosfetY,
            $"R_load (Y={resistorY}) should not be below M_in (Y={mosfetY})"
        );
    }

    [Fact]
    public async Task Render_StressFullyDiffOta_CmfbResistorsShareYPosition()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var home = CliIntegrationTestHelper.CreateCascodeHome(
            repoRoot,
            "render_stress_ota5t"
        );
        var outputPath = Path.Combine(home.Path, "ota5t_stress.svg");
        var inputPath = "tests/golden/cas/stress/OTA5TFullyDiff_Ideal.cas";

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            home,
            "render",
            inputPath,
            "--output",
            outputPath
        );

        CliIntegrationTestHelper.AssertSuccess(result);
        var svgContent = await File.ReadAllTextAsync(outputPath);

        var cmfbPX = GetDeviceOriginX(svgContent, "R_CMFB_P");
        var cmfbNX = GetDeviceOriginX(svgContent, "R_CMFB_N");
        var cmfbPY = GetDeviceOriginY(svgContent, "R_CMFB_P");
        var cmfbNY = GetDeviceOriginY(svgContent, "R_CMFB_N");
        Assert.NotEqual(cmfbPX, cmfbNX);
        Assert.Equal(cmfbPY, cmfbNY);
        Assert.DoesNotContain("rotate(-90)", GetDeviceGroup(svgContent, "R_CMFB_P"));
        Assert.DoesNotContain("rotate(-90)", GetDeviceGroup(svgContent, "R_CMFB_N"));
    }

    [Theory]
    [InlineData("tests/golden/cas/stress/RcLowpass.cas", "IN", "OUT", 50.0)]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai", "IN.P", "OUT.P", 25.0)]
    [InlineData("tests/golden/render/filters/DiffRCFilter.el.cai", "IN.N", "OUT.N", 25.0)]
    public async Task Render_FeedthroughPorts_AreVerticallyAligned(
        string inputPath,
        string leftPort,
        string rightPort,
        double maxDeltaY
    )
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var home = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "render_feedthrough");
        var outputPath = Path.Combine(
            home.Path,
            $"feedthrough_{leftPort.Replace('.', '_')}_{rightPort.Replace('.', '_')}.svg"
        );

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            home,
            "render",
            inputPath,
            "--output",
            outputPath
        );

        CliIntegrationTestHelper.AssertSuccess(result);
        var svgContent = await File.ReadAllTextAsync(outputPath);

        var leftY = GetPortOriginY(svgContent, leftPort);
        var rightY = GetPortOriginY(svgContent, rightPort);
        Assert.True(
            Math.Abs(leftY - rightY) <= maxDeltaY,
            $"Expected feedthrough ports to remain near-aligned within {maxDeltaY}, got delta={Math.Abs(leftY - rightY)}"
        );
    }

    [Fact]
    public async Task Render_RcLowpass_FeedthroughBoundaryWire_IsConnected()
    {
        var repoRoot = CliIntegrationTestHelper.GetRepositoryRoot();
        using var home = CliIntegrationTestHelper.CreateCascodeHome(repoRoot, "render_rc_lowpass");
        var outputPath = Path.Combine(home.Path, "rc_lowpass.svg");
        var inputPath = "tests/golden/cas/stress/RcLowpass.cas";

        var result = await CliIntegrationTestHelper.RunCliAsync(
            TimeSpan.FromMinutes(2),
            home,
            "render",
            inputPath,
            "--output",
            outputPath
        );

        CliIntegrationTestHelper.AssertSuccess(result);
        var svgContent = await File.ReadAllTextAsync(outputPath);

        var inY = GetPortOriginY(svgContent, "IN");
        var outY = GetPortOriginY(svgContent, "OUT");
        Assert.True(
            Math.Abs(inY - outY) <= 50.0,
            $"Expected IN/OUT ports to remain near-aligned within one cell, got delta={Math.Abs(inY - outY)}"
        );

        var inNetSegments = GetWireSegments(svgContent, "IN");
        var outNetSegments = GetWireSegments(svgContent, "OUT");
        Assert.NotEmpty(inNetSegments);
        Assert.NotEmpty(outNetSegments);

        var inBoundary = AnalyzeBoundary(inNetSegments, 0);
        var outBoundaryX = outNetSegments.Max(s => Math.Max(s.X1, s.X2));
        var outBoundary = AnalyzeBoundary(outNetSegments, outBoundaryX);

        Assert.True(
            inBoundary.HasHorizontal || inBoundary.HasVertical,
            "IN must connect to at least one boundary segment on the left edge"
        );
        Assert.True(
            outBoundary.HasHorizontal || outBoundary.HasVertical,
            "OUT must connect to at least one boundary segment on the right edge"
        );
    }

    private static double GetPortOriginY(string svgContent, string portName)
    {
        var escapedPort = Regex.Escape(portName);
        var match = Regex.Match(
            svgContent,
            $@"<g class=""port"" data-port=""{escapedPort}""[^>]*transform=""translate\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)"""
        );

        Assert.True(match.Success, $"Port '{portName}' transform should be found");
        return double.Parse(match.Groups[2].Value);
    }

    private static double GetDeviceOriginX(string svgContent, string deviceId)
    {
        var escapedDeviceId = Regex.Escape(deviceId);
        var match = Regex.Match(
            svgContent,
            $@"<g id=""{escapedDeviceId}""[^>]*transform=""translate\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)"""
        );

        Assert.True(match.Success, $"Device '{deviceId}' transform should be found");
        return double.Parse(match.Groups[1].Value);
    }

    private static double GetDeviceOriginY(string svgContent, string deviceId)
    {
        var escapedDeviceId = Regex.Escape(deviceId);
        var match = Regex.Match(
            svgContent,
            $@"<g id=""{escapedDeviceId}""[^>]*transform=""translate\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)"""
        );

        Assert.True(match.Success, $"Device '{deviceId}' transform should be found");
        return double.Parse(match.Groups[2].Value);
    }

    private static string GetDeviceGroup(string svgContent, string deviceId)
    {
        var escapedDeviceId = Regex.Escape(deviceId);
        var match = Regex.Match(
            svgContent,
            $@"<g id=""{escapedDeviceId}""[^>]*>.*?</g>",
            RegexOptions.Singleline
        );

        Assert.True(match.Success, $"Device '{deviceId}' group should be found");
        return match.Value;
    }

    private static List<SvgWireSegment> GetWireSegments(string svgContent, string netName)
    {
        var escapedNet = Regex.Escape(netName);
        var netMatch = Regex.Match(
            svgContent,
            $@"<g class=""net"" data-net=""{escapedNet}"">(?<body>.*?)</g>",
            RegexOptions.Singleline
        );

        Assert.True(netMatch.Success, $"Net '{netName}' should be present in SVG");

        var segments = new List<SvgWireSegment>();
        var lineMatches = Regex.Matches(
            netMatch.Groups["body"].Value,
            @"<line class=""wire"" x1=""(-?\d+(?:\.\d+)?)"" y1=""(-?\d+(?:\.\d+)?)"" x2=""(-?\d+(?:\.\d+)?)"" y2=""(-?\d+(?:\.\d+)?)""\s*/?>"
        );

        foreach (Match line in lineMatches)
        {
            segments.Add(
                new SvgWireSegment(
                    double.Parse(line.Groups[1].Value),
                    double.Parse(line.Groups[2].Value),
                    double.Parse(line.Groups[3].Value),
                    double.Parse(line.Groups[4].Value)
                )
            );
        }

        return segments;
    }

    private static BoundaryAnalysis AnalyzeBoundary(List<SvgWireSegment> segments, double boundaryX)
    {
        const double epsilon = 1e-9;
        var touching = segments
            .Where(s =>
                Math.Abs(s.X1 - boundaryX) < epsilon || Math.Abs(s.X2 - boundaryX) < epsilon
            )
            .ToList();
        var horizontal = touching.Where(s => s.Y1 == s.Y2).ToList();
        var hasVertical = touching.Any(s => s.X1 == s.X2);
        var horizontalY = horizontal.Count > 0 ? horizontal[0].Y1 : (double?)null;

        return new BoundaryAnalysis(horizontal.Count > 0, hasVertical, horizontalY);
    }

    private readonly record struct SvgWireSegment(double X1, double Y1, double X2, double Y2);

    private readonly record struct BoundaryAnalysis(
        bool HasHorizontal,
        bool HasVertical,
        double? HorizontalY
    );
}
