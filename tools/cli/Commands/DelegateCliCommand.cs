using System;
using System.Collections.Generic;

namespace Cascode.Cli.Commands;

// Simple adapter that wraps existing handlers into ICliCommand instances.
internal sealed class DelegateCliCommand : ICliCommand
{
    public DelegateCliCommand(
        string path,
        string description,
        CommandHandler handler,
        bool hidden = false,
        CommandHelpCategory helpCategory = CommandHelpCategory.Uncategorized,
        IReadOnlyList<string>? aliases = null
    )
    {
        Path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Path is required", nameof(path))
            : path;
        Description = description ?? string.Empty;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        HelpCategory = helpCategory;
        Hidden = hidden;
        Aliases = aliases ?? Array.Empty<string>();
    }

    public string Path { get; }
    public string Description { get; }
    public CommandHelpCategory HelpCategory { get; }
    public bool Hidden { get; }
    public IReadOnlyList<string> Aliases { get; }
    public CommandHandler Handler { get; }
}
