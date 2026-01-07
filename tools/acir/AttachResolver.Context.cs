using System;
using System.Collections.Generic;

namespace Cascode.ACIR;

public sealed partial class AttachResolver
{
    private sealed class ResolutionContext
    {
        public UnionFind<string> UnionFind { get; } = new();
        public Dictionary<string, string> NodeDomains { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> DomainByRoot { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> NetCountByRoot { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, SortedSet<string>> ExplicitNetNamesByRoot { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, NetTier> NetTiers { get; } = new(StringComparer.Ordinal);
        public HashSet<string> NetNodes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ExplicitNetNodes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> EndpointNodes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> AutoNetNameOverrides { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<AttachStatement, TraitConnector> ConnectorByAttach { get; } = new();
    }

    private enum NetTier
    {
        Supply = 0,
        Ground = 0,
        PortExpansion = 1,
        Declared = 2,
    }
}
