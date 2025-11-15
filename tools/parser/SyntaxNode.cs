using System;
using System.Collections.Generic;

namespace Cascode.Parser;

public abstract class SyntaxNode
{
    protected SyntaxNode(string filePath, int line, int column)
    {
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
    }

    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
}

public sealed class CompilationUnitSyntax : SyntaxNode
{
    public CompilationUnitSyntax(
        string filePath,
        int line,
        int column,
        PackageDeclarationSyntax? package,
        IReadOnlyList<ImportDeclarationSyntax> imports,
        IReadOnlyList<MemberDeclarationSyntax> members) : base(filePath, line, column)
    {
        Package = package;
        Imports = imports;
        Members = members;
    }

    public PackageDeclarationSyntax? Package { get; }
    public IReadOnlyList<ImportDeclarationSyntax> Imports { get; }
    public IReadOnlyList<MemberDeclarationSyntax> Members { get; }
}

public sealed class PackageDeclarationSyntax : SyntaxNode
{
    public PackageDeclarationSyntax(string filePath, int line, int column, string name)
        : base(filePath, line, column)
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class ImportDeclarationSyntax : SyntaxNode
{
    public ImportDeclarationSyntax(string filePath, int line, int column, string name, bool isWildcard)
        : base(filePath, line, column)
    {
        Name = name;
        IsWildcard = isWildcard;
    }

    public string Name { get; }
    public bool IsWildcard { get; }
}

public abstract class MemberDeclarationSyntax : SyntaxNode
{
    protected MemberDeclarationSyntax(string filePath, int line, int column)
        : base(filePath, line, column)
    {
    }
}

public sealed class TraitDeclarationSyntax : MemberDeclarationSyntax
{
    public TraitDeclarationSyntax(
        string filePath,
        int line,
        int column,
        string name,
        IReadOnlyList<string> extends)
        : base(filePath, line, column)
    {
        Name = name;
        Extends = extends;
    }

    public string Name { get; }
    public IReadOnlyList<string> Extends { get; }
}

public sealed class MotifDeclarationSyntax : MemberDeclarationSyntax
{
    public MotifDeclarationSyntax(
        string filePath,
        int line,
        int column,
        string name,
        IReadOnlyList<string> implements,
        IReadOnlyList<PortDeclarationSyntax> ports,
        IReadOnlyList<SupplyDeclarationSyntax> supplies,
        IReadOnlyList<GroundDeclarationSyntax> grounds,
        UseBlockSyntax? useBlock)
        : base(filePath, line, column)
    {
        Name = name;
        Implements = implements;
        Ports = ports;
        Supplies = supplies;
        Grounds = grounds;
        UseBlock = useBlock;
    }

    public string Name { get; }
    public IReadOnlyList<string> Implements { get; }
    public IReadOnlyList<PortDeclarationSyntax> Ports { get; }
    public IReadOnlyList<SupplyDeclarationSyntax> Supplies { get; }
    public IReadOnlyList<GroundDeclarationSyntax> Grounds { get; }
    public UseBlockSyntax? UseBlock { get; }
}

public sealed class PortDeclarationSyntax : SyntaxNode
{
    public PortDeclarationSyntax(string filePath, int line, int column, string name, string kind)
        : base(filePath, line, column)
    {
        Name = name;
        Kind = kind;
    }

    public string Name { get; }
    public string Kind { get; }
}

public sealed class SupplyDeclarationSyntax : SyntaxNode
{
    public SupplyDeclarationSyntax(string filePath, int line, int column, string name)
        : base(filePath, line, column)
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class GroundDeclarationSyntax : SyntaxNode
{
    public GroundDeclarationSyntax(string filePath, int line, int column, string name)
        : base(filePath, line, column)
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class UseBlockSyntax : SyntaxNode
{
    public UseBlockSyntax(string filePath, int line, int column, IReadOnlyList<UseStatementSyntax> statements)
        : base(filePath, line, column)
    {
        Statements = statements;
    }

    public IReadOnlyList<UseStatementSyntax> Statements { get; }
}

public abstract class UseStatementSyntax : SyntaxNode
{
    protected UseStatementSyntax(string filePath, int line, int column)
        : base(filePath, line, column)
    {
    }
}

public sealed class InstanceDeclarationSyntax : UseStatementSyntax
{
    public InstanceDeclarationSyntax(
        string filePath,
        int line,
        int column,
        string instanceName,
        string typeName)
        : base(filePath, line, column)
    {
        InstanceName = instanceName;
        TypeName = typeName;
    }

    public string InstanceName { get; }
    public string TypeName { get; }
}

public sealed class AttachStatementSyntax : UseStatementSyntax
{
    public AttachStatementSyntax(
        string filePath,
        int line,
        int column,
        string SourceInstance,
        string TargetInstance)
        : base(filePath, line, column)
    {
        Source = SourceInstance;
        Target = TargetInstance;
    }

    public string Source { get; }
    public string Target { get; }
}

public sealed class ConnectStatementSyntax : UseStatementSyntax
{
    public ConnectStatementSyntax(
        string filePath,
        int line,
        int column,
        string From,
        string To)
        : base(filePath, line, column)
    {
        FromPin = From;
        ToPin = To;
    }

    public string FromPin { get; }
    public string ToPin { get; }
}
