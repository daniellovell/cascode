namespace Cascode.Cli.Services;

public static class SpectreLocator
{
    public static string? FindOnPath()
    {
        return FindOnPath(pathOverride: null, pathextOverride: null);
    }

    internal static string? FindOnPath(string? pathOverride, string? pathextOverride)
    {
        return ToolLocator.FindOnPath("spectre", pathOverride, pathextOverride);
    }
}
