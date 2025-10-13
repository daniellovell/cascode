using System;

namespace Cascode.Workspace;

public sealed class ModelContext : IEquatable<ModelContext>
{
    public string? Corner { get; init; }
    public string? Detail { get; init; }
    public string? Section { get; init; }
    public string? IncludePath { get; init; }

    public bool Equals(ModelContext? other)
    {
        if (other is null) return false;
        return string.Equals(Corner, other.Corner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Detail, other.Detail, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Section, other.Section, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizePath(IncludePath), NormalizePath(other.IncludePath), StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as ModelContext);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Corner?.ToLowerInvariant(),
            Detail?.ToLowerInvariant(),
            Section?.ToLowerInvariant(),
            NormalizePath(IncludePath)?.ToLowerInvariant());
    }

    private static string? NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        try { return System.IO.Path.GetFullPath(p); } catch { return p; }
    }
}

