using FluentAssertions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class NativeMenuCatalogTests
{
    [Fact]
    public void TopLevelMenus_KeepRibbonBackstageNativeOrder()
    {
        NativeMenuCatalog.TopLevelMenus.Select(menu => menu.Id)
            .Should()
            .Equal(
                NativeMenuTopLevelId.File,
                NativeMenuTopLevelId.Home,
                NativeMenuTopLevelId.Insert,
                NativeMenuTopLevelId.PageLayout,
                NativeMenuTopLevelId.Formulas,
                NativeMenuTopLevelId.Data,
                NativeMenuTopLevelId.Review,
                NativeMenuTopLevelId.View,
                NativeMenuTopLevelId.Sheet,
                NativeMenuTopLevelId.Window,
                NativeMenuTopLevelId.Help);

        NativeMenuCatalog.TopLevelMenus.Select(menu => menu.Header)
            .Should()
            .Equal("File", "Home", "Insert", "Page Layout", "Formulas", "Data", "Review", "View", "Sheet", "Window", "Help");
    }

    [Fact]
    public void FileMenuEntries_GroupBackstageAndWorkbookCommandsInNativeOrder()
    {
        NativeMenuCatalog.FileMenuEntries
            .Select(DescribeEntry)
            .Should()
            .Equal(
                nameof(NativeFileMenuItemId.NewWorkbook),
                nameof(NativeFileMenuItemId.Open),
                nameof(NativeFileMenuItemId.OpenRecent),
                nameof(NativeFileMenuItemId.ShareWorkbook),
                "|",
                nameof(NativeFileMenuItemId.BackstageInfo),
                nameof(NativeFileMenuItemId.Save),
                nameof(NativeFileMenuItemId.SaveAs),
                "|",
                nameof(NativeFileMenuItemId.Print),
                nameof(NativeFileMenuItemId.PrintPreview),
                nameof(NativeFileMenuItemId.BackstageExport),
                nameof(NativeFileMenuItemId.ExportPdf),
                nameof(NativeFileMenuItemId.WorkbookStatistics),
                nameof(NativeFileMenuItemId.PageSetup),
                "|",
                nameof(NativeFileMenuItemId.CloseWorkbook),
                "|",
                nameof(NativeFileMenuItemId.BackstageAccount),
                nameof(NativeFileMenuItemId.Options),
                "|",
                nameof(NativeFileMenuItemId.Quit));
    }

    [Fact]
    public void FileMenuItems_CarryLocalizedLabelsGesturesAndSmokeExpectations()
    {
        NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.NewWorkbook).Should().Be(
            new NativeFileMenuItemPlan(
                NativeFileMenuItemId.NewWorkbook,
                "AvaloniaNativeMenu_NewWorkbook",
                new NativeMenuGesturePlan(NativeMenuGestureKey.N, NativeMenuGestureModifiers.Meta)));

        NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.OpenRecent)
            .Should()
            .Be(new NativeFileMenuItemPlan(
                NativeFileMenuItemId.OpenRecent,
                "AvaloniaNativeMenu_OpenRecent",
                Gesture: null,
                RequiresGestureInSmoke: false));

        NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.WorkbookStatistics).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(
                NativeMenuGestureKey.G,
                NativeMenuGestureModifiers.Control | NativeMenuGestureModifiers.Shift));

        var quit = NativeMenuCatalog.GetFileMenuItem(NativeFileMenuItemId.Quit);
        quit.Label.Should().Be("Quit FreeX");
        quit.UsesResourceKey.Should().BeFalse();
        quit.Gesture.Should().Be(new NativeMenuGesturePlan(NativeMenuGestureKey.Q, NativeMenuGestureModifiers.Meta));
    }

    [Fact]
    public void RequestedMenuEntries_LiveInCatalogInNativeOrder()
    {
        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Home))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.Undo),
                nameof(NativeMenuItemId.Redo),
                "|",
                nameof(NativeMenuItemId.Cut),
                nameof(NativeMenuItemId.Copy),
                nameof(NativeMenuItemId.Paste),
                nameof(NativeMenuItemId.PasteSpecial),
                nameof(NativeMenuItemId.FormatPainter),
                "|",
                nameof(NativeMenuItemId.Bold),
                nameof(NativeMenuItemId.Italic),
                nameof(NativeMenuItemId.Underline),
                nameof(NativeMenuItemId.DoubleUnderline),
                nameof(NativeMenuItemId.Strikethrough),
                nameof(NativeMenuItemId.IncreaseFontSize),
                nameof(NativeMenuItemId.DecreaseFontSize),
                nameof(NativeMenuItemId.FillColor),
                nameof(NativeMenuItemId.ClearFill),
                nameof(NativeMenuItemId.FontColor),
                nameof(NativeMenuItemId.Borders),
                nameof(NativeMenuItemId.CellStyles),
                nameof(NativeMenuItemId.FormatCells),
                nameof(NativeMenuItemId.ConditionalFormatting),
                "|",
                nameof(NativeMenuItemId.HorizontalText),
                nameof(NativeMenuItemId.AngleCounterclockwise),
                nameof(NativeMenuItemId.AngleClockwise),
                nameof(NativeMenuItemId.VerticalText),
                nameof(NativeMenuItemId.RotateTextUp),
                nameof(NativeMenuItemId.RotateTextDown),
                "|",
                nameof(NativeMenuItemId.CurrencyFormat),
                nameof(NativeMenuItemId.PercentFormat),
                nameof(NativeMenuItemId.CommaStyle),
                nameof(NativeMenuItemId.IncreaseDecimal),
                nameof(NativeMenuItemId.DecreaseDecimal),
                "|",
                nameof(NativeMenuItemId.AlignTop),
                nameof(NativeMenuItemId.AlignMiddle),
                nameof(NativeMenuItemId.AlignBottom),
                nameof(NativeMenuItemId.WrapText),
                nameof(NativeMenuItemId.MergeAndCenter),
                nameof(NativeMenuItemId.UnmergeCells),
                nameof(NativeMenuItemId.DecreaseIndent),
                nameof(NativeMenuItemId.IncreaseIndent),
                nameof(NativeMenuItemId.AlignLeft),
                nameof(NativeMenuItemId.AlignCenter),
                nameof(NativeMenuItemId.AlignRight),
                "|",
                nameof(NativeMenuItemId.FillCells),
                nameof(NativeMenuItemId.Clear),
                nameof(NativeMenuItemId.SelectAll),
                "|",
                nameof(NativeMenuItemId.Find),
                nameof(NativeMenuItemId.FindNext),
                nameof(NativeMenuItemId.Replace),
                nameof(NativeMenuItemId.GoTo),
                nameof(NativeMenuItemId.GoToSpecial),
                nameof(NativeMenuItemId.OpenHyperlink));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Insert))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.InsertHyperlink),
                "|",
                nameof(NativeMenuItemId.InsertColumnChart),
                nameof(NativeMenuItemId.InsertBarChart),
                nameof(NativeMenuItemId.InsertLineChart),
                nameof(NativeMenuItemId.InsertPieChart),
                nameof(NativeMenuItemId.InsertAreaChart),
                nameof(NativeMenuItemId.InsertScatterChart),
                "|",
                nameof(NativeMenuItemId.InsertTable),
                nameof(NativeMenuItemId.InsertPivotTable),
                "|",
                nameof(NativeMenuItemId.InsertPicture),
                nameof(NativeMenuItemId.InsertShape),
                nameof(NativeMenuItemId.InsertTextBox));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.PageLayout))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.Themes),
                nameof(NativeMenuItemId.ThemeColors),
                nameof(NativeMenuItemId.ThemeFonts),
                nameof(NativeMenuItemId.ThemeEffects),
                "|",
                nameof(NativeMenuItemId.PageMargins),
                nameof(NativeMenuItemId.PageOrientation),
                nameof(NativeMenuItemId.PaperSize),
                nameof(NativeMenuItemId.PrintArea),
                nameof(NativeMenuItemId.PageBreaks),
                nameof(NativeMenuItemId.SheetBackground),
                nameof(NativeMenuItemId.PageSetup),
                "|",
                nameof(NativeMenuItemId.PrintGridlines),
                nameof(NativeMenuItemId.PrintHeadings));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Formulas))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.AutoSum),
                nameof(NativeMenuItemId.InsertFunction),
                "|",
                nameof(NativeMenuItemId.NameManager),
                nameof(NativeMenuItemId.DefineName),
                nameof(NativeMenuItemId.CreateNamesFromSelection),
                "|",
                nameof(NativeMenuItemId.ShowFormulas));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Data))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.SortAscending),
                nameof(NativeMenuItemId.SortDescending),
                nameof(NativeMenuItemId.CustomSort),
                nameof(NativeMenuItemId.FlashFill),
                nameof(NativeMenuItemId.ToggleFilter),
                nameof(NativeMenuItemId.AdvancedFilter),
                nameof(NativeMenuItemId.RemoveDuplicates),
                nameof(NativeMenuItemId.Subtotal),
                "|",
                nameof(NativeMenuItemId.TextToColumns),
                nameof(NativeMenuItemId.Consolidate),
                "|",
                nameof(NativeMenuItemId.DataValidationPreview),
                nameof(NativeMenuItemId.DataValidation),
                "|",
                nameof(NativeMenuItemId.QuickAnalysis),
                "|",
                nameof(NativeMenuItemId.WhatIfAnalysis),
                nameof(NativeMenuItemId.ForecastSheet));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Review))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.ReviewSummary),
                nameof(NativeMenuItemId.CheckAccessibility),
                "|",
                nameof(NativeMenuItemId.ProtectSheet),
                nameof(NativeMenuItemId.ProtectWorkbook),
                "|",
                nameof(NativeMenuItemId.NextNote),
                nameof(NativeMenuItemId.PreviousNote),
                "|",
                nameof(NativeMenuItemId.NextComment),
                nameof(NativeMenuItemId.PreviousComment));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.View))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.ShowGridlines),
                nameof(NativeMenuItemId.ShowHeadings),
                "|",
                nameof(NativeMenuItemId.ZoomIn),
                nameof(NativeMenuItemId.ZoomOut),
                nameof(NativeMenuItemId.Zoom100),
                nameof(NativeMenuItemId.ZoomToSelection),
                "|",
                nameof(NativeMenuItemId.FreezePanes),
                nameof(NativeMenuItemId.FreezeTopRow),
                nameof(NativeMenuItemId.FreezeFirstColumn),
                nameof(NativeMenuItemId.UnfreezePanes),
                nameof(NativeMenuItemId.PageBreakPreview));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Sheet))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.NewSheet),
                nameof(NativeMenuItemId.RenameSheet),
                nameof(NativeMenuItemId.DuplicateSheet),
                nameof(NativeMenuItemId.MoveSheetLeft),
                nameof(NativeMenuItemId.MoveSheetRight),
                nameof(NativeMenuItemId.TabColor),
                nameof(NativeMenuItemId.SelectAllSheets),
                nameof(NativeMenuItemId.UngroupSheets),
                "|",
                nameof(NativeMenuItemId.HideSheet),
                nameof(NativeMenuItemId.UnhideSheet),
                "|",
                nameof(NativeMenuItemId.DeleteSheet));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Window))
            .Should()
            .Equal(nameof(NativeMenuItemId.MinimizeWindow), nameof(NativeMenuItemId.ZoomWindow), "|", nameof(NativeMenuItemId.BringAllToFront));

        DescribeEntries(NativeMenuCatalog.GetMenuEntries(NativeMenuTopLevelId.Help))
            .Should()
            .Equal(
                nameof(NativeMenuItemId.HelpOnline),
                nameof(NativeMenuItemId.SendFeedback),
                nameof(NativeMenuItemId.CheckForUpdates),
                "|",
                nameof(NativeMenuItemId.About),
                nameof(NativeMenuItemId.LegalNotices));
    }

    [Fact]
    public void PageLayoutAndFormulasSubmenuEntries_LiveInCatalogInNativeOrder()
    {
        DescribeEntries(NativeMenuCatalog.PageMarginsMenuEntries)
            .Should()
            .Equal(
                nameof(NativeMenuItemId.PageMarginsNormal),
                nameof(NativeMenuItemId.PageMarginsWide),
                nameof(NativeMenuItemId.PageMarginsNarrow),
                "|",
                nameof(NativeMenuItemId.PageMarginsCustom));

        DescribeEntries(NativeMenuCatalog.PageOrientationMenuEntries)
            .Should()
            .Equal(nameof(NativeMenuItemId.PageOrientationPortrait), nameof(NativeMenuItemId.PageOrientationLandscape));

        DescribeEntries(NativeMenuCatalog.PaperSizeMenuEntries)
            .Should()
            .Equal(
                nameof(NativeMenuItemId.PaperSizeLetter),
                nameof(NativeMenuItemId.PaperSizeLegal),
                nameof(NativeMenuItemId.PaperSizeA4),
                "|",
                nameof(NativeMenuItemId.PaperSizeMore));

        DescribeEntries(NativeMenuCatalog.PrintAreaMenuEntries)
            .Should()
            .Equal(nameof(NativeMenuItemId.SetPrintArea), nameof(NativeMenuItemId.ClearPrintArea));

        DescribeEntries(NativeMenuCatalog.SheetBackgroundMenuEntries)
            .Should()
            .Equal(nameof(NativeMenuItemId.ChooseSheetBackground), nameof(NativeMenuItemId.DeleteSheetBackground));

        DescribeEntries(NativeMenuCatalog.AutoSumMenuEntries)
            .Should()
            .Equal(
                nameof(NativeMenuItemId.AutoSumSum),
                nameof(NativeMenuItemId.AutoSumAverage),
                nameof(NativeMenuItemId.AutoSumCountNumbers),
                nameof(NativeMenuItemId.AutoSumCountAll),
                nameof(NativeMenuItemId.AutoSumMax),
                nameof(NativeMenuItemId.AutoSumMin));
    }

    [Fact]
    public void CatalogMenuItems_CarryLabelsGesturesAndSmokeExpectations()
    {
        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.NewSheet).Should().Be(
            new NativeMenuItemPlan(
                NativeMenuItemId.NewSheet,
                "AvaloniaNativeMenu_NewSheet",
                new NativeMenuGesturePlan(NativeMenuGestureKey.F11, NativeMenuGestureModifiers.Shift),
                UsesResourceKey: true));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.PasteSpecial).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(
                NativeMenuGestureKey.V,
                NativeMenuGestureModifiers.Meta | NativeMenuGestureModifiers.Alt));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.FlashFill).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(NativeMenuGestureKey.E, NativeMenuGestureModifiers.Control));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.PageSetup).Should().Be(
            new NativeMenuItemPlan(
                NativeMenuItemId.PageSetup,
                "AvaloniaNativeMenu_PageSetup",
                UsesResourceKey: true,
                RequiresGestureInSmoke: false));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.InsertFunction).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(NativeMenuGestureKey.F3, NativeMenuGestureModifiers.Shift));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.AutoSumSum).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(NativeMenuGestureKey.OemPlus, NativeMenuGestureModifiers.Alt));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.ShowFormulas).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(NativeMenuGestureKey.Oem3, NativeMenuGestureModifiers.Control));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.HelpOnline).Gesture
            .Should()
            .Be(new NativeMenuGesturePlan(NativeMenuGestureKey.F1));

        NativeMenuCatalog.GetMenuItem(NativeMenuItemId.RenameSheet).RequiresGestureInSmoke
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(WorkbookShortcutRoute.NewWorkbook, NativeFileMenuItemId.NewWorkbook)]
    [InlineData(WorkbookShortcutRoute.OpenWorkbook, NativeFileMenuItemId.Open)]
    [InlineData(WorkbookShortcutRoute.SaveWorkbook, NativeFileMenuItemId.Save)]
    [InlineData(WorkbookShortcutRoute.WorkbookStatistics, NativeFileMenuItemId.WorkbookStatistics)]
    public void SharedShortcutRoutes_DriveNativeFileMenuGestures(
        WorkbookShortcutRoute route,
        NativeFileMenuItemId itemId)
    {
        NativeMenuCatalog.GetFileMenuItem(itemId).Gesture
            .Should()
            .Be(ToNativeMenuGesturePlan(WorkbookKeyboardShortcutCatalog.GetNativeMenuChord(route)));
    }

    [Theory]
    [InlineData(WorkbookShortcutRoute.InsertWorksheet, NativeMenuItemId.NewSheet)]
    [InlineData(WorkbookShortcutRoute.Undo, NativeMenuItemId.Undo)]
    [InlineData(WorkbookShortcutRoute.Redo, NativeMenuItemId.Redo)]
    [InlineData(WorkbookShortcutRoute.Cut, NativeMenuItemId.Cut)]
    [InlineData(WorkbookShortcutRoute.Copy, NativeMenuItemId.Copy)]
    [InlineData(WorkbookShortcutRoute.Paste, NativeMenuItemId.Paste)]
    [InlineData(WorkbookShortcutRoute.PasteSpecial, NativeMenuItemId.PasteSpecial)]
    [InlineData(WorkbookShortcutRoute.OpenFormatCells, NativeMenuItemId.FormatCells)]
    [InlineData(WorkbookShortcutRoute.FillDown, NativeMenuItemId.FillDown)]
    [InlineData(WorkbookShortcutRoute.FillRight, NativeMenuItemId.FillRight)]
    [InlineData(WorkbookShortcutRoute.Find, NativeMenuItemId.Find)]
    [InlineData(WorkbookShortcutRoute.Replace, NativeMenuItemId.Replace)]
    [InlineData(WorkbookShortcutRoute.GoTo, NativeMenuItemId.GoTo)]
    [InlineData(WorkbookShortcutRoute.FlashFill, NativeMenuItemId.FlashFill)]
    [InlineData(WorkbookShortcutRoute.InsertFunction, NativeMenuItemId.InsertFunction)]
    [InlineData(WorkbookShortcutRoute.AutoSum, NativeMenuItemId.AutoSumSum)]
    [InlineData(WorkbookShortcutRoute.ToggleShowFormulas, NativeMenuItemId.ShowFormulas)]
    public void SharedShortcutRoutes_DriveNativeMenuGestures(
        WorkbookShortcutRoute route,
        NativeMenuItemId itemId)
    {
        NativeMenuCatalog.GetMenuItem(itemId).Gesture
            .Should()
            .Be(ToNativeMenuGesturePlan(WorkbookKeyboardShortcutCatalog.GetNativeMenuChord(route)));
    }

    [Fact]
    public void PlanMenuAvailability_MatchesAvaloniaNativeMenuRules()
    {
        var plan = NativeMenuCatalog.PlanMenuAvailability(CreateMenuAvailabilityContext());

        plan.IsEnabled(NativeMenuItemId.MoveSheetLeft).Should().BeFalse();
        plan.IsEnabled(NativeMenuItemId.MoveSheetRight).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.SelectAllSheets).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.FindNext).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.InsertTable).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.TextToColumns).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.DataTable).Should().BeFalse();
        plan.IsEnabled(NativeMenuItemId.PageMargins).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.PageMarginsNormal).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.PageSetup).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.AutoSum).Should().BeTrue();
        plan.IsEnabled(NativeMenuItemId.AutoSumSum).Should().BeTrue();
        plan.IsChecked(NativeMenuItemId.ShowGridlines).Should().BeTrue();
        plan.IsChecked(NativeMenuItemId.ShowHeadings).Should().BeFalse();
        plan.IsChecked(NativeMenuItemId.PageBreakPreview).Should().BeTrue();
        plan.IsChecked(NativeMenuItemId.ShowFormulas).Should().BeTrue();

        var busyPlan = NativeMenuCatalog.PlanMenuAvailability(CreateMenuAvailabilityContext(isIdle: false));

        busyPlan.IsEnabled(NativeMenuItemId.Undo).Should().BeTrue();
        busyPlan.IsEnabled(NativeMenuItemId.RenameSheet).Should().BeFalse();
        busyPlan.IsEnabled(NativeMenuItemId.FindNext).Should().BeFalse();
        busyPlan.IsEnabled(NativeMenuItemId.PageMargins).Should().BeFalse();
        busyPlan.IsEnabled(NativeMenuItemId.AutoSum).Should().BeFalse();
        busyPlan.IsEnabled(NativeMenuItemId.HelpOnline).Should().BeTrue();
    }

    [Fact]
    public void PlanFileMenuAvailability_MatchesAvaloniaNativeFileMenuRules()
    {
        var busyPlan = NativeMenuCatalog.PlanFileMenuAvailability(
            new NativeFileMenuAvailabilityContext(
                IsIdle: false,
                CanOpen: true,
                CanSave: true,
                CanSaveAs: false,
                CanSaveThroughStorageProvider: true));

        busyPlan.IsEnabled(NativeFileMenuItemId.NewWorkbook).Should().BeFalse();
        busyPlan.IsEnabled(NativeFileMenuItemId.Open).Should().BeTrue();
        busyPlan.IsEnabled(NativeFileMenuItemId.Save).Should().BeTrue();
        busyPlan.IsEnabled(NativeFileMenuItemId.SaveAs).Should().BeFalse();
        busyPlan.IsEnabled(NativeFileMenuItemId.ExportPdf).Should().BeFalse();
        busyPlan.IsEnabled(NativeFileMenuItemId.Options).Should().BeTrue();
        busyPlan.IsEnabled(NativeFileMenuItemId.Quit).Should().BeTrue();

        var idleWithoutStoragePlan = NativeMenuCatalog.PlanFileMenuAvailability(
            new NativeFileMenuAvailabilityContext(
                IsIdle: true,
                CanOpen: false,
                CanSave: false,
                CanSaveAs: true,
                CanSaveThroughStorageProvider: false));

        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.OpenRecent).Should().BeTrue();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.BackstageExport).Should().BeFalse();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.ExportPdf).Should().BeFalse();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.WorkbookStatistics).Should().BeTrue();
        idleWithoutStoragePlan.IsEnabled(NativeFileMenuItemId.PageSetup).Should().BeTrue();
    }

    private static string DescribeEntry(NativeFileMenuEntryPlan entry) =>
        entry.Kind == NativeMenuEntryKind.Separator
            ? "|"
            : entry.Item!.Id.ToString();

    private static IEnumerable<string> DescribeEntries(IEnumerable<NativeMenuEntryPlan> entries) =>
        entries.Select(static entry =>
            entry.Kind == NativeMenuEntryKind.Separator
                ? "|"
                : entry.ItemId!.Value.ToString());

    private static NativeMenuAvailabilityContext CreateMenuAvailabilityContext(bool isIdle = true) =>
        new(
            IsIdle: isIdle,
            CanAddSheet: true,
            ActiveSheetTabIndex: 0,
            SheetTabCount: 2,
            IsWorkbookGrouped: false,
            CanHideActiveSheet: true,
            HiddenSheetCount: 1,
            CanUndo: true,
            CanRedo: false,
            CanCut: true,
            CanCopy: true,
            CanPaste: true,
            CanPasteSpecial: true,
            CanFormatPainter: true,
            CanFindNext: true,
            CanOpenSelectedHyperlink: false,
            CanInsertPicture: true,
            CanSortSelectedRange: true,
            SelectedRangeRowCount: 3,
            SelectedRangeColCount: 1,
            SelectedRangeCellCount: 3,
            CanFillCells: true,
            CanFillDown: true,
            CanFillRight: true,
            CanFillUp: false,
            CanFillLeft: false,
            CanFillSeries: true,
            CanClear: true,
            CanBold: true,
            CanItalic: true,
            CanUnderline: true,
            CanDoubleUnderline: true,
            CanStrikethrough: true,
            CanIncreaseFontSize: true,
            CanDecreaseFontSize: true,
            CanFillColor: true,
            CanFontColor: true,
            CanBorders: true,
            CanCellStyles: true,
            CanCurrencyFormat: true,
            CanPercentFormat: true,
            CanCommaStyle: true,
            CanIncreaseDecimal: true,
            CanDecreaseDecimal: true,
            CanAlignLeft: true,
            CanAlignCenter: true,
            CanAlignRight: true,
            CanAlignTop: true,
            CanAlignMiddle: true,
            CanAlignBottom: true,
            CanWrapText: true,
            CanMergeAndCenter: true,
            IsSelectedRangeMerged: false,
            CanDecreaseIndent: true,
            CanIncreaseIndent: true,
            IsShowingGridlines: true,
            IsShowingHeadings: false,
            CanZoomIn: true,
            CanZoomOut: false,
            IsPageBreakPreview: true,
            IsShowingFormulas: true);

    private static NativeMenuGesturePlan ToNativeMenuGesturePlan(WorkbookShortcutChord chord) =>
        new(ToNativeMenuGestureKey(chord.Key), ToNativeMenuGestureModifiers(chord.Modifiers));

    private static NativeMenuGestureKey ToNativeMenuGestureKey(WorkbookShortcutKey key) =>
        key switch
        {
            WorkbookShortcutKey.C => NativeMenuGestureKey.C,
            WorkbookShortcutKey.D => NativeMenuGestureKey.D,
            WorkbookShortcutKey.D1 => NativeMenuGestureKey.D1,
            WorkbookShortcutKey.E => NativeMenuGestureKey.E,
            WorkbookShortcutKey.F => NativeMenuGestureKey.F,
            WorkbookShortcutKey.F3 => NativeMenuGestureKey.F3,
            WorkbookShortcutKey.F11 => NativeMenuGestureKey.F11,
            WorkbookShortcutKey.G => NativeMenuGestureKey.G,
            WorkbookShortcutKey.H => NativeMenuGestureKey.H,
            WorkbookShortcutKey.N => NativeMenuGestureKey.N,
            WorkbookShortcutKey.O => NativeMenuGestureKey.O,
            WorkbookShortcutKey.Oem3 => NativeMenuGestureKey.Oem3,
            WorkbookShortcutKey.OemPlus => NativeMenuGestureKey.OemPlus,
            WorkbookShortcutKey.R => NativeMenuGestureKey.R,
            WorkbookShortcutKey.S => NativeMenuGestureKey.S,
            WorkbookShortcutKey.V => NativeMenuGestureKey.V,
            WorkbookShortcutKey.X => NativeMenuGestureKey.X,
            WorkbookShortcutKey.Z => NativeMenuGestureKey.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

    private static NativeMenuGestureModifiers ToNativeMenuGestureModifiers(WorkbookShortcutModifiers modifiers)
    {
        var result = NativeMenuGestureModifiers.None;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Control))
            result |= NativeMenuGestureModifiers.Control;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Alt))
            result |= NativeMenuGestureModifiers.Alt;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Shift))
            result |= NativeMenuGestureModifiers.Shift;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Meta))
            result |= NativeMenuGestureModifiers.Meta;
        return result;
    }
}
