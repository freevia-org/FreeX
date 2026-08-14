using Free.Shared.Ribbon;

namespace FreeP.App.Compositor;

/// <summary>
/// Owns the renderer-neutral lifetime of FreeP's ribbon registry and state store.
/// Renderers keep only native control construction; editor replacement and command-state
/// projection remain identical across WPF and Avalonia.
/// </summary>
public sealed class FreePRibbonBindingSession
{
    private readonly RibbonStateStore _stateStore;
    private readonly Func<FreePRibbonHostProfile> _profileFactory;
    private IReadOnlyList<RibbonCommandId> _commandIds = [];

    public FreePRibbonBindingSession(
        EditingSession editor,
        RibbonStateStore stateStore,
        Func<FreePRibbonHostProfile> profileFactory)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _profileFactory = profileFactory ?? throw new ArgumentNullException(nameof(profileFactory));

        var result = FreePRibbonHostRegistryComposer.Build(editor, _stateStore, _profileFactory());
        Registry = result.Registry;
        _commandIds = result.AllCommandIds;
        SyncCommandStates();
    }

    public RibbonCommandRegistry Registry { get; }

    public IRibbonStateStore StateStore => _stateStore;

    /// <summary>Retargets the existing renderer-bound registry to a replacement document editor.</summary>
    public void Rebind(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var result = FreePRibbonHostRegistryComposer.BindInto(
            Registry,
            editor,
            _stateStore,
            _profileFactory());
        _commandIds = result.AllCommandIds;
        SyncCommandStates();
    }

    /// <summary>Projects every stateful command into the store consumed by either renderer.</summary>
    public void SyncCommandStates()
    {
        foreach (var commandId in _commandIds.Distinct())
        {
            if (Registry.TryGet(commandId, out var command) &&
                command is IRibbonStatefulCommand stateful)
            {
                _stateStore.SetState(commandId, stateful.GetState());
            }
        }
    }
}
