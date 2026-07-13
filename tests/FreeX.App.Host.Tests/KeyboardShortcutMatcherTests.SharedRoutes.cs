using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;

namespace FreeX.App.Host.Tests;

public sealed partial class KeyboardShortcutMatcherTests
{
    [Fact]
    public void SharedWorkbookShortcutMatrix_RoutesWpfCommandShortcuts()
    {
        foreach (var rule in WorkbookKeyboardShortcutCatalog.Rules.Where(rule => ShouldRouteAsCommandShortcut(rule.Route)))
        {
            KeyboardShortcutMatcher.TryGetCommandShortcut(
                    ToWpfKey(rule.WindowsChord.Key),
                    Key.None,
                    ToWpfModifiers(rule.WindowsChord.Modifiers),
                    out var shortcut)
                .Should().BeTrue($"WPF should route {rule.WindowsChord} through the shared workbook shortcut matrix");

            shortcut.Should().Be(ToHostShortcut(rule.Route));
        }
    }

    [Fact]
    public void SharedWorkbookShortcutMatrix_RoutesWpfFontToggleShortcuts()
    {
        foreach (var rule in WorkbookKeyboardShortcutCatalog.Rules.Where(rule => IsFontToggleRoute(rule.Route)))
        {
            KeyboardShortcutMatcher.TryGetFontToggleShortcut(
                    ToWpfKey(rule.WindowsChord.Key),
                    ToWpfModifiers(rule.WindowsChord.Modifiers),
                    out var shortcut)
                .Should().BeTrue($"WPF should route {rule.WindowsChord} through the shared workbook shortcut matrix");

            shortcut.Should().Be(ToFontToggleShortcut(rule.Route));
        }
    }

    [Fact]
    public void SharedWorkbookShortcutMatrix_RoutesWpfPasteSpecialShortcut()
    {
        var pasteSpecial = WorkbookKeyboardShortcutCatalog.Rules.Single(rule => rule.Route == WorkbookShortcutRoute.PasteSpecial);

        KeyboardShortcutMatcher.IsPasteSpecialShortcut(
                ToWpfKey(pasteSpecial.WindowsChord.Key),
                Key.None,
                ToWpfModifiers(pasteSpecial.WindowsChord.Modifiers))
            .Should().BeTrue();
    }

    private static Key ToWpfKey(WorkbookShortcutKey key) =>
        key switch
        {
            WorkbookShortcutKey.Back => Key.Back,
            WorkbookShortcutKey.B => Key.B,
            WorkbookShortcutKey.C => Key.C,
            WorkbookShortcutKey.D => Key.D,
            WorkbookShortcutKey.D1 => Key.D1,
            WorkbookShortcutKey.D2 => Key.D2,
            WorkbookShortcutKey.D3 => Key.D3,
            WorkbookShortcutKey.D4 => Key.D4,
            WorkbookShortcutKey.D5 => Key.D5,
            WorkbookShortcutKey.Delete => Key.Delete,
            WorkbookShortcutKey.E => Key.E,
            WorkbookShortcutKey.F => Key.F,
            WorkbookShortcutKey.F3 => Key.F3,
            WorkbookShortcutKey.F5 => Key.F5,
            WorkbookShortcutKey.F11 => Key.F11,
            WorkbookShortcutKey.F12 => Key.F12,
            WorkbookShortcutKey.G => Key.G,
            WorkbookShortcutKey.H => Key.H,
            WorkbookShortcutKey.I => Key.I,
            WorkbookShortcutKey.Insert => Key.Insert,
            WorkbookShortcutKey.N => Key.N,
            WorkbookShortcutKey.O => Key.O,
            WorkbookShortcutKey.Oem3 => Key.Oem3,
            WorkbookShortcutKey.OemPlus => Key.OemPlus,
            WorkbookShortcutKey.R => Key.R,
            WorkbookShortcutKey.S => Key.S,
            WorkbookShortcutKey.U => Key.U,
            WorkbookShortcutKey.V => Key.V,
            WorkbookShortcutKey.X => Key.X,
            WorkbookShortcutKey.Y => Key.Y,
            WorkbookShortcutKey.Z => Key.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

    private static ModifierKeys ToWpfModifiers(WorkbookShortcutModifiers modifiers)
    {
        var result = ModifierKeys.None;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Control))
            result |= ModifierKeys.Control;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Alt))
            result |= ModifierKeys.Alt;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Shift))
            result |= ModifierKeys.Shift;
        return result;
    }

    private static KeyboardCommandShortcut ToHostShortcut(WorkbookShortcutRoute route) =>
        route switch
        {
            WorkbookShortcutRoute.NewWorkbook => KeyboardCommandShortcut.NewWorkbook,
            WorkbookShortcutRoute.OpenWorkbook => KeyboardCommandShortcut.OpenWorkbook,
            WorkbookShortcutRoute.SaveWorkbook => KeyboardCommandShortcut.SaveWorkbook,
            WorkbookShortcutRoute.Copy => KeyboardCommandShortcut.Copy,
            WorkbookShortcutRoute.Cut => KeyboardCommandShortcut.Cut,
            WorkbookShortcutRoute.Paste => KeyboardCommandShortcut.Paste,
            WorkbookShortcutRoute.Undo => KeyboardCommandShortcut.Undo,
            WorkbookShortcutRoute.Redo => KeyboardCommandShortcut.Redo,
            WorkbookShortcutRoute.FillDown => KeyboardCommandShortcut.FillDown,
            WorkbookShortcutRoute.FillRight => KeyboardCommandShortcut.FillRight,
            WorkbookShortcutRoute.FlashFill => KeyboardCommandShortcut.FlashFill,
            WorkbookShortcutRoute.ToggleShowFormulas => KeyboardCommandShortcut.ToggleShowFormulas,
            WorkbookShortcutRoute.OpenFormatCells => KeyboardCommandShortcut.OpenFormatCells,
            WorkbookShortcutRoute.Find => KeyboardCommandShortcut.Find,
            WorkbookShortcutRoute.Replace => KeyboardCommandShortcut.Replace,
            WorkbookShortcutRoute.GoTo => KeyboardCommandShortcut.GoTo,
            WorkbookShortcutRoute.InsertFunction => KeyboardCommandShortcut.InsertFunction,
            WorkbookShortcutRoute.AutoSum => KeyboardCommandShortcut.AutoSum,
            WorkbookShortcutRoute.WorkbookStatistics => KeyboardCommandShortcut.WorkbookStatistics,
            WorkbookShortcutRoute.InsertWorksheet => KeyboardCommandShortcut.InsertWorksheet,
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };

    private static FontToggleShortcut ToFontToggleShortcut(WorkbookShortcutRoute route) =>
        route switch
        {
            WorkbookShortcutRoute.ToggleBold => FontToggleShortcut.Bold,
            WorkbookShortcutRoute.ToggleItalic => FontToggleShortcut.Italic,
            WorkbookShortcutRoute.ToggleUnderline => FontToggleShortcut.Underline,
            WorkbookShortcutRoute.ToggleStrikethrough => FontToggleShortcut.Strikethrough,
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };

    private static bool ShouldRouteAsCommandShortcut(WorkbookShortcutRoute route) =>
        route != WorkbookShortcutRoute.PasteSpecial && !IsFontToggleRoute(route);

    private static bool IsFontToggleRoute(WorkbookShortcutRoute route) =>
        route is
            WorkbookShortcutRoute.ToggleBold or
            WorkbookShortcutRoute.ToggleItalic or
            WorkbookShortcutRoute.ToggleUnderline or
            WorkbookShortcutRoute.ToggleStrikethrough;
}
