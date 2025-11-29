using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Cascode.Parser;

/// <summary>
/// Base type for all syntax nodes produced by the Cascode parser.
/// </summary>
public abstract class SyntaxNode
{
    protected SyntaxNode(string filePath, int line, int column)
    {
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
    }

    /// <summary>Path of the source file that defined the node.</summary>
    public string FilePath { get; }

    /// <summary>1-based line number of the first token.</summary>
    public int Line { get; }

    /// <summary>1-based column number of the first token.</summary>
    public int Column { get; }
}

/// <summary>
/// Root syntax node for a single Cascode compilation unit.
/// </summary>
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
        ArgumentNullException.ThrowIfNull(imports);
        ArgumentNullException.ThrowIfNull(members);

        Package = package;
        Imports = imports.ToList().AsReadOnly();
        Members = members.ToList().AsReadOnly();
    }

    /// <summary>Optional package declaration.</summary>
    public PackageDeclarationSyntax? Package { get; }

    /// <summary>List of import declarations in source order.</summary>
    public IReadOnlyList<ImportDeclarationSyntax> Imports { get; }

    /// <summary>Top-level member declarations (traits, motifs).</summary>
    public IReadOnlyList<MemberDeclarationSyntax> Members { get; }
}

/// <summary>
/// Package declaration at the top of a compilation unit.
/// </summary>
public sealed class PackageDeclarationSyntax : SyntaxNode
{
    public PackageDeclarationSyntax(string filePath, int line, int column, string name)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Fully qualified package name.</summary>
    public string Name { get; }
}

/// <summary>
/// Single import statement.
/// </summary>
public sealed class ImportDeclarationSyntax : SyntaxNode
{
    public ImportDeclarationSyntax(string filePath, int line, int column, string name, bool isWildcard)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        IsWildcard = isWildcard;
    }

    /// <summary>Imported package or type name.</summary>
    public string Name { get; }

    /// <summary>True when the import ends with <c>.*</c>.</summary>
    public bool IsWildcard { get; }
}

/// <summary>
/// Base type for top-level declarations.
/// </summary>
public abstract class MemberDeclarationSyntax : SyntaxNode
{
    protected MemberDeclarationSyntax(string filePath, int line, int column)
        : base(filePath, line, column)
    {
    }
}

/// <summary>
/// Trait declaration node.
/// </summary>
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
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(extends);

        Name = name;
        Extends = extends.ToList().AsReadOnly();
    }

    /// <summary>Trait identifier.</summary>
    public string Name { get; }

    /// <summary>List of parent traits this trait extends.</summary>
    public IReadOnlyList<string> Extends { get; }
}

/// <summary>
/// Motif declaration node containing ports, supplies, and use block.
/// </summary>
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
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(implements);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(supplies);
        ArgumentNullException.ThrowIfNull(grounds);

        Name = name;
        Implements = implements.ToList().AsReadOnly();
        Ports = ports.ToList().AsReadOnly();
        Supplies = supplies.ToList().AsReadOnly();
        Grounds = grounds.ToList().AsReadOnly();
        UseBlock = useBlock;
    }

    /// <summary>Motif identifier.</summary>
    public string Name { get; }

    /// <summary>Traits implemented by this motif.</summary>
    public IReadOnlyList<string> Implements { get; }

    /// <summary>Declared ports in source order.</summary>
    public IReadOnlyList<PortDeclarationSyntax> Ports { get; }

    /// <summary>Supply declarations inside the motif body.</summary>
    public IReadOnlyList<SupplyDeclarationSyntax> Supplies { get; }

    /// <summary>Ground declarations inside the motif body.</summary>
    public IReadOnlyList<GroundDeclarationSyntax> Grounds { get; }

    /// <summary>Optional use block containing instances and connections.</summary>
    public UseBlockSyntax? UseBlock { get; }
}

/// <summary>
/// Port declaration node for a single terminal.
/// </summary>
public sealed class PortDeclarationSyntax : SyntaxNode
{
    public PortDeclarationSyntax(string filePath, int line, int column, string name, string kind)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(kind);
        Name = name;
        Kind = kind;
    }

    /// <summary>Port identifier.</summary>
    public string Name { get; }

    /// <summary>Port kind token; remains a string until kinds stabilize.</summary>
    // TODO: Consider converting Kind to enum once port kinds are finalized in spec
    public string Kind { get; }
}

/// <summary>
/// Represents a numeric literal with an optional unit suffix (e.g., 1.8V, 100MHz).
/// </summary>
public sealed class QuantityLiteralSyntax : SyntaxNode
{
    public QuantityLiteralSyntax(string filePath, int line, int column, double numericValue, string? unit)
        : base(filePath, line, column)
    {
        NumericValue = numericValue;
        Unit = unit;
    }

    /// <summary>The numeric portion of the literal.</summary>
    public double NumericValue { get; }

    /// <summary>The unit suffix (e.g., "V", "MHz"), or null if bare numeric.</summary>
    public string? Unit { get; }
}

/// <summary>
/// Supply rail declaration node.
/// </summary>
public sealed class SupplyDeclarationSyntax : SyntaxNode
{
    public SupplyDeclarationSyntax(string filePath, int line, int column, string name, QuantityLiteralSyntax? value = null)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Value = value;
    }

    /// <summary>Supply rail name.</summary>
    public string Name { get; }

    /// <summary>Optional voltage value with unit (e.g., 1.8V).</summary>
    public QuantityLiteralSyntax? Value { get; }
}

/// <summary>
/// Ground reference declaration node.
/// </summary>
public sealed class GroundDeclarationSyntax : SyntaxNode
{
    public GroundDeclarationSyntax(string filePath, int line, int column, string name)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Ground net name.</summary>
    public string Name { get; }
}

/// <summary>
/// Encloses instance, attach, and connect statements inside a motif.
/// </summary>
public sealed class UseBlockSyntax : SyntaxNode
{
    public UseBlockSyntax(string filePath, int line, int column, IReadOnlyList<UseStatementSyntax> statements)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(statements);
        Statements = statements.ToList().AsReadOnly();
    }

    /// <summary>Statements establishing motif structure.</summary>
    public IReadOnlyList<UseStatementSyntax> Statements { get; }
}

/// <summary>
/// Base type for statements within a use block.
/// </summary>
public abstract class UseStatementSyntax : SyntaxNode
{
    protected UseStatementSyntax(string filePath, int line, int column)
        : base(filePath, line, column)
    {
    }
}

/// <summary>
/// Represents a single parameter assignment in an instance declaration (e.g., p=NMOS).
/// </summary>
public sealed class InstanceParameterSyntax : SyntaxNode
{
    public InstanceParameterSyntax(string filePath, int line, int column, string name, string value)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
    }

    /// <summary>Parameter name (left side of =).</summary>
    public string Name { get; }

    /// <summary>Parameter value as string (right side of =).</summary>
    public string Value { get; }
}

/// <summary>
/// Declares a motif instance inside the containing motif.
/// </summary>
public sealed class InstanceDeclarationSyntax : UseStatementSyntax
{
    public InstanceDeclarationSyntax(
        string filePath,
        int line,
        int column,
        string instanceName,
        string typeName,
        IReadOnlyList<string>? constructorArgs = null,
        IReadOnlyList<InstanceParameterSyntax>? parameters = null,
        IReadOnlyList<BindingSyntax>? bindings = null)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(instanceName);
        ArgumentNullException.ThrowIfNull(typeName);
        InstanceName = instanceName;
        TypeName = typeName;
        ConstructorArgs = constructorArgs is null ? Array.Empty<string>() : constructorArgs.ToList().AsReadOnly();
        Parameters = parameters is null ? Array.Empty<InstanceParameterSyntax>() : parameters.ToList().AsReadOnly();
        Bindings = bindings is null ? Array.Empty<BindingSyntax>() : bindings.ToList().AsReadOnly();
    }

    /// <summary>Local instance identifier.</summary>
    public string InstanceName { get; }

    /// <summary>Fully qualified motif type name.</summary>
    public string TypeName { get; }

    /// <summary>Constructor arguments (e.g., for MOS(p), this would be ["p"]).</summary>
    public IReadOnlyList<string> ConstructorArgs { get; }

    /// <summary>Instance parameters (e.g., p=NMOS, hasTail=true).</summary>
    public IReadOnlyList<InstanceParameterSyntax> Parameters { get; }

    /// <summary>Inline port bindings (e.g., IN.P -> IN.P).</summary>
    public IReadOnlyList<BindingSyntax> Bindings { get; }
}

/// <summary>
/// Represents a single port binding in an instance (e.g., IN.P -> IN.P).
/// </summary>
public sealed class BindingSyntax : SyntaxNode
{
    public BindingSyntax(string filePath, int line, int column, string fromPin, string toPin)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(fromPin);
        ArgumentNullException.ThrowIfNull(toPin);
        FromPin = fromPin;
        ToPin = toPin;
    }

    /// <summary>Source pin reference (left side of ->).</summary>
    public string FromPin { get; }

    /// <summary>Target net or pin reference (right side of ->).</summary>
    public string ToPin { get; }
}

/// <summary>
/// Attaches one instance to another (v0 currently recorded only).
/// </summary>
public sealed class AttachStatementSyntax : UseStatementSyntax
{
    public AttachStatementSyntax(
        string filePath,
        int line,
        int column,
        string sourceInstance,
        string targetInstance)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(sourceInstance);
        ArgumentNullException.ThrowIfNull(targetInstance);
        Source = sourceInstance;
        Target = targetInstance;
    }

    /// <summary>Source instance identifier.</summary>
    public string Source { get; }

    /// <summary>Target instance identifier.</summary>
    public string Target { get; }
}

/// <summary>
/// Connects an instance pin path to a net or port.
/// </summary>
public sealed class ConnectStatementSyntax : UseStatementSyntax
{
    public ConnectStatementSyntax(
        string filePath,
        int line,
        int column,
        string from,
        string to)
        : base(filePath, line, column)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        FromPin = from;
        ToPin = to;
    }

    /// <summary>Left-hand side pin reference (e.g., <c>dp.OUT.N</c>).</summary>
    public string FromPin { get; }

    /// <summary>Right-hand side net or port identifier.</summary>
    public string ToPin { get; }
}
