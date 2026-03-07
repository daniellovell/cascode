using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Cascode.Language.Tests;

public sealed class BenchBindingResolverTests
{
    [Fact]
    public void ResolveForCircuit_MarksInheritedBindingsWithInterfaceOrigin()
    {
        var interfaceDef = new TraitDefinition
        {
            Name = "AmpInterface",
            BenchBindings =
            [
                new BenchBinding { BenchName = "TransferBench", BindingName = "transfer_bench" },
            ],
        };
        var circuit = new Circuit { Name = "Amp", Traits = ["AmpInterface"] };

        var result = BenchBindingResolver.ResolveForCircuit(circuit, BuildInterfaces(interfaceDef));

        var binding = Assert.Single(result.Bindings).Value;
        Assert.Equal(BenchBindingOriginKind.Interface, binding.Resolution.Origin.Kind);
        Assert.Equal("AmpInterface", binding.Resolution.Origin.OwnerName);
        Assert.False(binding.Resolution.OverridesInheritedBinding);
        Assert.False(binding.Resolution.HasExtensions);
    }

    [Fact]
    public void ResolveForCircuit_MarksCircuitOverridesExplicitly()
    {
        var interfaceDef = new TraitDefinition
        {
            Name = "AmpInterface",
            BenchBindings =
            [
                new BenchBinding { BenchName = "TransferBench", BindingName = "transfer_bench" },
            ],
        };
        var circuit = new Circuit
        {
            Name = "Amp",
            Traits = ["AmpInterface"],
            BenchBindings =
            [
                new BenchBinding
                {
                    BenchName = "LocalTransferBench",
                    BindingName = "transfer_bench",
                },
            ],
        };

        var result = BenchBindingResolver.ResolveForCircuit(circuit, BuildInterfaces(interfaceDef));

        var binding = Assert.Single(result.Bindings).Value;
        Assert.Equal(BenchBindingOriginKind.Circuit, binding.Resolution.Origin.Kind);
        Assert.Equal("Amp", binding.Resolution.Origin.OwnerName);
        Assert.True(binding.Resolution.OverridesInheritedBinding);
        Assert.False(binding.Resolution.HasExtensions);
        Assert.Equal("LocalTransferBench", binding.Binding.BenchName);
    }

    [Fact]
    public void ResolveForCircuit_MarksExtendedInheritedBindingsAsCircuitRelevant()
    {
        var interfaceDef = new TraitDefinition
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
        };
        var extensionStatement = new BenchTerminalMapping("OUT", "OUT");
        var circuit = new Circuit
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
        };

        var result = BenchBindingResolver.ResolveForCircuit(circuit, BuildInterfaces(interfaceDef));

        var binding = Assert.Single(result.Bindings).Value;
        Assert.Equal(BenchBindingOriginKind.Interface, binding.Resolution.Origin.Kind);
        Assert.True(binding.Resolution.HasExtensions);
        Assert.True(binding.Resolution.RequiresCircuitValidation);
        Assert.Equal(1, binding.Resolution.ExtensionStatementCount);
        Assert.Same(
            extensionStatement,
            Assert.IsType<BenchTerminalMapping>(binding.Binding.Statements[1])
        );
    }

    [Fact]
    public void ResolveForCircuit_ReportsDuplicateInheritedBindingNames()
    {
        var first = new TraitDefinition
        {
            Name = "First",
            BenchBindings = [new BenchBinding { BenchName = "A", BindingName = "shared_bench" }],
        };
        var second = new TraitDefinition
        {
            Name = "Second",
            BenchBindings = [new BenchBinding { BenchName = "B", BindingName = "shared_bench" }],
        };
        var circuit = new Circuit { Name = "Amp", Traits = ["First", "Second"] };

        var result = BenchBindingResolver.ResolveForCircuit(
            circuit,
            BuildInterfaces(first, second)
        );

        Assert.Equal(["shared_bench"], result.DuplicateInheritedBindingNames);
    }

    [Fact]
    public void ResolveForCircuit_ReportsUnknownExtensionTargets()
    {
        var circuit = new Circuit
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
        };

        var result = BenchBindingResolver.ResolveForCircuit(
            circuit,
            new Dictionary<string, TraitDefinition>(StringComparer.OrdinalIgnoreCase)
        );

        Assert.Equal(["missing_bench"], result.UnknownExtensionTargets);
    }

    private static IReadOnlyDictionary<string, TraitDefinition> BuildInterfaces(
        params TraitDefinition[] interfaces
    )
    {
        return interfaces.ToDictionary(
            interfaceDef => interfaceDef.Name,
            StringComparer.OrdinalIgnoreCase
        );
    }
}
