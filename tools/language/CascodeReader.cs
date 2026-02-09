using System.IO;
using System.Linq;

namespace Cascode.Language;

/// <summary>
/// Parses Cascode text format into CascodeDocument objects.
/// </summary>
/// <remarks>
/// The reader supports the Cascode text format as specified in Ch03_Cascode.md.
/// It handles bundle definitions, circuit declarations at all levels (HL, ML, EL),
/// and all circuit sections including fill, harness, benches, and provenance.
/// </remarks>
public static class CascodeReader
{
    /// <summary>
    /// Reads an Cascode document from a text reader. Throws on parse errors.
    /// </summary>
    /// <param name="reader">Text reader containing Cascode content.</param>
    /// <param name="filePath">Optional file path for error messages.</param>
    /// <returns>Parsed Cascode document.</returns>
    /// <exception cref="CascodeParseException">Thrown when parsing fails.</exception>
    /// <remarks>
    /// For structured error handling, use <see cref="TryRead"/> instead.
    /// </remarks>
    public static CascodeDocument Read(TextReader reader, string filePath = "<unknown>")
    {
        var result = TryRead(reader, filePath);
        if (!result.Success)
        {
            var errors = result
                .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            var message = errors.Count > 0 ? errors[0].Message : "Unknown parse error";
            throw new CascodeParseException(message, result.Diagnostics);
        }
        return result.Document!;
    }

    /// <summary>
    /// Parses Cascode text content from a string. Throws on parse errors.
    /// </summary>
    /// <param name="content">Cascode text content.</param>
    /// <param name="filePath">Optional file path for error messages.</param>
    /// <returns>Parsed Cascode document.</returns>
    /// <exception cref="CascodeParseException">Thrown when parsing fails.</exception>
    /// <remarks>
    /// For structured error handling, use <see cref="TryParse"/> instead.
    /// </remarks>
    public static CascodeDocument Parse(string content, string filePath = "<unknown>")
    {
        var result = TryParse(content, filePath);
        if (!result.Success)
        {
            var errors = result
                .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            var message = errors.Count > 0 ? errors[0].Message : "Unknown parse error";
            throw new CascodeParseException(message, result.Diagnostics);
        }
        return result.Document!;
    }

    /// <summary>
    /// Reads an Cascode document from a text reader with structured error handling.
    /// </summary>
    /// <param name="reader">Text reader containing Cascode content.</param>
    /// <param name="filePath">Path of the source file for diagnostics.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    /// <remarks>
    /// This method follows the same pattern as <see cref="Cascode.Compiler.SimpleCascodeCompiler"/>
    /// by returning structured diagnostics instead of throwing exceptions.
    /// </remarks>
    public static CascodeReadResult TryRead(TextReader reader, string filePath = "<unknown>")
    {
        return CascodeParserFacade.Parse(filePath, reader.ReadToEnd(), CascodeParseOptions.Default);
    }

    /// <summary>
    /// Parses Cascode text content from a string with structured error handling.
    /// </summary>
    /// <param name="content">Cascode text content.</param>
    /// <param name="filePath">Path of the source file for diagnostics.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    public static CascodeReadResult TryParse(string content, string filePath = "<unknown>")
    {
        return CascodeParserFacade.Parse(filePath, content, CascodeParseOptions.Default);
    }

    /// <summary>
    /// Reads a Cascode document using explicit parse options (used by the linker for syntax-only parsing).
    /// </summary>
    public static CascodeReadResult TryRead(
        TextReader reader,
        CascodeParseOptions options,
        string filePath = "<unknown>"
    )
    {
        return CascodeParserFacade.Parse(filePath, reader.ReadToEnd(), options);
    }
}
