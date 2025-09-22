using System;
using System.Collections.Generic;
using System.Linq;

namespace Cascode.Cli;

internal delegate CommandResult CommandHandler(string[] args);

internal sealed class CommandDescriptor
{
    private readonly string[] _path;

    internal CommandDescriptor(string path, string description, CommandHandler handler, bool hidden = false, bool isAlias = false, CommandDescriptor? canonical = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided", nameof(path));
        }

        _path = Split(path);
        if (_path.Length == 0)
        {
            throw new ArgumentException("Command path cannot be empty", nameof(path));
        }

        Description = description ?? string.Empty;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Hidden = hidden;
        IsAlias = isAlias;
        Canonical = canonical;
    }

    public string Description { get; }
    public CommandHandler Handler { get; }
    public bool Hidden { get; }
    public bool IsAlias { get; }
    public CommandDescriptor? Canonical { get; }

    public IReadOnlyList<string> Tokens => Canonical?._path ?? _path;

    public string DisplayPath => string.Join(' ', Tokens);

    internal IReadOnlyList<string> OwnTokens => _path;

    public bool StartsWith(IReadOnlyList<string> prefix)
    {
        if (prefix.Count > Tokens.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(Tokens[i], prefix[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Split(string path) => path
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class CommandRegistry
{
    private readonly List<CommandDescriptor> _descriptors = new();
    private readonly Dictionary<string, CommandDescriptor> _lookup = new(StringComparer.OrdinalIgnoreCase);
    private int _maxPathLength;

    public CommandDescriptor Register(string path, string description, CommandHandler handler, bool hidden = false, params string[] aliases)
    {
        var descriptor = new CommandDescriptor(path, description, handler, hidden);
        AddDescriptor(descriptor);

        foreach (var alias in aliases ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            var aliasDescriptor = new CommandDescriptor(alias, description, handler, hidden: true, isAlias: true, canonical: descriptor);
            AddDescriptor(aliasDescriptor);
        }

        return descriptor;
    }

    public bool TryResolve(IReadOnlyList<string> tokens, out CommandDescriptor? descriptor, out string[] args, out int matchedLength)
    {
        descriptor = null;
        args = Array.Empty<string>();
        matchedLength = 0;

        if (tokens.Count == 0)
        {
            return false;
        }

        var max = Math.Min(tokens.Count, _maxPathLength);
        for (var length = max; length >= 1; length--)
        {
            var key = MakeKey(tokens, length);
            if (_lookup.TryGetValue(key, out var found))
            {
                descriptor = found.Canonical ?? found;
                args = tokens.Skip(length).ToArray();
                matchedLength = length;
                return true;
            }
        }

        matchedLength = FindLongestPrefix(tokens);
        return false;
    }

    public IEnumerable<CommandDescriptor> GetCanonicalCommands()
    {
        return _descriptors.Where(d => !d.IsAlias && !d.Hidden)
            .OrderBy(d => d.DisplayPath, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<CommandDescriptor> GetSubcommands(IReadOnlyList<string> prefix)
    {
        return _descriptors
            .Where(d => !d.IsAlias && !d.Hidden && d.Tokens.Count > prefix.Count && d.StartsWith(prefix))
            .OrderBy(d => d.DisplayPath, StringComparer.OrdinalIgnoreCase);
    }

    private void AddDescriptor(CommandDescriptor descriptor)
    {
        var key = MakeKey(descriptor.OwnTokens);
        if (_lookup.ContainsKey(key))
        {
            throw new InvalidOperationException($"Command '{descriptor.DisplayPath}' is already registered.");
        }

        _lookup[key] = descriptor;
        _descriptors.Add(descriptor);
        _maxPathLength = Math.Max(_maxPathLength, descriptor.OwnTokens.Count);
    }

    private static string MakeKey(IReadOnlyList<string> tokens, int length)
    {
        var span = tokens.Take(length);
        return string.Join('\u0001', span);
    }

    private static string MakeKey(IReadOnlyList<string> tokens)
    {
        return string.Join('\u0001', tokens);
    }

    private int FindLongestPrefix(IReadOnlyList<string> tokens)
    {
        var max = Math.Min(tokens.Count, _maxPathLength);
        for (var length = max; length >= 1; length--)
        {
            var prefix = tokens.Take(length).ToArray();
            if (_descriptors.Any(d => !d.IsAlias && d.StartsWith(prefix)))
            {
                return length;
            }
        }

        return 0;
    }
}
