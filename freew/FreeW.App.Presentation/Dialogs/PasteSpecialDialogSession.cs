namespace FreeW.App.Presentation.Dialogs;

public sealed record PasteSpecialDialogState(
    int SelectedIndex,
    string Description);

/// <summary>
/// Owns the shared Paste Special option selection, description projection, and acceptance result.
/// Native clipboard access remains in each host because the clipboard APIs are platform-specific.
/// </summary>
public sealed class PasteSpecialDialogSession
{
    private PasteSpecialDialogState _state;

    public PasteSpecialDialogSession()
    {
        if (PasteSpecialOptionCatalog.Options.Count == 0)
            throw new InvalidOperationException("Paste Special requires at least one option.");

        _state = BuildState(0);
    }

    public const string Title = "Paste Special";
    public const string PasteAsLabel = "Paste As:";
    public const string EmptyClipboardMessage =
        "The clipboard is empty or does not contain text that can be pasted.";

    public IReadOnlyList<PasteSpecialOptionChoice> Options => PasteSpecialOptionCatalog.Options;

    public PasteSpecialDialogState State => _state;

    public PasteSpecialDialogState UpdateSelection(int selectedIndex)
    {
        _state = BuildState(selectedIndex);
        return _state;
    }

    public PasteSpecialOption? PlanAcceptance()
    {
        if (_state.SelectedIndex < 0 || _state.SelectedIndex >= Options.Count)
            return null;

        return Options[_state.SelectedIndex].Option;
    }

    private PasteSpecialDialogState BuildState(int selectedIndex)
    {
        var description = selectedIndex >= 0 && selectedIndex < Options.Count
            ? Options[selectedIndex].Description
            : string.Empty;
        return new PasteSpecialDialogState(selectedIndex, description);
    }
}
