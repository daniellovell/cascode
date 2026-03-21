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
    /// <summary>
    /// Parses Cascode source text into a CascodeDocument and accumulates diagnostics produced during lexing, parsing, transforms, validation, and optional semantic/binding checks.
    /// </summary>
    /// <param name="path">The source file path used in diagnostics and error locations.</param>
    /// <param name="text">The Cascode source text to parse.</param>
    /// <param name="options">Optional parsing options; if null, the default options are used.</param>
    /// <returns>A CascodeReadResult containing the parsed document (or null if a fatal error occurred) and the collected diagnostics.</returns>
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

            LevelStructureValidator.Check(parsed, diagnostics);

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

            parsed = ApplyRenderValidation(path, parsed, diagnostics);

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

    /// <summary>
    /// Validate render blocks for each circuit and append any validation messages as diagnostics.
    /// </summary>
    /// <param name="path">Source file path used as the location for produced diagnostics.</param>
    /// <param name="doc">The parsed Cascode document to validate.</param>
    /// <param name="diagnostics">A mutable list that will receive warning diagnostics for render validation messages.</param>
    /// <returns>A new CascodeDocument with the same metadata but with each circuit's Render field replaced by the validator's result.</returns>
    private static CascodeDocument ApplyRenderValidation(
        string path,
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
                    new Diagnostic($"CAS3200: {message}", DiagnosticSeverity.Warning, path, 1, 1)
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
                    Metrics = circuit.Metrics,
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
            Parts = doc.Parts,
            Circuits = updatedCircuits,
        };
    }
}
