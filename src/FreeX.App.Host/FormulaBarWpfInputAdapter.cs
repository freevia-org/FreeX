using Free.Shared.Shell;
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

    public static FreeKeyModifiers ToFormulaEditorModifiers(ModifierKeys modifiers) =>
        Free.Shared.Shell.Wpf.WpfKeyModifiersMapper.FromWpf(modifiers);

    public static Thickness ToWpfThickness(FormulaEditorThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

}
