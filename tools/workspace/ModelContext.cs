using System;

namespace Cascode.Workspace;

public sealed class ModelContext : IEquatable<ModelContext>
{
    public string? Corner { get; init; }
    public string? Detail { get; init; }
    public string? Section { get; init; }
    public string? IncludePath { get; init; }

    /// <summary>
    /// Determines whether this ModelContext represents the same context as another by comparing its identifying properties.
    /// </summary>
    /// <param name="other">The ModelContext to compare to; may be null.</param>
    /// <returns>`true` if `Corner`, `Detail`, and `Section` match using ordinal case-insensitive comparison and the normalized `IncludePath` values match using ordinal case-insensitive comparison; `false` otherwise.</returns>
    public bool Equals(ModelContext? other)
    {
        if (other is null)
            return false;
        return string.Equals(Corner, other.Corner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Detail, other.Detail, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Section, other.Section, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                NormalizePath(IncludePath),
                NormalizePath(other.IncludePath),
                StringComparison.OrdinalIgnoreCase
            );
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current <see cref="ModelContext"/> instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns>`true` if <paramref name="obj"/> is a <see cref="ModelContext"/> whose values match this instance; `false` otherwise.</returns>
    public override bool Equals(object? obj) => Equals(obj as ModelContext);

    /// <summary>
    /// Computes a hash code that represents this ModelContext's value identity.
    /// </summary>
    /// <returns>
    /// An integer hash code derived from the Corner, Detail, Section, and IncludePath values; IncludePath is normalized and all values are compared in a case-insensitive form.
    /// </returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            Corner?.ToLowerInvariant(),
            Detail?.ToLowerInvariant(),
            Section?.ToLowerInvariant(),
            NormalizePath(IncludePath)?.ToLowerInvariant()
        );
    }

    /// <summary>
    /// Normalize a file system path string to its absolute form or canonical nullable representation.
    /// </summary>
    /// <param name="p">The path to normalize; may be null or whitespace.</param>
    /// <returns>
    /// The full absolute path for <paramref name="p"/> when resolvable, the original <paramref name="p"/> if resolution fails, or <c>null</c> when <paramref name="p"/> is null, empty, or only whitespace.
    /// </returns>
    private static string? NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p))
            return null;
        try
        {
            return System.IO.Path.GetFullPath(p);
        }
        catch
        {
            return p;
        }
    }
}
