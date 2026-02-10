using System;

namespace Cascode.Language;

internal static class CascodeSymbolUtils
{
    public static bool MightDefineAnySymbol(string content, string name) =>
        ContainsKeywordDecl(content, "bundle", name)
        || ContainsKeywordDecl(content, "interface", name)
        || ContainsKeywordDecl(content, "bench", name)
        || ContainsKeywordDecl(content, "function", name)
        || ContainsPrimitiveDecl(content, name)
        || ContainsKeywordDecl(content, "circuit", name);

    public static bool ContainsPrimitiveDecl(string content, string name) =>
        ContainsKeywordDecl(content, "NMOS", name)
        || ContainsKeywordDecl(content, "PMOS", name)
        || ContainsKeywordDecl(content, "Resistor", name)
        || ContainsKeywordDecl(content, "Capacitor", name)
        || ContainsKeywordDecl(content, "Diode", name);

    public static bool ContainsKeywordDecl(string content, string keyword, string name) =>
        content.Contains(keyword + " " + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\t" + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\r\n" + name, StringComparison.Ordinal)
        || content.Contains(keyword + "\n" + name, StringComparison.Ordinal);
}
