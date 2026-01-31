using System;
using System.IO;

namespace Cascode.Language.BenchRuntime;

public static class BenchRuntimePaths
{
    public static string GetTestbenchPath(string outputDir, string circuitName, string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        return Path.Combine(outputDir, $"{circuitName}_{bindingName}.sp");
    }

    public static string GetAcWrdataPath(
        string outputDir,
        string circuitName,
        string bindingName,
        string analysisName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisName);

        return Path.Combine(outputDir, $"{circuitName}_{bindingName}__{analysisName}.ac.wrdata");
    }

    public static string GetNoiseWrdataPath(
        string outputDir,
        string circuitName,
        string bindingName,
        string analysisName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisName);

        return Path.Combine(outputDir, $"{circuitName}_{bindingName}__{analysisName}.noise.wrdata");
    }

    public static string GetOpWrdataPath(string outputDir, string circuitName, string bindingName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitName);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingName);

        return Path.Combine(outputDir, $"{circuitName}_{bindingName}__op.op.wrdata");
    }
}
