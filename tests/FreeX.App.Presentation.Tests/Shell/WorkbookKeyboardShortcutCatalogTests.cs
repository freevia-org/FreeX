using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class WorkbookKeyboardShortcutCatalogTests
{
    private static readonly KeyboardCommandShortcut[] ExpectedApplicationCommands =
    [
        KeyboardCommandShortcut.CreateTable,
        KeyboardCommandShortcut.InsertCurrentDate,
        KeyboardCommandShortcut.InsertCurrentTime,
        KeyboardCommandShortcut.ToggleOutlineSymbols,
        KeyboardCommandShortcut.PasteName,
        KeyboardCommandShortcut.NameManager,
        KeyboardCommandShortcut.CreateNamesFromSelection,
        KeyboardCommandShortcut.SpellCheck,
        KeyboardCommandShortcut.RestoreWorkbookWindow,
        KeyboardCommandShortcut.MoveWorkbookWindow,
        KeyboardCommandShortcut.SizeWorkbookWindow,
        KeyboardCommandShortcut.SwitchToNextWorkbookWindow,
        KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow,
        KeyboardCommandShortcut.MinimizeWorkbookWindow,
        KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow,
        KeyboardCommandShortcut.RebuildDependenciesAndCalculate,
        KeyboardCommandShortcut.OpenErrorChecking,
        KeyboardCommandShortcut.ToggleFormulaBarExpansion,
        KeyboardCommandShortcut.ToggleFilter,
        KeyboardCommandShortcut.ReapplyFilter,
        KeyboardCommandShortcut.QuickAnalysis,
        KeyboardCommandShortcut.InsertEmbeddedChart,
        KeyboardCommandShortcut.ToggleRibbonVisibility,
        KeyboardCommandShortcut.InsertChartSheet,
        KeyboardCommandShortcut.GroupSelection,
        KeyboardCommandShortcut.UngroupSelection,
        KeyboardCommandShortcut.OpenFormatCellsFont,
        KeyboardCommandShortcut.NewNote,
        KeyboardCommandShortcut.NewThreadedComment,
        KeyboardCommandShortcut.ZoomIn,
        KeyboardCommandShortcut.ZoomOut,
        KeyboardCommandShortcut.CopyFormulaFromAbove,
        KeyboardCommandShortcut.CopyValueFromAbove,
        KeyboardCommandShortcut.ScrollActiveCellIntoView,
        KeyboardCommandShortcut.CycleSelectionCorner,
        KeyboardCommandShortcut.SelectDirectPrecedents,
        KeyboardCommandShortcut.SelectDirectDependents,
        KeyboardCommandShortcut.SelectAllPrecedents,
        KeyboardCommandShortcut.SelectAllDependents,
        KeyboardCommandShortcut.ClearSelectionAndEdit,
        KeyboardCommandShortcut.CloseWorkbook,
        KeyboardCommandShortcut.OpenActiveDropdown,
    ];

    [Fact]
    public void SharedResolverOwnsLookupAndDispatchBehavior()
    {
        var resolver = new ApplicationKeyboardShortcutCatalog<ProbeCommand, ProbeKey, ProbeModifiers>(
        [
            new(ProbeCommand.Open, ProbeKey.O, ProbeModifiers.Control),
            new(ProbeCommand.Save, ProbeKey.S, ProbeModifiers.Control | ProbeModifiers.Shift),
        ]);

        resolver.Resolve(ProbeKey.O, ProbeModifiers.Control).Should().Be(ProbeCommand.Open);
        resolver.Resolve(ProbeKey.O, ProbeModifiers.None).Should().BeNull();

        ProbeCommand? dispatched = null;
        resolver.TryDispatch(
                ProbeKey.S,
                ProbeModifiers.Control | ProbeModifiers.Shift,
                command => dispatched = command)
            .Should().BeTrue();
        dispatched.Should().Be(ProbeCommand.Save);

        resolver.TryDispatch(ProbeKey.S, ProbeModifiers.None, _ => dispatched = ProbeCommand.Open)
            .Should().BeFalse();
        dispatched.Should().Be(ProbeCommand.Save);
    }

    [Fact]
    public void SharedResolverRejectsNullCatalogAndDispatcher()
    {
        var create = () => new ApplicationKeyboardShortcutCatalog<ProbeCommand, ProbeKey, ProbeModifiers>(null!);
        create.Should().Throw<ArgumentNullException>().WithParameterName("shortcuts");

        var resolver = new ApplicationKeyboardShortcutCatalog<ProbeCommand, ProbeKey, ProbeModifiers>([]);
        var dispatch = () => resolver.TryDispatch(ProbeKey.O, ProbeModifiers.Control, null!);
        dispatch.Should().Throw<ArgumentNullException>().WithParameterName("dispatch");
    }

    [Fact]
    public void ProductCatalogsProjectIntoTheSharedResolver()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var freeX = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Presentation",
            "Shell",
            "WorkbookKeyboardShortcutCatalog.cs"));
        var freeW = File.ReadAllText(Path.Combine(
            repoRoot,
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWKeyboardShortcutCatalog.cs"));
        var freeP = File.ReadAllText(Path.Combine(
            repoRoot,
            "freep",
            "FreeP.App.Presentation",
            "FreePShellInteractionCatalog.cs"));

        freeX.Should().Contain("ApplicationKeyboardShortcutCatalog<");
        freeX.Should().Contain("WindowsRoutes.TryResolve(key, modifiers, out route)");
        freeX.Should().Contain("NativeMenuRoutes.TryResolve(key, modifiers, out route)");
        freeX.Should().NotContain("foreach (var rule in Rules)");

        foreach (var source in new[] { freeW, freeP })
        {
            source.Should().Contain("ApplicationKeyboardShortcutCatalog<");
            source.Should().Contain("Resolver.TryDispatch(key, modifiers, dispatch)");
            source.Should().NotContain("foreach (var shortcut in Shortcuts)");
        }
    }

    [Fact]
    public void ApplicationCommandCatalog_OwnsAllFortyTwoCrossRendererRoutes()
    {
        WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts.Should().HaveCount(47);
        WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts
            .Select(shortcut => shortcut.Command)
            .Distinct()
            .Should().BeEquivalentTo(ExpectedApplicationCommands);

        WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts
            .GroupBy(shortcut => (shortcut.Key, shortcut.Modifiers))
            .Should().OnlyContain(group => group.Count() == 1);

        foreach (var shortcut in WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts)
        {
            WorkbookKeyboardShortcutCatalog.TryGetApplicationCommand(
                    shortcut.Key,
                    shortcut.Modifiers,
                    out var resolved)
                .Should().BeTrue();
            resolved.Should().Be(shortcut.Command);
        }
    }

    [Fact]
    public void NativeRenderers_OnlyConvertAndDispatchApplicationCommandCatalog()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "KeyboardShortcutMatcher.CommandRules.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.KeyboardParity.cs"));

        wpf.Should().Contain("WorkbookKeyboardShortcutCatalog.ApplicationCommandShortcuts");
        wpf.Should().NotContain("new(KeyboardCommandShortcut.CreateTable");
        wpf.Should().NotContain("new(KeyboardCommandShortcut.RebuildDependenciesAndCalculate");
        avalonia.Should().Contain("WorkbookKeyboardShortcutCatalog.TryGetApplicationCommand(");
        avalonia.Should().NotContain("AvaloniaHostShortcut");
        avalonia.Should().NotContain("R(Key.T, Ctrl");
        avalonia.Should().NotContain("R(Key.F9, CtrlAltShift");
    }

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
    [InlineData("NumPad2", WorkbookShortcutKey.D2)]
    [InlineData("Add", WorkbookShortcutKey.OemPlus)]
    [InlineData("Subtract", WorkbookShortcutKey.OemMinus)]
    [InlineData("Decimal", WorkbookShortcutKey.OemPeriod)]
    [InlineData("Next", WorkbookShortcutKey.PageDown)]
    [InlineData("Prior", WorkbookShortcutKey.PageUp)]
    [InlineData("Oem1", WorkbookShortcutKey.OemSemicolon)]
    [InlineData("Oem4", WorkbookShortcutKey.OemOpenBrackets)]
    [InlineData("Oem6", WorkbookShortcutKey.OemCloseBrackets)]
    [InlineData("Oem7", WorkbookShortcutKey.OemQuotes)]
    public void TryParseKeyName_MapsPlatformAliases(
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
    [InlineData("Space")]
    public void TryParseKeyName_RejectsUnsupportedKeys(string? keyName)
    {
        WorkbookKeyboardShortcutCatalog.TryParseKeyName(keyName, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(WorkbookShortcutKey.N, FreeKeyModifiers.Control, WorkbookShortcutRoute.NewWorkbook)]
    [InlineData(WorkbookShortcutKey.O, FreeKeyModifiers.Control, WorkbookShortcutRoute.OpenBackstage)]
    [InlineData(WorkbookShortcutKey.F12, FreeKeyModifiers.Control, WorkbookShortcutRoute.OpenWorkbook)]
    [InlineData(WorkbookShortcutKey.S, FreeKeyModifiers.Control, WorkbookShortcutRoute.SaveWorkbook)]
    [InlineData(WorkbookShortcutKey.F12, FreeKeyModifiers.Shift, WorkbookShortcutRoute.SaveWorkbook)]
    [InlineData(WorkbookShortcutKey.P, FreeKeyModifiers.Control, WorkbookShortcutRoute.PrintWorkbook)]
    [InlineData(WorkbookShortcutKey.F12, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.PrintWorkbook)]
    [InlineData(WorkbookShortcutKey.F2, FreeKeyModifiers.Control, WorkbookShortcutRoute.PrintWorkbook)]
    [InlineData(WorkbookShortcutKey.C, FreeKeyModifiers.Control, WorkbookShortcutRoute.Copy)]
    [InlineData(WorkbookShortcutKey.Insert, FreeKeyModifiers.Control, WorkbookShortcutRoute.Copy)]
    [InlineData(WorkbookShortcutKey.X, FreeKeyModifiers.Control, WorkbookShortcutRoute.Cut)]
    [InlineData(WorkbookShortcutKey.Delete, FreeKeyModifiers.Shift, WorkbookShortcutRoute.Cut)]
    [InlineData(WorkbookShortcutKey.V, FreeKeyModifiers.Control, WorkbookShortcutRoute.Paste)]
    [InlineData(WorkbookShortcutKey.Insert, FreeKeyModifiers.Shift, WorkbookShortcutRoute.Paste)]
    [InlineData(WorkbookShortcutKey.V, FreeKeyModifiers.Control | FreeKeyModifiers.Alt, WorkbookShortcutRoute.PasteSpecial)]
    [InlineData(WorkbookShortcutKey.Z, FreeKeyModifiers.Control, WorkbookShortcutRoute.Undo)]
    [InlineData(WorkbookShortcutKey.Back, FreeKeyModifiers.Alt, WorkbookShortcutRoute.Undo)]
    [InlineData(WorkbookShortcutKey.Y, FreeKeyModifiers.Control, WorkbookShortcutRoute.Redo)]
    [InlineData(WorkbookShortcutKey.Z, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.Redo)]
    [InlineData(WorkbookShortcutKey.B, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleBold)]
    [InlineData(WorkbookShortcutKey.D2, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleBold)]
    [InlineData(WorkbookShortcutKey.I, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleItalic)]
    [InlineData(WorkbookShortcutKey.D3, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleItalic)]
    [InlineData(WorkbookShortcutKey.U, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleUnderline)]
    [InlineData(WorkbookShortcutKey.D4, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleUnderline)]
    [InlineData(WorkbookShortcutKey.D5, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleStrikethrough)]
    [InlineData(WorkbookShortcutKey.D, FreeKeyModifiers.Control, WorkbookShortcutRoute.FillDown)]
    [InlineData(WorkbookShortcutKey.R, FreeKeyModifiers.Control, WorkbookShortcutRoute.FillRight)]
    [InlineData(WorkbookShortcutKey.E, FreeKeyModifiers.Control, WorkbookShortcutRoute.FlashFill)]
    [InlineData(WorkbookShortcutKey.Oem3, FreeKeyModifiers.Control, WorkbookShortcutRoute.ToggleShowFormulas)]
    [InlineData(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Control, WorkbookShortcutRoute.ActivatePreviousSheet)]
    [InlineData(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Control, WorkbookShortcutRoute.ActivateNextSheet)]
    [InlineData(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.SelectPreviousSheetGroup)]
    [InlineData(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.SelectNextSheetGroup)]
    [InlineData(WorkbookShortcutKey.D1, FreeKeyModifiers.Control, WorkbookShortcutRoute.OpenFormatCells)]
    [InlineData(WorkbookShortcutKey.Oem3, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatGeneral)]
    [InlineData(WorkbookShortcutKey.D1, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatNumber)]
    [InlineData(WorkbookShortcutKey.D2, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatTime)]
    [InlineData(WorkbookShortcutKey.D3, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatDate)]
    [InlineData(WorkbookShortcutKey.D4, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatCurrency)]
    [InlineData(WorkbookShortcutKey.D5, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatPercentage)]
    [InlineData(WorkbookShortcutKey.D6, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatScientific)]
    [InlineData(WorkbookShortcutKey.D7, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.ApplyOutlineBorder)]
    [InlineData(WorkbookShortcutKey.OemMinus, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.ClearOutlineBorder)]
    [InlineData(WorkbookShortcutKey.F, FreeKeyModifiers.Control, WorkbookShortcutRoute.Find)]
    [InlineData(WorkbookShortcutKey.H, FreeKeyModifiers.Control, WorkbookShortcutRoute.Replace)]
    [InlineData(WorkbookShortcutKey.G, FreeKeyModifiers.Control, WorkbookShortcutRoute.GoTo)]
    [InlineData(WorkbookShortcutKey.F5, FreeKeyModifiers.None, WorkbookShortcutRoute.GoTo)]
    [InlineData(WorkbookShortcutKey.F3, FreeKeyModifiers.Shift, WorkbookShortcutRoute.InsertFunction)]
    [InlineData(WorkbookShortcutKey.OemPlus, FreeKeyModifiers.Alt, WorkbookShortcutRoute.AutoSum)]
    [InlineData(WorkbookShortcutKey.G, FreeKeyModifiers.Control | FreeKeyModifiers.Shift, WorkbookShortcutRoute.WorkbookStatistics)]
    [InlineData(WorkbookShortcutKey.F11, FreeKeyModifiers.Shift, WorkbookShortcutRoute.InsertWorksheet)]
    public void TryGetWindowsRoute_ResolvesSharedWorkbookShortcutRoutes(
        WorkbookShortcutKey key,
        FreeKeyModifiers modifiers,
        WorkbookShortcutRoute expected)
    {
        WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(key, modifiers, out var route)
            .Should().BeTrue();

        route.Should().Be(expected);
    }

    [Theory]
    [InlineData(WorkbookShortcutKey.P, FreeKeyModifiers.Meta, WorkbookShortcutRoute.PrintWorkbook)]
    [InlineData(WorkbookShortcutKey.D, FreeKeyModifiers.Meta, WorkbookShortcutRoute.FillDown)]
    [InlineData(WorkbookShortcutKey.R, FreeKeyModifiers.Meta, WorkbookShortcutRoute.FillRight)]
    [InlineData(WorkbookShortcutKey.Oem3, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatGeneral)]
    [InlineData(WorkbookShortcutKey.D1, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatNumber)]
    [InlineData(WorkbookShortcutKey.D2, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatTime)]
    [InlineData(WorkbookShortcutKey.D3, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatDate)]
    [InlineData(WorkbookShortcutKey.D4, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatCurrency)]
    [InlineData(WorkbookShortcutKey.D5, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatPercentage)]
    [InlineData(WorkbookShortcutKey.D6, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.NumberFormatScientific)]
    [InlineData(WorkbookShortcutKey.D7, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.ApplyOutlineBorder)]
    [InlineData(WorkbookShortcutKey.OemMinus, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.ClearOutlineBorder)]
    [InlineData(WorkbookShortcutKey.X, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.ToggleStrikethrough)]
    [InlineData(WorkbookShortcutKey.E, FreeKeyModifiers.Meta, WorkbookShortcutRoute.FlashFill)]
    [InlineData(WorkbookShortcutKey.T, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.AutoSum)]
    [InlineData(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Meta, WorkbookShortcutRoute.ActivatePreviousSheet)]
    [InlineData(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Meta, WorkbookShortcutRoute.ActivateNextSheet)]
    [InlineData(WorkbookShortcutKey.PageUp, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.SelectPreviousSheetGroup)]
    [InlineData(WorkbookShortcutKey.PageDown, FreeKeyModifiers.Meta | FreeKeyModifiers.Shift, WorkbookShortcutRoute.SelectNextSheetGroup)]
    public void TryGetNativeMenuRoute_ResolvesSharedMacShortcutRoutes(
        WorkbookShortcutKey key,
        FreeKeyModifiers modifiers,
        WorkbookShortcutRoute expected)
    {
        WorkbookKeyboardShortcutCatalog.TryGetNativeMenuRoute(key, modifiers, out var route)
            .Should().BeTrue();

        route.Should().Be(expected);
    }

    [Fact]
    public void WindowsAndNativeMenuRoutesRemainIndependent()
    {
        WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(
                WorkbookShortcutKey.N,
                FreeKeyModifiers.Meta,
                out _)
            .Should().BeFalse();
        WorkbookKeyboardShortcutCatalog.TryGetNativeMenuRoute(
                WorkbookShortcutKey.F12,
                FreeKeyModifiers.Control,
                out _)
            .Should().BeFalse();
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

    private enum ProbeCommand
    {
        Open,
        Save,
    }

    private enum ProbeKey
    {
        O,
        S,
    }

    [Flags]
    private enum ProbeModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
    }
}
