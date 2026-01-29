using System.IO;
using System.Linq;
using Cascode.Parser;

namespace Cascode.Language;

/// <summary>
/// Parses ACIR text format into ACIRDocument objects.
/// </summary>
/// <remarks>
/// The reader supports the ACIR text format as specified in Ch03_ACIR.md.
/// It handles bundle definitions, circuit declarations at all levels (HL, ML, EL),
/// and all circuit sections including fill, harness, benches, and provenance.
/// </remarks>
public static class ACIRReader
{
    /// <summary>
    /// Reads an ACIR document from a text reader. Throws on parse errors.
    /// </summary>
    /// <param name="reader">Text reader containing ACIR content.</param>
    /// <param name="filePath">Optional file path for error messages.</param>
    /// <returns>Parsed ACIR document.</returns>
    /// <exception cref="ACIRParseException">Thrown when parsing fails.</exception>
    /// <remarks>
    /// For structured error handling, use <see cref="TryRead"/> instead.
    /// </remarks>
    public static ACIRDocument Read(TextReader reader, string filePath = "<unknown>")
    {
        var result = TryRead(reader, filePath);
        if (!result.Success)
        {
            var errors = result
                .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            var message = errors.Count > 0 ? errors[0].Message : "Unknown parse error";
            throw new ACIRParseException(message, result.Diagnostics);
        }
        return result.Document!;
    }

    /// <summary>
    /// Parses ACIR text content from a string. Throws on parse errors.
    /// </summary>
    /// <param name="content">ACIR text content.</param>
    /// <param name="filePath">Optional file path for error messages.</param>
    /// <returns>Parsed ACIR document.</returns>
    /// <exception cref="ACIRParseException">Thrown when parsing fails.</exception>
    /// <remarks>
    /// For structured error handling, use <see cref="TryParse"/> instead.
    /// </remarks>
    public static ACIRDocument Parse(string content, string filePath = "<unknown>")
    {
        var result = TryParse(content, filePath);
        if (!result.Success)
        {
            var errors = result
                .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            var message = errors.Count > 0 ? errors[0].Message : "Unknown parse error";
            throw new ACIRParseException(message, result.Diagnostics);
        }
        return result.Document!;
    }

    /// <summary>
    /// Reads an ACIR document from a text reader with structured error handling.
    /// </summary>
    /// <param name="reader">Text reader containing ACIR content.</param>
    /// <param name="filePath">Path of the source file for diagnostics.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    /// <remarks>
    /// This method follows the same pattern as <see cref="Cascode.Compiler.SimpleCascodeCompiler"/>
    /// by returning structured diagnostics instead of throwing exceptions.
    /// </remarks>
    public static ACIRReadResult TryRead(TextReader reader, string filePath = "<unknown>")
    {
        return CascodeParserFacade.Parse(filePath, reader.ReadToEnd());
    }

    /// <summary>
    /// Parses ACIR text content from a string with structured error handling.
    /// </summary>
    /// <param name="content">ACIR text content.</param>
    /// <param name="filePath">Path of the source file for diagnostics.</param>
    /// <returns>Read result containing the document and any diagnostics.</returns>
    public static ACIRReadResult TryParse(string content, string filePath = "<unknown>")
    {
        return CascodeParserFacade.Parse(filePath, content);
    }
}
