using System;
using System.Collections.Generic;

namespace Cascode.Cli.Commands;

// Minimal transitional CLI command contract. This allows us to register
// cohesive command objects while keeping existing handlers intact.
internal interface ICliCommand
{
    string Path { get; }
    string Description { get; }
    bool Hidden { get; }
    IReadOnlyList<string> Aliases { get; }
    CommandHandler Handler { get; }
}
