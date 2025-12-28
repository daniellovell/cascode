namespace Cascode.ACIR;

/// <summary>
/// ACIR format version constants.
/// </summary>
public static class ACIRVersion
{
    /// <summary>
    /// ACIR major version. Increment for breaking changes.
    /// Reader rejects files with different major version.
    /// </summary>
    public const int Major = 1;

    /// <summary>
    /// ACIR minor version. Increment for additive changes.
    /// Reader accepts any minor version within same major.
    /// </summary>
    public const int Minor = 0;

    /// <summary>
    /// Current ACIR version string (MAJOR.MINOR format).
    /// </summary>
    public static string Current => $"{Major}.{Minor}";
}

