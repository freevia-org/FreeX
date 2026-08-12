using System.Windows;
using System.Windows.Input;
using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Host;

internal static class FormulaBarWpfInputAdapter
{
    public static FormulaEditorKey ToFormulaEditorKey(Key key) =>
        key switch
        {
            Key.None => FormulaEditorKey.None,
            Key.System => FormulaEditorKey.System,
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

    public static FormulaEditorModifiers ToFormulaEditorModifiers(ModifierKeys modifiers)
    {
        var result = FormulaEditorModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            result |= FormulaEditorModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Control))
            result |= FormulaEditorModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            result |= FormulaEditorModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Windows))
            result |= FormulaEditorModifiers.Meta;
        return result;
    }

    public static Thickness ToWpfThickness(FormulaEditorThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

}
