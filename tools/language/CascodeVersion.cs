namespace Cascode.Language;

/// <summary>
/// Cascode format version constants.
/// </summary>
public static class CascodeVersion
{
    /// <summary>
    /// Cascode major version. Increment for breaking changes.
    /// Reader rejects files with different major version.
    /// </summary>
    public const int Major = 3;

    /// <summary>
    /// Cascode minor version. Increment for additive changes.
    /// Reader accepts any minor version within same major.
    /// </summary>
    public const int Minor = 0;

    /// <summary>
    /// Current Cascode version string (MAJOR.MINOR format).
    /// </summary>
    public static string Current => $"{Major}.{Minor}";
}
