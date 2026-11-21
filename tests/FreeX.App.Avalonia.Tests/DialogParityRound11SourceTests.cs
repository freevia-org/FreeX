using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-11 dialog/task-pane parity sweep: pins the WPF-matching structure of the dialogs fixed in this
/// pass (the WPF host classes are named in each test).
/// </summary>
public sealed class DialogParityRound11SourceTests
{
    [Fact]
    public void EditIssue_AlsoShowsOwnedWarningOverOpenModalDialog()
    {
        var main = Read("MainWindow.cs");
        var warnings = Read("MainWindow.DialogWarnings.cs");

        main.Should().Contain("ShowWarningOverOpenModalDialog(message);");
        warnings.Should().Contain("FindTopmostOpenModalDialog");
        warnings.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(dialog, message, dialog.Title");
        warnings.Should().Contain("_modelessOwnedWindows");
        Read("MainWindow.ModelessDialogs.cs").Should().Contain("_modelessOwnedWindows.Add(dialog);");
    }

    [Fact]
    public void ProtectDialogs_UseWpfTwoStepConfirmPasswordFlow()
    {
        var source = Read("MainWindow.Protection.cs");

        source.Should().Contain("ShowConfirmPasswordDialogAsync");
        source.Should().Contain("Protection_ConfirmPassword");
        source.Should().Contain("Protection_ReenterPasswordToProceed");
        source.Should().Contain("Protection_TheConfirmationPasswordDoesNotMatch");
        source.Should().Contain("ConfirmProtectionPasswordBox");
        source.Should().NotContain("ProtectSheetConfirmBox");
        source.Should().NotContain("ProtectWorkbookConfirmBox");
        // Protect Workbook offers Excel's Structure (checked) / Windows (unchecked) toggles, as in WPF.
        source.Should().Contain("ProtectWorkbookStructureBox");
        source.Should().Contain("ProtectWorkbookWindowsBox");
        source.Should().Contain("ProtectStructure = structureBox.IsChecked == true,");
        source.Should().Contain("ProtectWindows = windowsBox.IsChecked == true,");
    }

    [Fact]
    public void PivotChartType_HasDedicatedWpfPicker()
    {
        var picker = Read("MainWindow.InsertChartDialog.cs");

        picker.Should().Contain("ShowPivotChartTypeDialogAsync");
        picker.Should().Contain("PivotChartType_ChangePivotChartType");
        picker.Should().Contain("PivotChartType_RecommendedPivotCharts");
        picker.Should().Contain("PivotChartType_AllCharts");
        picker.Should().Contain("PivotChartTypeDialogWidth = 640");
        picker.Should().Contain("PivotChartTypeDialogHeight = 410");
        picker.Should().Contain("PivotChartTypeTabsHeight = 290");
        Read("MainWindow.PivotChartOptions.cs").Should().Contain("ShowPivotChartTypeDialogAsync(chart!.Type)");
        Read("MainWindow.PivotChart.cs").Should().Contain("ShowPivotChartTypeDialogAsync(ChartType.Column)");
    }

    [Fact]
    public void PivotDialogs_DoNotCoupleEnabledStatesWpfLeavesIndependent()
    {
        Read("MainWindow.PivotChartOptions.cs").Should().NotContain("SyncSubButtons");
        Read("MainWindow.PivotOptions.cs").Should().NotContain("SyncSubtotalState");
        Read("MainWindow.PivotGroupField.cs").Should().Contain("rangePanel.IsEnabled = true;");
    }

    [Fact]
    public void PivotFilterDialogs_UseWpfDefaults()
    {
        var source = Read("MainWindow.PivotFilters.cs");

        source.Should().Contain("PivotFieldFilterPlanner.DefaultLabelKindIndex");
        source.Should().Contain("PivotFieldFilterPlanner.DefaultValueKindIndex");
        source.Should().Contain("PivotFieldFilterPlanner.DefaultValueFilterPrimaryText");
    }

    [Fact]
    public void InsertSparklineDialog_MatchesWpfTitleDefaultsFocusAndWarnings()
    {
        var source = Read("MainWindow.Sparklines.cs");

        source.Should().Contain("Title = UiText.Get(\"Sparkline_InsertSparkline\")");
        source.Should().Contain("initialLocationText ?? string.Empty");
        source.Should().Contain("initialDataRangeText ?? FormatRangeReference(selection)");
        source.Should().Contain("Sparkline_InvalidLocationCell");
        source.Should().NotContain("ShowEditIssue(UiText.Get(\"Sparkline_Invalid");
        source.Should().Contain("AvaloniaCompactDialogChrome.FocusAndSelect(dataRangeBox)");
    }

    [Fact]
    public void ChartDialogs_FocusTheWpfInitialControl()
    {
        var titles = Read("MainWindow.ChartTabs.cs");
        titles.Should().Contain("\"ChartTitlesDialog\", chartTitleBox)");
        titles.Should().Contain("subtypeGallery.DoubleTapped");

        var remaining = Read("MainWindow.ChartRemainingDialogs.cs");
        remaining.Should().Contain("ChartMovePlanner.DialogAutomationId, objectRadio)");
        remaining.Should().Contain("\"FormatChartAreaDialog\", chartAreaEditor.Box)");

        var format = Read("MainWindow.ChartFormatDialogs.cs");
        format.Should().Contain("\"ChartDataLabelsDialog\", showCheck)");
        format.Should().Contain("\"ChartSeriesFormatDialog\", seriesCombo)");
        format.Should().Contain("\"ChartTrendlineDialog\", showCheck)");
        format.Should().Contain("\"ChartErrorBarsDialog\", showCheck)");

        var typeFormat = Read("MainWindow.ChartTypeFormatDialogs.cs");
        typeFormat.Should().Contain("ChartBarFormatPlanner.DialogAutomationId, gapWidthBox)");
        typeFormat.Should().Contain("ChartPieFormatPlanner.DialogAutomationId, angleBox)");
        typeFormat.Should().Contain("ChartBubbleFormatPlanner.DialogAutomationId, scaleBox)");
        typeFormat.Should().Contain("ChartStockFormatPlanner.DialogAutomationId, gapWidthBox)");
    }

    [Fact]
    public void DataDialogs_MatchWpfResizeFocusAndEnabledRules()
    {
        var main = Read("MainWindow.cs");
        main.Should().Contain("headersCheck.IsEnabled = !optionsState.LeftToRight;");
        main.Should().Contain("dialog.Opened += (_, _) => caseSensitiveBox.Focus();");
        main.Should().Contain("new(\"Fill\", CellHAlign.Fill)");
        main.Should().Contain("new(\"Center\", CellVAlign.Center)");
        main.Should().NotContain("new(\"Middle\", CellVAlign.Center)");
        // Sources are LF-only (.gitattributes eol=lf); normalise so the match holds on Windows checkouts too.
        main.Replace("\r\n", "\n").Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(\n                    dialog,\n                    UiText.Get(\"GoTo_ReferenceIsNotValid\")");
        main.Should().Contain("_session.TryResolveReferenceRange(inputBox.Text");
        main.Should().Contain("var findBox = new TextBox { Text = string.Empty");
        main.Should().NotContain("if (OnReplaceTab()) DoReplace(); else DoFindNext();");
    }

    [Fact]
    public void SheetAndNameDialogs_MatchWpfInteractions()
    {
        Read("MainWindow.MoveCopySheet.cs").Should().Contain("beforeSheetList.DoubleTapped");
        Read("MainWindow.SheetTabPointer.cs").Should().Contain("targets.Count > 0 ? targets[0] : null");
        Read("MainWindow.Spelling.cs").Should().Contain("suggestionList.DoubleTapped");
        Read("MainWindow.CustomViews.cs").Should().Contain("UiText.Get(\"CustomViews_DefaultName\")");
        Read("MainWindow.SheetOptionsNotes.cs").Should().Contain("ReviewCommentList_OpenButton");

        var names = Read("MainWindow.DefinedNames.cs");
        names.Should().Contain("NamedRange_DeleteConfirmation");
        names.Should().Contain("UserMessageButtons.YesNo");
        names.Should().Contain("namesList.DoubleTapped");
    }

    [Fact]
    public void ConditionalFormatDialogs_MatchWpfThresholdAndManageBehaviour()
    {
        var source = Read("MainWindow.ConditionalFormat.cs");

        source.Should().Contain("Remaining_EnterThresholdValue");
        source.Should().Contain("ConditionalFormatThresholdDialogPlanner.TryCreateResult");
        source.Should().Contain("UiText.Get(\"Remaining_NewFormattingRule\")");
        source.Should().Contain("startRuleType: CfRuleType.DataBar");
        source.Should().Contain("Reload(selectIndex: deletedIndex)");
    }

    private static string Read(string avaloniaFile) =>
        File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "src",
            "FreeX.App.Avalonia",
            avaloniaFile));
}
