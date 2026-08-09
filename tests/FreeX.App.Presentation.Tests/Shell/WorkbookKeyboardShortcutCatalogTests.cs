using FluentAssertions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class WorkbookKeyboardShortcutCatalogTests
{
    [Theory]
    [InlineData("A", WorkbookShortcutKey.A)]
    [InlineData("OemPlus", WorkbookShortcutKey.OemPlus)]
    [InlineData("PageDown", WorkbookShortcutKey.PageDown)]
    public void TryParseKeyName_MapsCanonicalPlatformEnumNames(
        string keyName,
        WorkbookShortcutKey expected)
    {
        WorkbookKeyboardShortcutCatalog.TryParseKeyName(keyName, out var key).Should().BeTrue();
        key.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("NumPad2")]
    [InlineData("Space")]
    public void TryParseKeyName_RejectsAliasesAndUnsupportedKeys(string? keyName)
    {
        WorkbookKeyboardShortcutCatalog.TryParseKeyName(keyName, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(WorkbookShortcutKey.N, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.NewWorkbook)]
    [InlineData(WorkbookShortcutKey.O, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.OpenWorkbook)]
    [InlineData(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.OpenWorkbook)]
    [InlineData(WorkbookShortcutKey.S, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.SaveWorkbook)]
    [InlineData(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.SaveWorkbook)]
    [InlineData(WorkbookShortcutKey.P, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.PrintWorkbook)]
    [InlineData(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.PrintWorkbook)]
    [InlineData(WorkbookShortcutKey.C, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Copy)]
    [InlineData(WorkbookShortcutKey.Insert, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Copy)]
    [InlineData(WorkbookShortcutKey.X, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Cut)]
    [InlineData(WorkbookShortcutKey.Delete, WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.Cut)]
    [InlineData(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Paste)]
    [InlineData(WorkbookShortcutKey.Insert, WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.Paste)]
    [InlineData(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Alt, WorkbookShortcutRoute.PasteSpecial)]
    [InlineData(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Undo)]
    [InlineData(WorkbookShortcutKey.Back, WorkbookShortcutModifiers.Alt, WorkbookShortcutRoute.Undo)]
    [InlineData(WorkbookShortcutKey.Y, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Redo)]
    [InlineData(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.Redo)]
    [InlineData(WorkbookShortcutKey.B, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleBold)]
    [InlineData(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleBold)]
    [InlineData(WorkbookShortcutKey.I, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleItalic)]
    [InlineData(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleItalic)]
    [InlineData(WorkbookShortcutKey.U, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleUnderline)]
    [InlineData(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleUnderline)]
    [InlineData(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleStrikethrough)]
    [InlineData(WorkbookShortcutKey.D, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.FillDown)]
    [InlineData(WorkbookShortcutKey.R, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.FillRight)]
    [InlineData(WorkbookShortcutKey.E, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.FlashFill)]
    [InlineData(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ToggleShowFormulas)]
    [InlineData(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ActivatePreviousSheet)]
    [InlineData(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.ActivateNextSheet)]
    [InlineData(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.SelectPreviousSheetGroup)]
    [InlineData(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.SelectNextSheetGroup)]
    [InlineData(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.OpenFormatCells)]
    [InlineData(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatGeneral)]
    [InlineData(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatNumber)]
    [InlineData(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatTime)]
    [InlineData(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatDate)]
    [InlineData(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatCurrency)]
    [InlineData(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatPercentage)]
    [InlineData(WorkbookShortcutKey.D6, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatScientific)]
    [InlineData(WorkbookShortcutKey.D7, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.ApplyOutlineBorder)]
    [InlineData(WorkbookShortcutKey.OemMinus, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.ClearOutlineBorder)]
    [InlineData(WorkbookShortcutKey.F, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Find)]
    [InlineData(WorkbookShortcutKey.H, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.Replace)]
    [InlineData(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control, WorkbookShortcutRoute.GoTo)]
    [InlineData(WorkbookShortcutKey.F5, WorkbookShortcutModifiers.None, WorkbookShortcutRoute.GoTo)]
    [InlineData(WorkbookShortcutKey.F3, WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.InsertFunction)]
    [InlineData(WorkbookShortcutKey.OemPlus, WorkbookShortcutModifiers.Alt, WorkbookShortcutRoute.AutoSum)]
    [InlineData(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.WorkbookStatistics)]
    [InlineData(WorkbookShortcutKey.F11, WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.InsertWorksheet)]
    public void TryGetWindowsRoute_ResolvesSharedWorkbookShortcutRoutes(
        WorkbookShortcutKey key,
        WorkbookShortcutModifiers modifiers,
        WorkbookShortcutRoute expected)
    {
        WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(key, modifiers, out var route)
            .Should().BeTrue();

        route.Should().Be(expected);
    }

    [Theory]
    [InlineData(WorkbookShortcutKey.P, WorkbookShortcutModifiers.Meta, WorkbookShortcutRoute.PrintWorkbook)]
    [InlineData(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatGeneral)]
    [InlineData(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatNumber)]
    [InlineData(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatTime)]
    [InlineData(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatDate)]
    [InlineData(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatCurrency)]
    [InlineData(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatPercentage)]
    [InlineData(WorkbookShortcutKey.D6, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.NumberFormatScientific)]
    [InlineData(WorkbookShortcutKey.D7, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.ApplyOutlineBorder)]
    [InlineData(WorkbookShortcutKey.OemMinus, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.ClearOutlineBorder)]
    [InlineData(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Meta, WorkbookShortcutRoute.ActivatePreviousSheet)]
    [InlineData(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Meta, WorkbookShortcutRoute.ActivateNextSheet)]
    [InlineData(WorkbookShortcutKey.PageUp, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.SelectPreviousSheetGroup)]
    [InlineData(WorkbookShortcutKey.PageDown, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift, WorkbookShortcutRoute.SelectNextSheetGroup)]
    public void TryGetNativeMenuRoute_ResolvesSharedMacShortcutRoutes(
        WorkbookShortcutKey key,
        WorkbookShortcutModifiers modifiers,
        WorkbookShortcutRoute expected)
    {
        WorkbookKeyboardShortcutCatalog.TryGetNativeMenuRoute(key, modifiers, out var route)
            .Should().BeTrue();

        route.Should().Be(expected);
    }

    [Fact]
    public void WindowsChords_AreUnique()
    {
        var duplicateChords = WorkbookKeyboardShortcutCatalog.Rules
            .GroupBy(rule => rule.WindowsChord)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(rule => rule.Route))}")
            .ToArray();

        duplicateChords.Should().BeEmpty();
    }

    [Fact]
    public void NativeMenuChords_AreUniquePerRoute()
    {
        var duplicateRoutes = WorkbookKeyboardShortcutCatalog.Rules
            .Where(rule => rule.NativeMenuChord is not null)
            .GroupBy(rule => rule.Route)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        duplicateRoutes.Should().BeEmpty();
    }

    [Fact]
    public void RouteCategories_PartitionWorkbookShortcutMatrix()
    {
        var uncategorizedRoutes = WorkbookKeyboardShortcutCatalog.Rules
            .Select(rule => rule.Route)
            .Distinct()
            .Where(route =>
                !WorkbookKeyboardShortcutCatalog.IsCommandRoute(route) &&
                route != WorkbookShortcutRoute.PasteSpecial &&
                !WorkbookKeyboardShortcutCatalog.IsFontToggleRoute(route) &&
                !WorkbookKeyboardShortcutCatalog.IsNumberFormatRoute(route) &&
                !WorkbookKeyboardShortcutCatalog.IsBorderRoute(route))
            .ToArray();

        uncategorizedRoutes.Should().BeEmpty();
    }
}
