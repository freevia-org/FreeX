using Free.Shared.Shell;
using Avalonia.Input;
using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Avalonia;

internal static class FormulaBarAvaloniaInputAdapter
{
    public static FormulaEditorKey ToFormulaEditorKey(Key key) =>
        key switch
        {
            Key.None => FormulaEditorKey.None,
            Key.Enter => FormulaEditorKey.Enter,
            Key.Tab => FormulaEditorKey.Tab,
            Key.Escape => FormulaEditorKey.Escape,
            Key.F2 => FormulaEditorKey.F2,
            Key.F4 => FormulaEditorKey.F4,
            Key.F8 => FormulaEditorKey.F8,
            Key.Up => FormulaEditorKey.Up,
            Key.Down => FormulaEditorKey.Down,
            Key.Left => FormulaEditorKey.Left,
            Key.Right => FormulaEditorKey.Right,
            Key.Home => FormulaEditorKey.Home,
            Key.End => FormulaEditorKey.End,
            Key.PageUp => FormulaEditorKey.PageUp,
            Key.PageDown => FormulaEditorKey.PageDown,
            _ => FormulaEditorKey.None
        };

    public static FreeKeyModifiers ToFormulaEditorModifiers(KeyModifiers modifiers) =>
        ToFormulaEditorModifiers(modifiers, OperatingSystem.IsMacOS());

    /// <param name="metaIsCommand">
    /// True on macOS, where Cmd is the platform command modifier: the shared edit-key planners only
    /// know the Windows Ctrl chords (Ctrl+Enter fills the selection, Ctrl+Arrow jumps while pointing
    /// a formula reference), so Cmd is projected onto <see cref="FreeKeyModifiers.Control"/>.
    /// </param>
    public static FreeKeyModifiers ToFormulaEditorModifiers(KeyModifiers modifiers, bool metaIsCommand) =>
        Free.Shared.Shell.Avalonia.AvaloniaKeyModifiersMapper.FromAvalonia(
            modifiers,
            metaIsCommand ? KeyModifierMetaPolicy.AsControl : KeyModifierMetaPolicy.Keep);
}
