using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Completes renderer-provided native command ports against the canonical FreeW action catalog.
/// Presentation owns route coverage and fallback state; renderers own only concrete editor, dialog,
/// focus, and control adapters.
/// </summary>
public static class FreeWRibbonExecutionProfile
{
    public static FreeWRibbonCommandBuildResult Build(FreeWRibbonCommandBindingPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        var completed = new FreeWRibbonCommandBindingPorts();
        foreach (var action in Enum.GetValues<FreeWRibbonCommandAction>())
        {
            completed.Bind(
                action,
                ports.CanonicalBindings.TryGetValue(action, out var command)
                    ? command
                    : UnavailableCommand.Instance);
        }

        foreach (var (commandId, command) in ports.AdapterBindings)
            completed.Register(commandId, command);

        return FreeWRibbonCommandWorkflow.Build(completed);
    }

    private sealed class UnavailableCommand : IRibbonStatefulCommand
    {
        public static UnavailableCommand Instance { get; } = new();

        private UnavailableCommand()
        {
        }

        public void Execute(RibbonCommandContext context)
        {
        }

        public RibbonCommandState GetState() => new(IsEnabled: false);
    }
}
