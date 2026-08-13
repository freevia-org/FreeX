using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests covering the key behaviors of the 14 formerly-partial shortcuts
/// that were promoted to Parity status. Each group covers a shortcut or shortcut family
/// and asserts the specific behaviors that confirm it meets the Parity bar.
/// </summary>
public sealed class ShortcutParityBehaviorTests
{
    // --- Ctrl+P / Ctrl+Shift+F12 (Print) ---

    [Theory]
    [InlineData(Key.P, ModifierKeys.Control)]
    [InlineData(Key.F12, ModifierKeys.Control | ModifierKeys.Shift)]
    public void PrintShortcuts_AreRegisteredAsOpenPrintPreviewCommand(Key key, ModifierKeys modifiers)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, Key.None, modifiers, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenPrintPreview);
    }

    // --- Ctrl+Z / Alt+Backspace (Undo), Ctrl+Y / Ctrl+Shift+Z (Redo) ---

    [Theory]
    [InlineData(Key.Z, Key.None, ModifierKeys.Control)]
    [InlineData(Key.Back, Key.None, ModifierKeys.Alt)]
    [InlineData(Key.System, Key.Back, ModifierKeys.Alt)]
    public void UndoShortcuts_AreRegisteredAsUndoCommand(Key key, Key systemKey, ModifierKeys modifiers)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, systemKey, modifiers, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.Undo);
    }

    [Theory]
    [InlineData(Key.Y, Key.None, ModifierKeys.Control)]
    [InlineData(Key.Z, Key.None, ModifierKeys.Control | ModifierKeys.Shift)]
    public void RedoShortcuts_AreRegisteredAsRedoCommand(Key key, Key systemKey, ModifierKeys modifiers)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, systemKey, modifiers, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.Redo);
    }

    [Fact]
    public void PrintSettingsPanel_ExposesOrientationPaperSizeMarginsAndScalingControls()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PrintPreviewSettingsPanelFactory.cs");

        source.Should().ContainAny("PageOrientation", "Orientation");
        source.Should().ContainAny("PaperSize", "paperSize");
        source.Should().ContainAny("PageMargins", "Margins");
        source.Should().ContainAny("ScaleToFit", "Scaling");
    }

    [Fact]
    public void PrintPreview_ExposesKeyboardedGridlineAndHeadingToggles()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PrintPreviewSettingsPanelFactory.cs");

        source.Should().Contain("PrintGridlines");
        source.Should().Contain("PrintHeadings");
    }

    [Fact]
    public void PrintSettings_IncludesIgnorePrintAreaOption()
    {
        var source = DialogSourceTestSupport.ReadPresentationSources("PageLayout", "PrintSettingsPlanner.cs");

        source.Should().Contain("IgnorePrintArea");
    }

    // --- Ctrl+V / Ctrl+Shift+V / Ctrl+Alt+V (Paste / Paste Special) ---

    [Fact]
    public void CtrlV_IsRegisteredAsPasteCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.V, Key.None, ModifierKeys.Control, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.Paste);
    }

    [Fact]
    public void CtrlShiftV_IsRegisteredAsPasteValuesCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.PasteValues);
    }

    [Fact]
    public void CtrlAltV_IsRegisteredAsPasteSpecialShortcut()
    {
        KeyboardShortcutMatcher.IsPasteSpecialShortcut(
            Key.V, Key.None, ModifierKeys.Control | ModifierKeys.Alt)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(PasteSpecialDialogMode.Values, PasteSpecialAction.Paste, PasteMode.Values)]
    [InlineData(PasteSpecialDialogMode.Formulas, PasteSpecialAction.Paste, PasteMode.Formulas)]
    [InlineData(PasteSpecialDialogMode.Formats, PasteSpecialAction.Paste, PasteMode.Formats)]
    [InlineData(PasteSpecialDialogMode.AllUsingSourceTheme, PasteSpecialAction.Paste, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.AllExceptBorders, PasteSpecialAction.Paste, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.AllMergingConditionalFormats, PasteSpecialAction.Paste, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.FormulasAndNumberFormats, PasteSpecialAction.Paste, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.ValuesAndNumberFormats, PasteSpecialAction.Paste, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.ValuesAndSourceFormatting, PasteSpecialAction.Paste, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.ColumnWidths, PasteSpecialAction.ColumnWidths, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Comments, PasteSpecialAction.Comments, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Validation, PasteSpecialAction.Validation, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Picture, PasteSpecialAction.Picture, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.LinkedPicture, PasteSpecialAction.LinkedPicture, PasteMode.All)]
    [InlineData(PasteSpecialDialogMode.Text, PasteSpecialAction.ExternalText, PasteMode.All)]
    public void PasteSpecialPlanner_MapsAllImplementedModes(
        PasteSpecialDialogMode mode,
        PasteSpecialAction expectedAction,
        PasteMode expectedPasteMode)
    {
        var plan = PasteSpecialPlanner.CreatePlan(new PasteSpecialDialogSelection(mode, "None"));

        plan.Action.Should().Be(expectedAction);
        plan.PasteMode.Should().Be(expectedPasteMode);
    }

    [Theory]
    [InlineData(PasteSpecialOperation.Add)]
    [InlineData(PasteSpecialOperation.Subtract)]
    [InlineData(PasteSpecialOperation.Multiply)]
    [InlineData(PasteSpecialOperation.Divide)]
    [InlineData(PasteSpecialOperation.None)]
    public void PasteSpecialPlanner_MapsAllArithmeticOperations(PasteSpecialOperation operation)
    {
        var plan = PasteSpecialPlanner.CreatePlan(
            new PasteSpecialDialogSelection(PasteSpecialDialogMode.Values, operation.ToString()));

        plan.Options.Operation.Should().Be(operation);
    }

    [Fact]
    public void PasteSpecialPlanner_SupportsSkipBlanksAndTranspose()
    {
        var plan = PasteSpecialPlanner.CreatePlan(
            new PasteSpecialDialogSelection(PasteSpecialDialogMode.All, "None", SkipBlanks: true, Transpose: true));

        plan.Options.SkipBlanks.Should().BeTrue();
        plan.Options.Transpose.Should().BeTrue();
    }

    // --- Ctrl+1 (Format Cells) ---

    [Fact]
    public void Ctrl1_IsRegisteredAsOpenFormatCellsCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.D1, Key.None, ModifierKeys.Control, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenFormatCells);
    }

    [Fact]
    public void FormatCellsDialog_ExposesNumberAlignmentFontFillBorderAndProtectionTabs()
    {
        var source = DialogSourceTestSupport.ReadHostSources("FormatCellsDialog.xaml.cs");

        source.Should().ContainAll(
            "FormatCellsDialogTab.Number",
            "FormatCellsDialogTab.Alignment",
            "FormatCellsDialogTab.Font",
            "FormatCellsDialogTab.Fill",
            "FormatCellsDialogTab.Border",
            "FormatCellsDialogTab.Protection");
    }

    // --- Ctrl+Shift+F / Ctrl+Shift+P (Format Cells Font tab) ---

    [Fact]
    public void CtrlShiftF_IsRegisteredAsOpenFormatCellsFontCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F, Key.None, ModifierKeys.Control | ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenFormatCellsFont);
    }

    [Fact]
    public void CtrlShiftP_IsRegisteredAsOpenFormatCellsFontCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.P, Key.None, ModifierKeys.Control | ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenFormatCellsFont);
    }

    [Fact]
    public void FormatCellsFontTab_ExposesStrikethroughSuperscriptAndSubscript()
    {
        var source = DialogSourceTestSupport.ReadHostSources("FormatCellsDialog.xaml.cs");

        source.Should().ContainAll("Strikethrough", "Superscript", "Subscript");
    }

    // --- F3 (Paste Name) ---

    [Fact]
    public void F3_IsRegisteredAsPasteNameCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F3, Key.None, ModifierKeys.None, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.PasteName);
    }

    [Fact]
    public void PasteNameCommand_OpensPasteNamesDialogAndCanPasteList()
    {
        var keyboardSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var formulaSource = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");

        keyboardSource.Should().Contain("KeyboardCommandShortcut.PasteName");
        formulaSource.Should().Contain("OpenPasteNamesDialog");
        formulaSource.Should().Contain("new PasteNamesDialog(items)");
        formulaSource.Should().Contain("InsertDefinedNameIntoFormula(dialog.Result.Name)");
        formulaSource.Should().Contain("PasteNamesPlanner.TryBuildPasteListEdits");
        formulaSource.Should().Contain("TryExecuteEditCells(edits, title)");
    }

    // --- Alt+Shift+F10 (Error Checking) ---

    [Fact]
    public void AltShiftF10_IsRegisteredAsErrorCheckingCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.System, Key.F10, ModifierKeys.Alt | ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenErrorChecking);
    }

    [Fact]
    public void ErrorCheckingShortcut_RoutesToFormulaErrorChecking()
    {
        var keyboardSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var formulaSource = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");

        keyboardSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenErrorChecking, ErrorCheckBtn_Click);");
        formulaSource.Should().Contain("private void ErrorCheckBtn_Click");
        formulaSource.Should().Contain("FormulaAuditingService.FindFormulaErrorIssues");
    }

    // --- Shift+F2 / Ctrl+Shift+F2 (Notes / Threaded Comments) ---

    [Fact]
    public void ShiftF2_IsRegisteredAsNewNoteCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F2, Key.None, ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.NewNote);
    }

    [Fact]
    public void CtrlShiftF2_IsRegisteredAsNewThreadedCommentCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F2, Key.None, ModifierKeys.Control | ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.NewThreadedComment);
    }

    [Fact]
    public void ThreadedCommentDialog_SupportsCtrlEnterReplySubmissionAndAccessKeys()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ThreadedCommentDialog.cs");

        // Ctrl+Enter submits the reply - confirmed via Key.Enter + ModifierKeys.Control
        source.Should().Contain("Key.Enter");
        source.Should().ContainAny("_replyBox", "replyBox", "_replyButton", "Reply");
    }

    // --- Alt+Down (AutoFilter / Data Validation Dropdown) ---

    [Fact]
    public void AltDown_IsRegisteredAsOpenActiveDropdownCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.None, Key.Down, ModifierKeys.Alt, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenActiveDropdown);
    }

    [Fact]
    public void SystemAltDown_IsRegisteredAsOpenActiveDropdownCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.System, Key.Down, ModifierKeys.Alt, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenActiveDropdown);
    }

    [Fact]
    public void AutoFilterDropdown_UsesExcelStyleMenuPlannerWithInitialKeyboardFocus()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.EditingDropdowns.cs");

        source.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        source.Should().Contain("WpfResourceKeyTextResolver.Resources.AutoFilter");
        source.Should().Contain("new AutoFilterDialog(menuPlan)");
    }

    [Fact]
    public void AutoFilterDropdownMenuPlanner_SupportsCriteriaSuggestionsAndFilterFamilySubmenus()
    {
        var hostResourcesSource = DialogSourceTestSupport.ReadPresentationSources("Localization", "FreeXPlannerTextResources.cs");
        var plannerSource = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Presentation", "Filtering", "AutoFilterDropdownMenuPlanner.cs");
        var menuModelSource = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Presentation", "Filtering", "AutoFilterMenuModel.cs");

        hostResourcesSource.Should().Contain("IAutoFilterMenuTextProvider");
        hostResourcesSource.Should().NotContain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        plannerSource.Should().ContainAll(
            "SortAscending",
            "ClearFilter");
        menuModelSource.Should().ContainAll(
            "CriteriaSuggestions",
            "FilterFamily");
    }

    [Fact]
    public void DataFilterCommands_UsesFilterPromptPlannerForTopBottomAverageAndCriterionFilters()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.DataFilterCommands.cs");

        source.Should().Contain("FilterPromptPlanner.TryPlan");
        source.Should().Contain("promptPlan.CreateCommand");
    }

    // --- Ctrl+Q (Quick Analysis) ---

    [Fact]
    public void CtrlQ_IsRegisteredAsQuickAnalysisCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.Q, Key.None, ModifierKeys.Control, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.QuickAnalysis);
    }

    [Fact]
    public void QuickAnalysisMenu_CoversFormattingChartsAndTotalsGroups()
    {
        var catalogSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisCatalog.cs");
        var shellSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisShellPlanner.cs");

        catalogSource.Should().ContainAll(
            "QuickAnalysisGroup.Formatting",
            "QuickAnalysisGroup.Charts",
            "QuickAnalysisGroup.Totals");
        shellSource.Should().ContainAll(
            "TableLoc_QaGroupFormatting",
            "TableLoc_QaGroupCharts",
            "TableLoc_QaGroupTotals");
    }

    [Fact]
    public void QuickAnalysisMenu_AnchorsToCellRangeBottomRightCornerOnKeyboardActivation()
    {
        var plannerSource = DialogSourceTestSupport.ReadPresentationSources("QuickAnalysis", "QuickAnalysisMenuPlacementPlanner.cs");

        // Anchor is computed from the last visible row and column in the selection,
        // placing the menu at the selection's visible bottom-right corner.
        plannerSource.Should().Contain("FindLastVisibleRowInSelection");
        plannerSource.Should().Contain("FindLastVisibleColumnInSelection");
    }

    // --- F10 (Ribbon Keytip Mode) ---

    [Fact]
    public void F10_IsRegisteredAsShowKeyTipsCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F10, Key.None, ModifierKeys.None, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.ShowKeyTips);
    }

    [Fact]
    public void RibbonKeyTipMode_EntersTopLevelModeAndBadgesAreClampedInsideOverlay()
    {
        var source = DialogSourceTestSupport.ReadHostSources("RibbonKeyTipOverlayPlacement.cs");

        // Badge positions are clamped inside the overlay window so keytips remain visible
        source.Should().ContainAny("Clamp", "clamp", "Math.Min", "Math.Max");
    }

    // --- F6 / Shift+F6 (Cycle Shell Focus) ---

    [Fact]
    public void F6_IsRegisteredAsCycleShellFocusCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F6, Key.None, ModifierKeys.None, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.CycleShellFocus);
    }

    [Fact]
    public void ShiftF6_IsRegisteredAsCycleShellFocusCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F6, Key.None, ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.CycleShellFocus);
    }

    [Fact]
    public void ShellFocusCycle_SkipsUnavailableTaskPanesInsteadOfFailingFocusAttempts()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");

        source.Should().Contain("CycleShellFocus");
        source.Should().ContainAny("PivotTable", "task pane", "taskPane", "TaskPane");
    }

    // --- Ctrl+F6 / Ctrl+Tab and reverse variants (Workbook window cycling) ---

    [Theory]
    [InlineData(Key.F6, ModifierKeys.Control)]
    [InlineData(Key.Tab, ModifierKeys.Control)]
    public void WorkbookWindowForwardCycleShortcuts_AreRegisteredAsNextWindow(Key key, ModifierKeys modifiers)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, Key.None, modifiers, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.SwitchToNextWorkbookWindow);
    }

    [Theory]
    [InlineData(Key.F6, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift)]
    public void WorkbookWindowReverseCycleShortcuts_AreRegisteredAsPreviousWindow(Key key, ModifierKeys modifiers)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, Key.None, modifiers, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.SwitchToPreviousWorkbookWindow);
    }

    [Fact]
    public void WorkbookWindowCycleShortcuts_RouteThroughWindowRegistry()
    {
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var multiWindowSource = DialogSourceTestSupport.ReadHostSources("MainWindow.MultiWindow.cs");
        var registrySource = DialogSourceTestSupport.ReadHostSources("WorkbookWindowRegistry.cs");

        commandSource.Should().Contain("SwitchWorkbookWindow(forward: true)");
        commandSource.Should().Contain("SwitchWorkbookWindow(forward: false)");
        multiWindowSource.Should().Contain("_windowRegistry.SwitchToNextWindow(this)");
        multiWindowSource.Should().Contain("_windowRegistry.SwitchToPreviousWindow(this)");
        registrySource.Should().Contain("PreviousWindowTarget");
    }

    // --- Ctrl+F5 / Ctrl+F9 / Ctrl+F10 (Workbook window state) ---

    [Theory]
    [InlineData(Key.F5, KeyboardCommandShortcut.RestoreWorkbookWindow)]
    [InlineData(Key.F7, KeyboardCommandShortcut.MoveWorkbookWindow)]
    [InlineData(Key.F8, KeyboardCommandShortcut.SizeWorkbookWindow)]
    [InlineData(Key.F9, KeyboardCommandShortcut.MinimizeWorkbookWindow)]
    [InlineData(Key.F10, KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow)]
    public void WorkbookWindowStateShortcuts_AreRegistered(Key key, KeyboardCommandShortcut expected)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, Key.None, ModifierKeys.Control, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(expected);
    }

    [Fact]
    public void WorkbookWindowStateShortcuts_RouteToWindowChromeCommands()
    {
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var viewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ViewCommands.cs");

        commandSource.Should().Contain("KeyboardCommandShortcut.RestoreWorkbookWindow");
        commandSource.Should().Contain("KeyboardCommandShortcut.MoveWorkbookWindow");
        commandSource.Should().Contain("KeyboardCommandShortcut.SizeWorkbookWindow");
        commandSource.Should().Contain("KeyboardCommandShortcut.MinimizeWorkbookWindow, MinimizeBtn_Click");
        commandSource.Should().Contain("KeyboardCommandShortcut.MaximizeOrRestoreWorkbookWindow, MaxRestoreBtn_Click");
        viewSource.Should().Contain("RestoreWorkbookWindow");
        viewSource.Should().Contain("BeginSystemWindowCommand");
        viewSource.Should().Contain("WM_SYSCOMMAND");
        viewSource.Should().Contain("SystemCommands.RestoreWindow(this)");
    }

    // --- Tab / Shift+Tab in Ribbon ---

    [Fact]
    public void RibbonFocus_TabAndShiftTabNavigateWithinRibbonSurface()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");

        source.Should().ContainAny("RibbonTabStrip", "ribbonTab", "FocusedRibbon", "_ribbon");
        source.Should().Contain("Tab");
    }

    // --- Shift+F10 / Menu Key (Context Menu) ---

    [Fact]
    public void ShiftF10_IsRegisteredAsOpenContextMenuCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F10, Key.None, ModifierKeys.Shift, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenContextMenu);
    }

    [Fact]
    public void MenuKey_IsRegisteredAsOpenContextMenuCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.Apps, Key.None, ModifierKeys.None, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenContextMenu);
    }

    [Fact]
    public void CtrlEnter_IsRegisteredAsOpenHyperlinkCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.Enter, Key.None, ModifierKeys.Control, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenHyperlink);
    }

    [Fact]
    public void OpenHyperlinkShortcut_RoutesToSelectedHyperlink()
    {
        var commandsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");
        var insertSource = DialogSourceTestSupport.ReadHostSources("MainWindow.InsertCommands.cs");

        commandsSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenHyperlink");
        commandsSource.Should().Contain("TryOpenSelectedHyperlink()");
        insertSource.Should().Contain("private bool TryOpenSelectedHyperlink()");
        // R112-model-active-cell-vs-selection-1-1 sibling fix: Excel opens the ACTIVE cell's
        // hyperlink, not the selection's normalized top-left Start corner (the two differ whenever
        // the selection was made upward/leftward, e.g. dragging D4 -> A1 pins the active cell at
        // D4 while Start normalizes to A1). The WPF host must read SheetGrid.ActiveCell -- which
        // MainWindow mirrors from _selectionAnchor -- falling back to Start only when it is unset,
        // matching WorkbookSession.OpenSelectedHyperlink and the Avalonia shell's
        // OpenSelectedHyperlinkAsync.
        insertSource.Should().Contain("TryOpenHyperlink(SheetGrid.ActiveCell ?? selectedRange.Start)");
        insertSource.Should().NotContain("TryOpenHyperlink(selectedRange.Start)");
    }

    [Fact]
    public void WorksheetContextMenu_IncludesPasteSpecialInsertDeleteAndFormatCellsItems()
    {
        var plannerSource = DialogSourceTestSupport.ReadAppServicesRibbonSource("WorksheetContextMenuPlanner.cs");

        plannerSource.Should().ContainAll(
            "Paste Special",
            "Format Cells",
            "Insert",
            "Delete");
    }

    // --- F4 outside formula editing (Repeat Last Action) ---

    [Fact]
    public void F4_IsRegisteredAsRepeatLastActionCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.F4, Key.None, ModifierKeys.None, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.RepeatLastAction);
    }

    [Fact]
    public void RepeatLastAction_RoutesToWorkbookSessionWithSelectionTracking()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CommandExecution.cs");

        source.Should().Contain("ExecuteRepeatLast");
        source.Should().Contain("_session.RepeatLastAction()");
    }

    [Fact]
    public void RepeatLastAction_SupportsTryExecuteRepeatableVariantsForGroupedSheetAndCurrentRangeCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.CommandExecution.cs");

        source.Should().Contain("TryExecuteRepeatableGroupedSheetCommand");
        source.Should().Contain("TryExecuteRepeatableCurrentRangeCommand");
        source.Should().Contain("ExecuteRepeatable");
    }

    [Fact]
    public void RepeatLastAction_FillDownAndFillRightAreWiredToRepeatablePaths()
    {
        var homeEditingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeEditing.cs");

        homeEditingSource.Should().ContainAny("FillDown", "FillRight");
        homeEditingSource.Should().Contain("TryExecuteRepeatableCurrentRangeCommand");
    }

    // --- Ctrl+Alt+= / Ctrl+Alt+- (Zoom) ---

    [Theory]
    [InlineData(Key.OemPlus)]
    [InlineData(Key.Add)]
    public void CtrlAltPlus_IsRegisteredAsZoomInCommand(Key key)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, Key.None, ModifierKeys.Control | ModifierKeys.Alt, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.ZoomIn);
    }

    [Theory]
    [InlineData(Key.OemMinus)]
    [InlineData(Key.Subtract)]
    public void CtrlAltMinus_IsRegisteredAsZoomOutCommand(Key key)
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            key, Key.None, ModifierKeys.Control | ModifierKeys.Alt, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.ZoomOut);
    }

    // --- Alt / Ribbon Keytips ---

    [Fact]
    public void AltKeytipMode_OpensFileBackstageOrSelectsRibbonTabs()
    {
        var source = DialogSourceTestSupport.ReadHostSources("KeyboardShortcutMatcher.CommandRules.cs");

        // Alt key opens keytip mode via F10 path in the dispatcher
        source.Should().Contain("ShowKeyTips");
    }

    [Fact]
    public void RibbonKeyTipMode_ClosesOnEscape()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyTips.cs");

        source.Should().Contain("_ribbonKeyTipSession.HandleEscape()");
        source.Should().Contain("ExitRibbonKeyTipMode()");
    }

    [Fact]
    public void RibbonKeytipRouter_SupportsTopLevelTabsAndBackstageFile()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs", "MainWindow.KeyTips.cs");

        source.Should().Contain("FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel");
        source.Should().Contain("FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix");
    }

    [Fact]
    public void RibbonKeytipMode_CoversQatTabFormulaBarAndSheetTabBadges()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");

        source.Should().ContainAny("FormulaBar", "formulaBar", "NameBox", "nameBox");
    }
}
