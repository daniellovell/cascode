using System;
using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;

namespace Cascode.Parser;

/// <summary>
/// Translates ANTLR parse trees into immutable Cascode syntax nodes.
/// </summary>
internal sealed class CascodeAstBuilder
{
    private readonly string _filePath;

    /// <summary>
    /// Initializes a builder bound to a specific source file used for location data.
    /// </summary>
    /// <param name="filePath">Source file path.</param>
    public CascodeAstBuilder(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Builds the root <see cref="CompilationUnitSyntax"/> from the ANTLR parse context.
    /// </summary>
    public CompilationUnitSyntax Build(CascodeParser.CompilationUnitContext context)
    {
        var package = context.packageDecl() is { } pkgCtx ? BuildPackage(pkgCtx) : null;
        var imports = context.importDecl().Select(BuildImport).ToList();
        var members = new List<MemberDeclarationSyntax>();

        foreach (var declCtx in context.declaration())
        {
            if (declCtx.traitDecl() is { } t)
            {
                members.Add(BuildTrait(t));
            }
            else if (declCtx.motifDecl() is { } m)
            {
                members.Add(BuildMotif(m));
            }
        }

        return new CompilationUnitSyntax(
            _filePath,
            1,
            1,
            package,
            imports,
            members);
    }

    private PackageDeclarationSyntax BuildPackage(CascodeParser.PackageDeclContext ctx)
    {
        var name = BuildQualifiedName(ctx.qualifiedName());
        var (line, column) = GetLocation(ctx.Start);
        return new PackageDeclarationSyntax(_filePath, line, column, name);
    }

    private ImportDeclarationSyntax BuildImport(CascodeParser.ImportDeclContext ctx)
    {
        var name = BuildQualifiedName(ctx.qualifiedName());
        var isWildcard = ctx.GetText().Contains(".*", StringComparison.Ordinal);
        var (line, column) = GetLocation(ctx.Start);
        return new ImportDeclarationSyntax(_filePath, line, column, name, isWildcard);
    }

    private TraitDeclarationSyntax BuildTrait(CascodeParser.TraitDeclContext ctx)
    {
        var name = ctx.Identifier().GetText();
        var extends = new List<string>();

        // All qualifiedName() children after 'extend' are extends targets.
        foreach (var q in ctx.qualifiedName())
        {
            extends.Add(BuildQualifiedName(q));
        }

        var (line, column) = GetLocation(ctx.Start);
        return new TraitDeclarationSyntax(_filePath, line, column, name, extends);
    }

    private MotifDeclarationSyntax BuildMotif(CascodeParser.MotifDeclContext ctx)
    {
        var name = ctx.Identifier().GetText();
        var implements = new List<string>();

        foreach (var q in ctx.qualifiedName())
        {
            implements.Add(BuildQualifiedName(q));
        }

        var ports = new List<PortDeclarationSyntax>();
        var supplies = new List<SupplyDeclarationSyntax>();
        var grounds = new List<GroundDeclarationSyntax>();
        UseBlockSyntax? useBlock = null;

        foreach (var member in ctx.motifBody().motifMember())
        {
            if (member.portsSquare() is { } portsCtx)
            {
                ports.AddRange(BuildPorts(portsCtx));
            }
            else if (member.supplyDecl() is { } supplyCtx)
            {
                supplies.Add(BuildSupply(supplyCtx));
            }
            else if (member.groundDecl() is { } groundCtx)
            {
                grounds.Add(BuildGround(groundCtx));
            }
            else if (member.useBlock() is { } useCtx)
            {
                useBlock = BuildUseBlock(useCtx);
            }
        }

        var (line, column) = GetLocation(ctx.Start);
        return new MotifDeclarationSyntax(
            _filePath,
            line,
            column,
            name,
            implements,
            ports,
            supplies,
            grounds,
            useBlock);
    }

    private IEnumerable<PortDeclarationSyntax> BuildPorts(CascodeParser.PortsSquareContext ctx)
    {
        var list = ctx.portList();
        if (list is null)
        {
            yield break;
        }

        foreach (var pd in list.portDecl())
        {
            var name = pd.Identifier(0).GetText();
            var kind = pd.Identifier(1).GetText();
            var (line, column) = GetLocation(pd.Start);
            yield return new PortDeclarationSyntax(_filePath, line, column, name, kind);
        }
    }

    private SupplyDeclarationSyntax BuildSupply(CascodeParser.SupplyDeclContext ctx)
    {
        var name = ctx.Identifier().GetText();
        var (line, column) = GetLocation(ctx.Start);
        return new SupplyDeclarationSyntax(_filePath, line, column, name);
    }

    private GroundDeclarationSyntax BuildGround(CascodeParser.GroundDeclContext ctx)
    {
        var name = ctx.Identifier().GetText();
        var (line, column) = GetLocation(ctx.Start);
        return new GroundDeclarationSyntax(_filePath, line, column, name);
    }

    private UseBlockSyntax BuildUseBlock(CascodeParser.UseBlockContext ctx)
    {
        var statements = new List<UseStatementSyntax>();

        foreach (var stmt in ctx.useStatement())
        {
            if (stmt.instanceDecl() is { } instCtx)
            {
                statements.Add(BuildInstance(instCtx));
            }
            else if (stmt.attachStmt() is { } attachCtx)
            {
                statements.Add(BuildAttach(attachCtx));
            }
            else if (stmt.connectStmt() is { } connCtx)
            {
                statements.Add(BuildConnect(connCtx));
            }
        }

        var (line, column) = GetLocation(ctx.Start);
        return new UseBlockSyntax(_filePath, line, column, statements);
    }

    private InstanceDeclarationSyntax BuildInstance(CascodeParser.InstanceDeclContext ctx)
    {
        var ids = ctx.Identifier();
        var instanceName = ids[0].GetText();
        var typeName = ids.Length > 1 ? ids[1].GetText() : string.Empty;
        var (line, column) = GetLocation(ctx.Start);
        return new InstanceDeclarationSyntax(_filePath, line, column, instanceName, typeName);
    }

    private AttachStatementSyntax BuildAttach(CascodeParser.AttachStmtContext ctx)
    {
        var source = ctx.Identifier(0).GetText();
        var target = ctx.Identifier(1).GetText();
        var (line, column) = GetLocation(ctx.Start);
        return new AttachStatementSyntax(_filePath, line, column, source, target);
    }

    private ConnectStatementSyntax BuildConnect(CascodeParser.ConnectStmtContext ctx)
    {
        var from = ctx.pinRef(0).GetText();
        var to = ctx.pinRef(1).GetText();
        var (line, column) = GetLocation(ctx.Start);
        return new ConnectStatementSyntax(_filePath, line, column, from, to);
    }

    private static (int Line, int Column) GetLocation(IToken token)
    {
        return (token.Line, token.Column + 1);
    }

    private static string BuildQualifiedName(CascodeParser.QualifiedNameContext ctx)
    {
        return string.Join(".", ctx.Identifier().Select(id => id.GetText()));
    }
}
