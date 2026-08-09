namespace FreeW.App.Presentation.Shell;

[Flags]
public enum FreeWKeyboardModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

public enum FreeWKeyboardKey
{
    A,
    C,
    F,
    H,
    N,
    O,
    P,
    S,
    V,
    X,
    Y,
    Z,
    F1,
    F7,
    F9,
}

public enum FreeWKeyboardCommand
{
    NewDocument,
    OpenDocument,
    SaveDocument,
    SaveDocumentAs,
    PrintDocument,
    Find,
    Replace,
    Cut,
    Copy,
    Paste,
    PasteTextOnly,
    SelectAll,
    Undo,
    Redo,
    RevealFormatting,
    Thesaurus,
    ToggleCurrentFieldCode,
    ToggleFieldCodes,
    UpdateFields,
}

public readonly record struct FreeWKeyboardShortcut(
    FreeWKeyboardCommand Command,
    FreeWKeyboardKey Key,
    FreeWKeyboardModifiers Modifiers);

/// <summary>
/// Host-neutral keyboard contract shared by the WPF and Avalonia FreeW shells.
/// Platform-specific key types and command implementations stay in their host adapters.
/// </summary>
public static class FreeWKeyboardShortcutCatalog
{
    private static readonly FreeWKeyboardShortcut[] Shortcuts =
    [
        new(FreeWKeyboardCommand.NewDocument, FreeWKeyboardKey.N, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.OpenDocument, FreeWKeyboardKey.O, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.SaveDocument, FreeWKeyboardKey.S, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.SaveDocumentAs, FreeWKeyboardKey.S, FreeWKeyboardModifiers.Control | FreeWKeyboardModifiers.Shift),
        new(FreeWKeyboardCommand.PrintDocument, FreeWKeyboardKey.P, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.Find, FreeWKeyboardKey.F, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.Replace, FreeWKeyboardKey.H, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.Cut, FreeWKeyboardKey.X, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.Copy, FreeWKeyboardKey.C, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.Paste, FreeWKeyboardKey.V, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.PasteTextOnly, FreeWKeyboardKey.V, FreeWKeyboardModifiers.Control | FreeWKeyboardModifiers.Shift),
        new(FreeWKeyboardCommand.SelectAll, FreeWKeyboardKey.A, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.Undo, FreeWKeyboardKey.Z, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.Redo, FreeWKeyboardKey.Y, FreeWKeyboardModifiers.Control),
        new(FreeWKeyboardCommand.RevealFormatting, FreeWKeyboardKey.F1, FreeWKeyboardModifiers.Shift),
        new(FreeWKeyboardCommand.Thesaurus, FreeWKeyboardKey.F7, FreeWKeyboardModifiers.Shift),
        new(FreeWKeyboardCommand.ToggleCurrentFieldCode, FreeWKeyboardKey.F9, FreeWKeyboardModifiers.Shift),
        new(FreeWKeyboardCommand.ToggleFieldCodes, FreeWKeyboardKey.F9, FreeWKeyboardModifiers.Alt),
        new(FreeWKeyboardCommand.UpdateFields, FreeWKeyboardKey.F9, FreeWKeyboardModifiers.None),
    ];

    public static IReadOnlyList<FreeWKeyboardShortcut> All => Shortcuts;

    public static FreeWKeyboardCommand? Resolve(
        FreeWKeyboardKey key,
        FreeWKeyboardModifiers modifiers)
    {
        foreach (var shortcut in Shortcuts)
        {
            if (shortcut.Key == key && shortcut.Modifiers == modifiers)
                return shortcut.Command;
        }

        return null;
    }

    public static bool TryDispatch(
        FreeWKeyboardKey key,
        FreeWKeyboardModifiers modifiers,
        Action<FreeWKeyboardCommand> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        if (Resolve(key, modifiers) is not { } command)
            return false;

        dispatch(command);
        return true;
    }
}
