namespace Cascode.Cli.Commands;

internal interface IPdkEmitCommandHandlers
{
    CommandResult ShowPdkEmitUsage(string[] args);
    CommandResult PdkEmitPrimitivesCommand(string[] args);
}

internal interface IPdkCharacterizationCommandHandlers
{
    CommandResult ShowPdkCharUsage(string[] args);
    CommandResult PdkCharConfigCommand(string[] args);
    CommandResult PdkCharRunCommand(string[] args);
    CommandResult PdkCharReadCommand(string[] args);
    CommandResult PdkCharStatusCommand(string[] args);
}
