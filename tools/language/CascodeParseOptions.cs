namespace Cascode.Language;

/// <summary>
/// Controls which post-parse transforms and validations run during <see cref="CascodeParserFacade"/> parsing.
/// </summary>
/// <remarks>
/// The linker needs a "syntax-only" mode so it can parse individual candidate files before the full
/// dependency graph is known. Full validation is run after linking produces a self-contained document.
/// </remarks>
public sealed record CascodeParseOptions(
    bool DesugarBundles,
    bool RunBenchSemanticChecks,
    bool RunBenchBindingChecksWhenNoIncludes,
    int CompatibilityMinor = CascodeVersion.Minor
)
{
    public static readonly CascodeParseOptions Default = new(
        DesugarBundles: true,
        RunBenchSemanticChecks: true,
        RunBenchBindingChecksWhenNoIncludes: false,
        CompatibilityMinor: CascodeVersion.Minor
    );

    public static readonly CascodeParseOptions SyntaxOnly = new(
        DesugarBundles: false,
        RunBenchSemanticChecks: false,
        RunBenchBindingChecksWhenNoIncludes: false,
        CompatibilityMinor: CascodeVersion.Minor
    );

    public static readonly CascodeParseOptions Compatibility31 = new(
        DesugarBundles: true,
        RunBenchSemanticChecks: true,
        RunBenchBindingChecksWhenNoIncludes: false,
        CompatibilityMinor: 1
    );
}
