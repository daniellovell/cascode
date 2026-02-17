using System;
using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;
using Cascode.Language.Validation;

namespace Cascode.Language;

/// <summary>
/// Entry point for parsing Cascode source text into an CascodeDocument using ANTLR.
/// </summary>
public static class CascodeParserFacade
{
    /// <summary>
    /// Parses the provided Cascode source text using the ANTLR-generated lexer and parser.
    /// </summary>
    /// <param name="path">File path used for diagnostic reporting.</param>
    /// <param name="text">Source text to parse.</param>
    /// <param name="options">Optional parse options controlling post-parse transforms and validation.</param>
    /// <returns>An CascodeReadResult containing the parsed document and any diagnostics.</returns>
    public static CascodeReadResult Parse(
        string path,
        string text,
        CascodeParseOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);

        options ??= CascodeParseOptions.Default;

        var diagnostics = new List<Diagnostic>();

        try
        {
            var inputStream = CharStreams.fromString(text);
            var lexer = new CascodeLexer(inputStream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new CascodeParser(tokens);

            lexer.RemoveErrorListeners();
            parser.RemoveErrorListeners();

            var listener = new CascodeErrorListener(path, diagnostics);
            lexer.AddErrorListener(listener);
            parser.AddErrorListener(listener);

            var rootContext = parser.document();

            // If there are syntax errors, return early with null document
            if (
                diagnostics.Count > 0
                && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            )
            {
                return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
            }

            var builder = new CascodeAstBuilder(path, diagnostics);
            var document = builder.Build(rootContext);

            var parsed = document;
            if (options.DesugarBundles)
            {
                parsed = BundleDesugarer.Desugar(parsed);
            }

            // Bench inheritance and binding extensions require a complete document (no includes) to resolve.
            // Syntax-only parses used by the linker must preserve raw benches.
            var runBenchTransforms =
                options.RunBenchSemanticChecks || options.RunBenchBindingChecksWhenNoIncludes;
            if (runBenchTransforms && parsed.Includes.Count == 0)
            {
                parsed = BenchInheritanceResolver.Resolve(parsed, diagnostics);
                parsed = BenchBindingExtender.Apply(parsed, diagnostics);
            }

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                return new CascodeReadResult { Document = parsed, Diagnostics = diagnostics };
            }

            if (options.RunBenchSemanticChecks)
            {
                // Bench semantic checks (type checking for measurement expressions).
                BenchSemanticChecker.Check(parsed, diagnostics);
            }

            // Bench binding checks require a complete document. For source files with includes,
            // defer these checks until after linking produces an include-free document.
            if (options.RunBenchBindingChecksWhenNoIncludes && parsed.Includes.Count == 0)
            {
                BenchBindingChecker.Check(parsed, diagnostics);
            }

            parsed = ApplyRenderValidation(parsed, diagnostics);

            if (options.CompatibilityMinor < 2)
            {
                parsed = StripRenderBlocks(parsed);
            }

            return new CascodeReadResult { Document = parsed, Diagnostics = diagnostics };
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new Diagnostic(
                    $"CAS0001: Failed to parse Cascode: {ex.Message}",
                    DiagnosticSeverity.Error,
                    path,
                    1,
                    1
                )
            );
            return new CascodeReadResult { Document = null, Diagnostics = diagnostics };
        }
    }

    private static CascodeDocument ApplyRenderValidation(
        CascodeDocument doc,
        List<Diagnostic> diagnostics
    )
    {
        if (doc.Circuits.Count == 0)
        {
            return doc;
        }

        var updatedCircuits = new List<Circuit>(doc.Circuits.Count);
        foreach (var circuit in doc.Circuits)
        {
            var validation = RenderBlockValidator.Validate(circuit);
            foreach (var message in validation.Messages)
            {
                diagnostics.Add(
                    new Diagnostic(
                        $"CAS3200: {message}",
                        DiagnosticSeverity.Warning,
                        "<render>",
                        1,
                        1
                    )
                );
            }

            updatedCircuits.Add(
                new Circuit
                {
                    Name = circuit.Name,
                    Traits = circuit.Traits,
                    Level = circuit.Level,
                    Inline = circuit.Inline,
                    Package = circuit.Package,
                    Parameters = circuit.Parameters,
                    Sizes = circuit.Sizes,
                    Supplies = circuit.Supplies,
                    Grounds = circuit.Grounds,
                    Ports = circuit.Ports,
                    Slot = circuit.Slot,
                    Fill = circuit.Fill,
                    Constraints = circuit.Constraints,
                    Harness = circuit.Harness,
                    Env = circuit.Env,
                    Render = validation.Render,
                    BenchBindings = circuit.BenchBindings,
                    BenchBindingExtensions = circuit.BenchBindingExtensions,
                    Synth = circuit.Synth,
                    Provenance = circuit.Provenance,
                }
            );
        }

        return new CascodeDocument
        {
            VersionMajor = doc.VersionMajor,
            VersionMinor = doc.VersionMinor,
            Includes = doc.Includes,
            FileLibrary = doc.FileLibrary,
            Functions = doc.Functions,
            BundleTypes = doc.BundleTypes,
            Traits = doc.Traits,
            BenchDefinitions = doc.BenchDefinitions,
            Primitives = doc.Primitives,
            Circuits = updatedCircuits,
        };
    }

    private static CascodeDocument StripRenderBlocks(CascodeDocument doc)
    {
        if (doc.Circuits.All(c => c.Render is null))
        {
            return doc;
        }

        return new CascodeDocument
        {
            VersionMajor = doc.VersionMajor,
            VersionMinor = doc.VersionMinor,
            Includes = doc.Includes,
            FileLibrary = doc.FileLibrary,
            Functions = doc.Functions,
            BundleTypes = doc.BundleTypes,
            Traits = doc.Traits,
            BenchDefinitions = doc.BenchDefinitions,
            Primitives = doc.Primitives,
            Circuits = doc
                .Circuits.Select(circuit => new Circuit
                {
                    Name = circuit.Name,
                    Traits = circuit.Traits,
                    Level = circuit.Level,
                    Inline = circuit.Inline,
                    Package = circuit.Package,
                    Parameters = circuit.Parameters,
                    Sizes = circuit.Sizes,
                    Supplies = circuit.Supplies,
                    Grounds = circuit.Grounds,
                    Ports = circuit.Ports,
                    Slot = circuit.Slot,
                    Fill = circuit.Fill,
                    Constraints = circuit.Constraints,
                    Harness = circuit.Harness,
                    Env = circuit.Env,
                    Render = null,
                    BenchBindings = circuit.BenchBindings,
                    BenchBindingExtensions = circuit.BenchBindingExtensions,
                    Synth = circuit.Synth,
                    Provenance = circuit.Provenance,
                })
                .ToList(),
        };
    }
}
