using Free.Shared.Shell;

namespace FreeW.App.Presentation.Shell;

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
    F11,
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
    SpellingAndGrammar,
    Thesaurus,
    LockCurrentField,
    UnlockCurrentField,
    UnlinkCurrentField,
    ToggleCurrentFieldCode,
    ToggleFieldCodes,
    UpdateCurrentField,
}

public readonly record struct FreeWKeyboardShortcut(
    FreeWKeyboardCommand Command,
    FreeWKeyboardKey Key,
    FreeKeyModifiers Modifiers);

/// <summary>
/// Host-neutral keyboard contract shared by the WPF and Avalonia FreeW shells.
/// Platform-specific key types and command implementations stay in their host adapters.
/// </summary>
public static class FreeWKeyboardShortcutCatalog
{
    private static readonly FreeWKeyboardShortcut[] Shortcuts =
    [
        new(FreeWKeyboardCommand.NewDocument, FreeWKeyboardKey.N, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.OpenDocument, FreeWKeyboardKey.O, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.SaveDocument, FreeWKeyboardKey.S, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.SaveDocumentAs, FreeWKeyboardKey.S, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        new(FreeWKeyboardCommand.PrintDocument, FreeWKeyboardKey.P, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.Find, FreeWKeyboardKey.F, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.Replace, FreeWKeyboardKey.H, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.Cut, FreeWKeyboardKey.X, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.Copy, FreeWKeyboardKey.C, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.Paste, FreeWKeyboardKey.V, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.PasteTextOnly, FreeWKeyboardKey.V, FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        new(FreeWKeyboardCommand.SelectAll, FreeWKeyboardKey.A, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.Undo, FreeWKeyboardKey.Z, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.Redo, FreeWKeyboardKey.Y, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.RevealFormatting, FreeWKeyboardKey.F1, FreeKeyModifiers.Shift),
        new(FreeWKeyboardCommand.SpellingAndGrammar, FreeWKeyboardKey.F7, FreeKeyModifiers.None),
        new(FreeWKeyboardCommand.Thesaurus, FreeWKeyboardKey.F7, FreeKeyModifiers.Shift),
        new(FreeWKeyboardCommand.LockCurrentField, FreeWKeyboardKey.F11, FreeKeyModifiers.Control),
        new(FreeWKeyboardCommand.UnlockCurrentField, FreeWKeyboardKey.F11,
            FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        new(FreeWKeyboardCommand.UnlinkCurrentField, FreeWKeyboardKey.F9,
            FreeKeyModifiers.Control | FreeKeyModifiers.Shift),
        new(FreeWKeyboardCommand.ToggleCurrentFieldCode, FreeWKeyboardKey.F9, FreeKeyModifiers.Shift),
        new(FreeWKeyboardCommand.ToggleFieldCodes, FreeWKeyboardKey.F9, FreeKeyModifiers.Alt),
        new(FreeWKeyboardCommand.UpdateCurrentField, FreeWKeyboardKey.F9, FreeKeyModifiers.None),
    ];

    private static readonly ApplicationKeyboardShortcutCatalog<
        FreeWKeyboardCommand,
        FreeWKeyboardKey,
        FreeKeyModifiers> Resolver = new(
            Shortcuts.Select(shortcut => new ApplicationKeyboardShortcut<
                FreeWKeyboardCommand,
                FreeWKeyboardKey,
                FreeKeyModifiers>(
                    shortcut.Command,
                    shortcut.Key,
                    shortcut.Modifiers)));

    public static IReadOnlyList<FreeWKeyboardShortcut> All => Shortcuts;

    public static FreeWKeyboardCommand? Resolve(
        FreeWKeyboardKey key,
        FreeKeyModifiers modifiers) =>
        Resolver.Resolve(key, modifiers);

    public static bool TryDispatch(
        FreeWKeyboardKey key,
        FreeKeyModifiers modifiers,
        Action<FreeWKeyboardCommand> dispatch) =>
        Resolver.TryDispatch(key, modifiers, dispatch);
}
