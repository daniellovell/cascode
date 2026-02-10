namespace Cascode.Language;

public enum LinkBenchMode
{
    Full,
    None,
}

public enum LinkIncludePolicy
{
    Default,
    ExplicitOnly,
}

public sealed record CascodeLinkOptions(LinkBenchMode BenchMode, LinkIncludePolicy IncludePolicy)
{
    public static readonly CascodeLinkOptions Default = new(
        LinkBenchMode.Full,
        LinkIncludePolicy.Default
    );
}
