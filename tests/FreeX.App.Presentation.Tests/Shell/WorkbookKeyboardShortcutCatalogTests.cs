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
        KeyboardCommandShortcut.InsertChartSheet,
        KeyboardCommandShortcut.GroupSelection,
        KeyboardCommandShortcut.UngroupSelection,
        KeyboardCommandShortcut.OpenFormatCellsFont,
        KeyboardCommandShortcut.NewNote,
        KeyboardCommandShortcut.NewThreadedComment,
        KeyboardCommandShortcut.EditInFormulaBar,
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
    public void WindowsAndNativeMenuRoutesRemainIndependent()
    {
        WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(
                WorkbookShortcutKey.N,
                WorkbookShortcutModifiers.Meta,
                out _)
            .Should().BeFalse();
        WorkbookKeyboardShortcutCatalog.TryGetNativeMenuRoute(
                WorkbookShortcutKey.F12,
                WorkbookShortcutModifiers.Control,
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
