using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Completes renderer-provided native command ports against the canonical FreeW action catalog.
/// Presentation owns route coverage and fallback state; renderers own only concrete editor, dialog,
/// focus, and control adapters.
/// </summary>
public static class FreeWRibbonExecutionProfile
{
    public static IRibbonCommand UnavailableCommand => UnavailableCommandImpl.Instance;

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
                    : UnavailableCommandImpl.Instance);
        }

        foreach (var (commandId, command) in ports.AdapterBindings)
            completed.Register(commandId, command);

        return FreeWRibbonCommandWorkflow.Build(completed);
    }

    private sealed class UnavailableCommandImpl : IRibbonStatefulCommand
    {
        public static UnavailableCommandImpl Instance { get; } = new();

        private UnavailableCommandImpl()
        {
        }

        public void Execute(RibbonCommandContext context)
        {
        }

        public RibbonCommandState GetState() => new(IsEnabled: false);
    }
}
