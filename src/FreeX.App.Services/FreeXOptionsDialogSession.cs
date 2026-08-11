using FreeX.App.Services.Ribbon;

namespace FreeX.App.Services;

/// <summary>
/// Renderer-neutral lifetime for the WPF and Avalonia FreeX Options dialogs. Native views render
/// controls and collect primitive values; this session owns the open snapshot, mutable dictionary
/// and Quick Access state, projection, merge, persistence, and runtime adoption.
/// </summary>
public sealed class FreeXOptionsDialogSession
{
    private readonly FreeXOptionsRuntimeSession _runtimeSession;

    internal FreeXOptionsDialogSession(
        FreeXOptionsRuntimeSession runtimeSession,
        AppOptions openSnapshot)
    {
        _runtimeSession = runtimeSession ?? throw new ArgumentNullException(nameof(runtimeSession));
        OpenSnapshot = openSnapshot ?? throw new ArgumentNullException(nameof(openSnapshot));
        QuickAccessToolbar = new QuickAccessToolbarOptionsSession(
            openSnapshot.QuickAccessToolbarCommands,
            openSnapshot.QuickAccessToolbarBelowRibbon);
        CustomDictionary = new CustomDictionaryEditorSession(
            openSnapshot.SpellCheckCustomDictionaryWords);
    }

    public AppOptions OpenSnapshot { get; }

    public QuickAccessToolbarOptionsSession QuickAccessToolbar { get; }

    public CustomDictionaryEditorSession CustomDictionary { get; }

    public FreeXOptionsPersistenceResult Commit(
        OptionsDialogPlanner.OptionsDialogInput input,
        bool enableFillHandleAndCellDragAndDrop,
        bool enableAutoCompleteForCellValues,
        bool quickAccessToolbarBelowRibbon,
        bool? formulaBarExpanded = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        QuickAccessToolbar.SetPlacement(quickAccessToolbarBelowRibbon);
        var edited = OptionsDialogPlanner.Project(
            OpenSnapshot,
            input,
            new OptionsDialogPlanner.OptionsDialogSupplementalInput(
                enableFillHandleAndCellDragAndDrop,
                enableAutoCompleteForCellValues,
                QuickAccessToolbar.QuickAccessToolbarBelowRibbon,
                QuickAccessToolbar.CommandIds,
                CustomDictionary.Model.Words,
                formulaBarExpanded));

        return _runtimeSession.CommitDialog(OpenSnapshot, edited);
    }
}
