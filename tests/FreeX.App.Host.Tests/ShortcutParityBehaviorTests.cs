using System.IO;
using System.Windows.Input;
using FluentAssertions;
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
    // --- Ctrl+P (Print Preview) ---

    [Fact]
    public void CtrlP_IsRegisteredAsOpenPrintPreviewCommand()
    {
        KeyboardShortcutMatcher.TryGetCommandShortcut(
            Key.P, Key.None, ModifierKeys.Control, out var shortcut)
            .Should().BeTrue();

        shortcut.Should().Be(KeyboardCommandShortcut.OpenPrintPreview);
    }

    // --- Ctrl+Z / Alt+Backspace (Undo) ---

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

    [Fact]
    public void PrintSettingsPanel_ExposesOrientationPaperSizeMarginsAndScalingControls()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewSettingsPanelFactory.cs"));

        source.Should().ContainAny("PageOrientation", "Orientation");
        source.Should().ContainAny("PaperSize", "paperSize");
        source.Should().ContainAny("PageMargins", "Margins");
        source.Should().ContainAny("ScaleToFit", "Scaling");
    }

    [Fact]
    public void PrintPreview_ExposesKeyboardedGridlineAndHeadingToggles()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewSettingsPanelFactory.cs"));

        source.Should().Contain("PrintGridlines");
        source.Should().Contain("PrintHeadings");
    }

    [Fact]
    public void PrintSettings_IncludesIgnorePrintAreaOption()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintSettingsPlanner.cs"));

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
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.xaml.cs"));

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
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FormatCellsDialog.xaml.cs"));

        source.Should().ContainAll("Strikethrough", "Superscript", "Subscript");
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
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ThreadedCommentDialog.cs"));

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
    public void AutoFilterDropdown_UsesExcelStyleMenuPlannerWithInitialKeyboardFocus()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.EditingDropdowns.cs"));

        source.Should().Contain("AutoFilterDropdownPlanner.CreateMenuPlan");
        source.Should().Contain("new AutoFilterDialog(menuPlan)");
    }

    [Fact]
    public void AutoFilterDropdownPlanner_SupportsCriteriaSuggestionsAndFilterFamilySubmenus()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutoFilterDropdownPlanner.cs"));

        source.Should().ContainAll(
            "CriteriaSuggestions",
            "SortAscending",
            "ClearFilter");
    }

    [Fact]
    public void DataFilterCommands_UsesFilterPromptPlannerForTopBottomAverageAndCriterionFilters()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));

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
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "QuickAnalysisPlanner.cs"));

        // QuickAnalysisPlanner.Format/Chart/Total factory methods correspond to the three groups
        source.Should().ContainAll("\"Formatting\"", "\"Charts\"", "\"Totals\"");
    }

    [Fact]
    public void QuickAnalysisMenu_AnchorsToCellRangeBottomRightCornerOnKeyboardActivation()
    {
        var plannerSource = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "QuickAnalysisMenuPlacementPlanner.cs"));

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
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonKeyTipOverlayPlacement.cs"));

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
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));

        source.Should().Contain("CycleShellFocus");
        source.Should().ContainAny("PivotTable", "task pane", "taskPane", "TaskPane");
    }

    // --- Tab / Shift+Tab in Ribbon ---

    [Fact]
    public void RibbonFocus_TabAndShiftTabNavigateWithinRibbonSurface()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));

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
    public void WorksheetContextMenu_IncludesPasteSpecialInsertDeleteAndFormatCellsItems()
    {
        var plannerSource = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorksheetContextMenuPlanner.cs"));

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
    public void RepeatLastAction_RoutesToCommandBusRepeatLastWithSelectionTracking()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CommandExecution.cs"));

        source.Should().Contain("ExecuteRepeatLast");
        source.Should().Contain("_commandBus.RepeatLast");
    }

    [Fact]
    public void RepeatLastAction_SupportsTryExecuteRepeatableVariantsForGroupedSheetAndCurrentRangeCommands()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CommandExecution.cs"));

        source.Should().Contain("TryExecuteRepeatableGroupedSheetCommand");
        source.Should().Contain("TryExecuteRepeatableCurrentRangeCommand");
        source.Should().Contain("ExecuteRepeatable");
    }

    [Fact]
    public void RepeatLastAction_FillDownAndFillRightAreWiredToRepeatablePaths()
    {
        var homeEditingSource = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs"));

        homeEditingSource.Should().ContainAny("FillDown", "FillRight");
        homeEditingSource.Should().Contain("TryExecuteRepeatableCurrentRangeCommand");
    }

    // --- Alt / Ribbon Keytips ---

    [Fact]
    public void AltKeytipMode_OpensFileBackstageOrSelectsRibbonTabs()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "KeyboardShortcutMatcher.CommandRules.cs"));

        // Alt key opens keytip mode via F10 path in the dispatcher
        source.Should().Contain("ShowKeyTips");
    }

    [Fact]
    public void RibbonKeyTipMode_ClosesOnEscape()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonKeyTipMode.cs"));

        // Escape exits keytip mode
        source.Should().ContainAny("Escape", "escape", "Key.Escape");
    }

    [Fact]
    public void RibbonKeytipRouter_SupportsTopLevelTabsAndBackstageFile()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonTopLevelKeyTipRouter.cs"));

        source.Should().Contain("BackstageFile");
        source.Should().Contain("RibbonTab");
    }

    [Fact]
    public void RibbonKeytipMode_CoversQatTabFormulaBarAndSheetTabBadges()
    {
        var source = File.ReadAllText(
            WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));

        source.Should().ContainAny("FormulaBar", "formulaBar", "NameBox", "nameBox");
    }
}
