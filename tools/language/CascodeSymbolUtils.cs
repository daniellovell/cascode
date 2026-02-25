using System;
using System.Text.RegularExpressions;

namespace Cascode.Language;

internal static class CascodeSymbolUtils
{
    public static bool MightDefineAnySymbol(string content, string name) =>
        ContainsKeywordDecl(content, "bundle", name)
        || ContainsKeywordDecl(content, "interface", name)
        || ContainsKeywordDecl(content, "bench", name)
        || ContainsKeywordDecl(content, "function", name)
        || ContainsPrimitiveDecl(content, name)
        || ContainsPartDecl(content, name)
        || ContainsKeywordDecl(content, "circuit", name);

    public static bool ContainsPrimitiveDecl(string content, string name) =>
        Regex.IsMatch(
            content,
            $@"\bprimitive\s+{Regex.Escape(name)}\s*\(",
            RegexOptions.CultureInvariant
        );

    public static bool ContainsPartDecl(string content, string name) =>
        Regex.IsMatch(
            content,
            $@"\b(?:abstract\s+)?part\s+{Regex.Escape(name)}\b",
            RegexOptions.CultureInvariant
        );

    public static bool ContainsKeywordDecl(string content, string keyword, string name) =>
        content.Contains(keyword + " " + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\t" + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\r\n" + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\n" + name, StringComparison.Ordinal);
}
