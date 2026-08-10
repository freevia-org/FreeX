namespace FreeW.App.Presentation.Shell;

public sealed record FreeWApplicationCommandActions(
    Action NewDocument,
    Action OpenDocument,
    Action SaveDocument,
    Action SaveDocumentAs,
    Action PrintDocument,
    Action Find,
    Action Replace,
    Action Cut,
    Action Copy,
    Action Paste,
    Action PasteTextOnly,
    Action SelectAll,
    Action Undo,
    Action Redo,
    Action RevealFormatting,
    Action Thesaurus,
    Action LockCurrentField,
    Action UnlockCurrentField,
    Action UnlinkCurrentField,
    Action ToggleCurrentFieldCode,
    Action ToggleFieldCodes,
    Action UpdateCurrentField);

/// <summary>
/// Owns the application decision that maps FreeW commands to host-provided actions.
/// Key conversion and native editing/clipboard execution remain platform responsibilities.
/// </summary>
public sealed class FreeWApplicationCommandRouter
{
    private readonly FreeWApplicationCommandActions _actions;

    public FreeWApplicationCommandRouter(FreeWApplicationCommandActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public void Execute(FreeWKeyboardCommand command)
    {
        Action action = command switch
        {
            FreeWKeyboardCommand.NewDocument => _actions.NewDocument,
            FreeWKeyboardCommand.OpenDocument => _actions.OpenDocument,
            FreeWKeyboardCommand.SaveDocument => _actions.SaveDocument,
            FreeWKeyboardCommand.SaveDocumentAs => _actions.SaveDocumentAs,
            FreeWKeyboardCommand.PrintDocument => _actions.PrintDocument,
            FreeWKeyboardCommand.Find => _actions.Find,
            FreeWKeyboardCommand.Replace => _actions.Replace,
            FreeWKeyboardCommand.Cut => _actions.Cut,
            FreeWKeyboardCommand.Copy => _actions.Copy,
            FreeWKeyboardCommand.Paste => _actions.Paste,
            FreeWKeyboardCommand.PasteTextOnly => _actions.PasteTextOnly,
            FreeWKeyboardCommand.SelectAll => _actions.SelectAll,
            FreeWKeyboardCommand.Undo => _actions.Undo,
            FreeWKeyboardCommand.Redo => _actions.Redo,
            FreeWKeyboardCommand.RevealFormatting => _actions.RevealFormatting,
            FreeWKeyboardCommand.Thesaurus => _actions.Thesaurus,
            FreeWKeyboardCommand.LockCurrentField => _actions.LockCurrentField,
            FreeWKeyboardCommand.UnlockCurrentField => _actions.UnlockCurrentField,
            FreeWKeyboardCommand.UnlinkCurrentField => _actions.UnlinkCurrentField,
            FreeWKeyboardCommand.ToggleCurrentFieldCode => _actions.ToggleCurrentFieldCode,
            FreeWKeyboardCommand.ToggleFieldCodes => _actions.ToggleFieldCodes,
            FreeWKeyboardCommand.UpdateCurrentField => _actions.UpdateCurrentField,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

        action();
    }
}
