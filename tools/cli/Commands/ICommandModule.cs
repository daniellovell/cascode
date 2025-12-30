using System;

namespace Cascode.Cli.Commands;

// Placeholder for future modularization. For now modules only register
// commands; dependencies will be provided by the host at composition time.
internal interface ICommandModule
{
    void Register(CommandRegistry registry);
}
