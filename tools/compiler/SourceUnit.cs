namespace Cascode.Compiler;

/// <summary>
/// In-memory representation of a compilation unit supplied to the compiler.
/// </summary>
/// <param name="Path">Original file path used for diagnostics.</param>
/// <param name="Text">Full source text of the unit.</param>
public readonly record struct SourceUnit(string Path, string Text);
