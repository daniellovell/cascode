using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Cascode.Workspace;

public static class PdkPrimitiveLibraryLayout
{
    public const string DevicesFileName = "devices.cas";
    public const string ResistorsFileName = "resistors.cas";
    public const string CapacitorsFileName = "capacitors.cas";
    public const string DiodesFileName = "diodes.cas";

    public static readonly IReadOnlyList<string> CategoryFiles = new[]
    {
        DevicesFileName,
        ResistorsFileName,
        CapacitorsFileName,
        DiodesFileName,
    };

    public static string SanitizePdkName(string pdkName)
    {
        if (string.IsNullOrWhiteSpace(pdkName))
        {
            return "pdk";
        }

        var sb = new StringBuilder(pdkName.Length);
        foreach (var c in pdkName.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c == '-' || c == '.')
            {
                sb.Append('_');
            }
        }

        var sanitized = sb.ToString();
        return string.IsNullOrWhiteSpace(sanitized) ? "pdk" : sanitized;
    }

    public static string GetLibraryNamespace(string pdkName)
    {
        return $"lib.pdk.{SanitizePdkName(pdkName)}";
    }

    public static string GetDefaultOutputDirectory(string pdkName)
    {
        return Path.Combine("lib", "pdk", SanitizePdkName(pdkName));
    }

    public static string GetLibraryDirectory(string workspaceRoot, string pdkName)
    {
        return Path.Combine(workspaceRoot, "lib", "pdk", SanitizePdkName(pdkName));
    }

    public static IReadOnlyList<string> GetExpectedCategoryPaths(string outputDirectory)
    {
        var full = Path.GetFullPath(outputDirectory);
        return CategoryFiles.Select(file => Path.Combine(full, file)).ToArray();
    }

    public static bool TryValidateLibrary(
        string workspaceRoot,
        string pdkName,
        out string libraryDirectory,
        out string message
    )
    {
        libraryDirectory = GetLibraryDirectory(workspaceRoot, pdkName);
        if (!Directory.Exists(libraryDirectory))
        {
            message =
                $"No emitted PDK primitive library found at '{libraryDirectory}'. Run 'pdk emit primitives --pdk {SanitizePdkName(pdkName)}' first.";
            return false;
        }

        var missingFiles = GetExpectedCategoryPaths(libraryDirectory)
            .Where(path => !File.Exists(path))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingFiles.Length > 0)
        {
            message =
                $"PDK primitive library at '{libraryDirectory}' is incomplete (missing: {string.Join(", ", missingFiles)}). Run 'pdk emit primitives --pdk {SanitizePdkName(pdkName)}' again.";
            return false;
        }

        message = string.Empty;
        return true;
    }
}
