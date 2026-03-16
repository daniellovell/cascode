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
        || ContainsKeywordDecl(content, "part", name)
        || ContainsKeywordDecl(content, "circuit", name);

    public static bool ContainsPrimitiveDecl(string content, string name) =>
        ContainsPrimitiveTypeDecl(content, "NMOS", name)
        || ContainsPrimitiveTypeDecl(content, "PMOS", name)
        || ContainsPrimitiveTypeDecl(content, "Resistor", name)
        || ContainsPrimitiveTypeDecl(content, "Capacitor", name)
        || ContainsPrimitiveTypeDecl(content, "Diode", name)
        || ContainsPrimitiveTypeDecl(content, "Inductor", name);

    public static bool ContainsKeywordDecl(string content, string keyword, string name) =>
        content.Contains(keyword + " " + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\t" + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\r\n" + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\n" + name, StringComparison.Ordinal);

    private static bool ContainsPrimitiveTypeDecl(string content, string deviceType, string name) =>
        Regex.IsMatch(
            content,
            $@"\bprimitive\s+{Regex.Escape(deviceType)}\s+{Regex.Escape(name)}\s*\(",
            RegexOptions.CultureInvariant
        );
}
