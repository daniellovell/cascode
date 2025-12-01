using System.Linq;
using Xunit;

namespace Cascode.Parser.Tests;

/// <summary>
/// Shared test utilities for parser tests.
/// </summary>
public static class ParserTestHelpers
{
    /// <summary>
    /// Asserts that the parsed syntax tree has no parse errors.
    /// </summary>
    /// <param name="tree">The syntax tree to check.</param>
    public static void AssertNoParseErrors(CascodeSyntaxTree tree)
    {
        var errors = tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, $"Expected no parse errors but got: {string.Join("; ", errors.Select(e => e.Message))}");
    }
}

