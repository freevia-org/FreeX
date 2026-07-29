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

    public static FormulaEditorModifiers ToFormulaEditorModifiers(KeyModifiers modifiers)
    {
        var result = FormulaEditorModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= FormulaEditorModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= FormulaEditorModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= FormulaEditorModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            result |= FormulaEditorModifiers.Meta;
        return result;
    }
}
