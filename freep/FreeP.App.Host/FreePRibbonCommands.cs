using Free.Shared.Ribbon;

namespace FreeP.App.Host;

/// <summary>
/// Binds FreeP's ribbon command ids (declared in <see cref="FreePRibbon"/>) to behavior, implementing the
/// shared <see cref="IRibbonCommandRegistry"/>. Scaffold-level: New Slide drives the real host action; the
/// remaining ids are registered as harmless no-op stubs so every button is enabled and clickable (the
/// presentation-domain session replaces the stubs with real edits). Stateful toggles (bold/italic/underline)
/// flip a local flag and reflect it back through the <see cref="RibbonStateStore"/> so the ribbon shows the
/// pressed state, proving the stateful-command + state-store wiring end-to-end.
/// </summary>
internal static class FreePRibbonCommands
{
    public static RibbonCommandRegistry Build(RibbonStateStore stateStore, Action onNewSlide)
    {
        var registry = new RibbonCommandRegistry();

        // The one real command in the scaffold: New Slide appends a slide via the host (undoable command bus).
        registry.Register("freep.new-slide", new ActionCommand(onNewSlide));

        // Stub action commands — clickable no-ops until the domain lands.
        foreach (var id in new[]
        {
            "freep.duplicate-slide", "freep.delete-slide", "freep.layout",
            "freep.paste", "freep.cut", "freep.copy",
            "freep.font-family",
            "freep.text-box", "freep.picture", "freep.shape-rectangle", "freep.shape-ellipse",
        })
        {
            registry.Register(id, new ActionCommand(() => { /* stub: wired in the presentation-domain session */ }));
        }

        // Stateful toggles reflect their checked state back through the store so the ribbon renders pressed.
        Register(registry, stateStore, "freep.bold");
        Register(registry, stateStore, "freep.italic");
        Register(registry, stateStore, "freep.underline");

        return registry;
    }

    private static void Register(RibbonCommandRegistry registry, RibbonStateStore stateStore, RibbonCommandId id) =>
        registry.Register(id, new ToggleStubCommand(id, stateStore));

    /// <summary>A fire-and-forget command over a plain delegate.</summary>
    private sealed class ActionCommand : IRibbonCommand
    {
        private readonly Action _action;
        public ActionCommand(Action action) => _action = action;
        public void Execute(RibbonCommandContext context) => _action();
    }

    /// <summary>A stub stateful toggle: flips a local checked flag and publishes it to the state store.</summary>
    private sealed class ToggleStubCommand : IRibbonStatefulCommand
    {
        private readonly RibbonCommandId _id;
        private readonly RibbonStateStore _stateStore;
        private bool _checked;

        public ToggleStubCommand(RibbonCommandId id, RibbonStateStore stateStore)
        {
            _id = id;
            _stateStore = stateStore;
        }

        public void Execute(RibbonCommandContext context)
        {
            _checked = !_checked;
            _stateStore.SetChecked(_id, _checked);
        }

        public RibbonCommandState GetState() => new(IsEnabled: true, IsChecked: _checked);
    }
}
