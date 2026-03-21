namespace Cascode.Cli.Commands;

internal sealed class PdkCharacterizationCommandModule : ICommandModule
{
    private readonly IPdkCharacterizationCommandHandlers _handlers;

    public PdkCharacterizationCommandModule(IPdkCharacterizationCommandHandlers handlers)
    {
        _handlers = handlers;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                "pdk char",
                "PDK characterization commands",
                _handlers.ShowPdkCharUsage,
                helpCategory: CommandHelpCategory.PdkCharacterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char help",
                "Show PDK characterization help",
                _handlers.ShowPdkCharUsage,
                hidden: true,
                helpCategory: CommandHelpCategory.PdkCharacterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char config",
                "Configure batch characterization",
                _handlers.PdkCharConfigCommand,
                helpCategory: CommandHelpCategory.PdkCharacterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char run",
                "Characterize devices",
                _handlers.PdkCharRunCommand,
                helpCategory: CommandHelpCategory.PdkCharacterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char read",
                "View characterized LUTs",
                _handlers.PdkCharReadCommand,
                helpCategory: CommandHelpCategory.PdkCharacterization
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk char status",
                "Show characterization coverage",
                _handlers.PdkCharStatusCommand,
                helpCategory: CommandHelpCategory.PdkCharacterization
            )
        );
    }
}
