namespace Cascode.Cli.Commands;

internal sealed class PdkEmitCommandModule : ICommandModule
{
    private readonly IPdkEmitCommandHandlers _handlers;

    public PdkEmitCommandModule(IPdkEmitCommandHandlers handlers)
    {
        _handlers = handlers;
    }

    public void Register(CommandRegistry registry)
    {
        registry.Register(
            new DelegateCliCommand(
                "pdk emit",
                "Emit derived PDK artifacts",
                _handlers.ShowPdkEmitUsage,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
        registry.Register(
            new DelegateCliCommand(
                "pdk emit primitives",
                "Generate a Cascode primitive library from pdk.db",
                _handlers.PdkEmitPrimitivesCommand,
                helpCategory: CommandHelpCategory.Pdk
            )
        );
    }
}
