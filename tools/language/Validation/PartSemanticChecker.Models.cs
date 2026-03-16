using System.Collections.Generic;

namespace Cascode.Language.Validation;

internal static partial class PartSemanticChecker
{
    private sealed record EffectivePartDefinition(
        string Name,
        bool IsAbstract,
        List<string> Implements,
        List<CircuitParameter> Parameters,
        List<string> Supplies,
        List<string> Grounds,
        List<PortDeclaration> Ports,
        List<PartCorner> Corners,
        PartCatalog Catalog,
        List<EffectivePartEntry> EffectiveEntries
    );

    private sealed record EffectivePartEntry(
        string? EntryName,
        Dictionary<string, string> Selection,
        PartCatalogBody Body,
        List<SelectionArgument> Excludes
    );
}
