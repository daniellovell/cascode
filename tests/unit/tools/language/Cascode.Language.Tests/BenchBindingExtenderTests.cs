using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class BenchBindingExtenderTests
{
    [Fact]
    public void Apply_ReportsUnknownExtensionTargets()
    {
        var document = new CascodeDocument
        {
            Circuits =
            [
                new Circuit
                {
                    Name = "Amp",
                    BenchBindingExtensions =
                    [
                        new BenchBindingExtension
                        {
                            BindingName = "missing_bench",
                            Statements = [new BenchDutConnection("OUT", "net::vout")],
                        },
                    ],
                },
            ],
        };
        var diagnostics = new List<Diagnostic>();

        _ = BenchBindingExtender.Apply(document, diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CAS3010", diagnostic.Code);
    }

    [Fact]
    public void Apply_ReportsDuplicateInheritedBindingNames_WhenExtensionsArePresent()
    {
        var document = new CascodeDocument
        {
            Traits =
            [
                new TraitDefinition
                {
                    Name = "First",
                    BenchBindings = [new BenchBinding { BenchName = "A", BindingName = "shared" }],
                },
                new TraitDefinition
                {
                    Name = "Second",
                    BenchBindings = [new BenchBinding { BenchName = "B", BindingName = "shared" }],
                },
            ],
            Circuits =
            [
                new Circuit
                {
                    Name = "Amp",
                    Traits = ["First", "Second"],
                    BenchBindingExtensions = [new BenchBindingExtension { BindingName = "shared" }],
                },
            ],
        };
        var diagnostics = new List<Diagnostic>();

        _ = BenchBindingExtender.Apply(document, diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CAS3011", diagnostic.Code);
    }

    [Fact]
    public void Apply_PreservesInheritedOriginOnMergedBindings()
    {
        var extensionStatement = new BenchTerminalMapping("OUT", "OUT");
        var document = new CascodeDocument
        {
            Traits =
            [
                new TraitDefinition
                {
                    Name = "AmpInterface",
                    BenchBindings =
                    [
                        new BenchBinding
                        {
                            BenchName = "TransferBench",
                            BindingName = "transfer_bench",
                            Statements = [new BenchTerminalMapping("IN", "IN")],
                        },
                    ],
                },
            ],
            Circuits =
            [
                new Circuit
                {
                    Name = "Amp",
                    Traits = ["AmpInterface"],
                    BenchBindingExtensions =
                    [
                        new BenchBindingExtension
                        {
                            BindingName = "transfer_bench",
                            Statements = [extensionStatement],
                        },
                    ],
                },
            ],
        };
        var diagnostics = new List<Diagnostic>();

        var updated = BenchBindingExtender.Apply(document, diagnostics);

        Assert.Empty(diagnostics);
        var binding = Assert.Single(updated.Circuits.Single().BenchBindings);
        Assert.Equal(BenchBindingOriginKind.Interface, binding.Resolution!.Origin.Kind);
        Assert.Equal("AmpInterface", binding.Resolution.Origin.OwnerName);
        Assert.Equal(1, binding.Resolution.ExtensionStatementCount);
        Assert.True(binding.Resolution.HasExtensions);
        Assert.Same(
            extensionStatement,
            Assert.IsType<BenchTerminalMapping>(binding.Statements.Last())
        );
    }
}
