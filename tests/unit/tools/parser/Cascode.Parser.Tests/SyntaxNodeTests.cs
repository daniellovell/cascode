using System;
using System.Collections.Generic;
using Xunit;

namespace Cascode.Parser.Tests;

public class SyntaxNodeTests
{
    [Fact]
    public void CompilationUnitSyntax_Constructor_GuardsAgainstNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new CompilationUnitSyntax(
            "file", 1, 1, null, null!, new List<MemberDeclarationSyntax>()));

        Assert.Throws<ArgumentNullException>(() => new CompilationUnitSyntax(
            "file", 1, 1, null, new List<ImportDeclarationSyntax>(), null!));
    }

    [Fact]
    public void CompilationUnitSyntax_Constructor_CopiesLists()
    {
        var imports = new List<ImportDeclarationSyntax>();
        var members = new List<MemberDeclarationSyntax>();

        var cu = new CompilationUnitSyntax(
            "file", 1, 1, null, imports, members);

        // Mutate original lists
        imports.Add(new ImportDeclarationSyntax("f", 1, 1, "imp", false));

        // Assert AST is unchanged
        Assert.Empty(cu.Imports);
    }

    [Fact]
    public void TraitDeclarationSyntax_Constructor_GuardsAndCopies()
    {
        Assert.Throws<ArgumentNullException>(() => new TraitDeclarationSyntax(
            "file", 1, 1, null!, new List<string>()));

        Assert.Throws<ArgumentNullException>(() => new TraitDeclarationSyntax(
            "file", 1, 1, "name", null!));

        var extends = new List<string> { "Base" };
        var trait = new TraitDeclarationSyntax("file", 1, 1, "Name", extends);

        extends.Add("Other");

        Assert.Single(trait.Extends);
        Assert.Equal("Base", trait.Extends[0]);
    }

    [Fact]
    public void MotifDeclarationSyntax_Constructor_GuardsAndCopies()
    {
        var implements = new List<string>();
        var ports = new List<PortDeclarationSyntax>();
        var supplies = new List<SupplyDeclarationSyntax>();
        var grounds = new List<GroundDeclarationSyntax>();

        Assert.Throws<ArgumentNullException>(() => new MotifDeclarationSyntax(
            "file", 1, 1, null!, implements, ports, supplies, grounds, null));

        var motif = new MotifDeclarationSyntax(
            "file", 1, 1, "Name", implements, ports, supplies, grounds, null);

        implements.Add("Interface");
        ports.Add(new PortDeclarationSyntax("f", 1, 1, "p", "in"));

        Assert.Empty(motif.Implements);
        Assert.Empty(motif.Ports);
    }

    [Fact]
    public void UseBlockSyntax_Constructor_GuardsAndCopies()
    {
        var statements = new List<UseStatementSyntax>();

        Assert.Throws<ArgumentNullException>(() => new UseBlockSyntax(
            "file", 1, 1, null!));

        var useBlock = new UseBlockSyntax("file", 1, 1, statements);

        statements.Add(new InstanceDeclarationSyntax("f", 1, 1, "inst", "Type"));

        Assert.Empty(useBlock.Statements);
    }
}

