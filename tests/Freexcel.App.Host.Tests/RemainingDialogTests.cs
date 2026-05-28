using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Freexcel.App.Host.Tests;

public sealed class RemainingDialogTests
{
    [Fact]
    public void ConditionalFormatThresholdDialog_CreateResult_TrimsThresholdText()
    {
        ConditionalFormatThresholdDialog.CreateResult("  100  ")
            .Should()
            .Be(new ConditionalFormatThresholdDialogResult("100"));
    }

    [Fact]
    public void ConditionalFormatThresholdDialog_TryCreateResult_RejectsBlankThreshold()
    {
        ConditionalFormatThresholdDialog.TryCreateResult(" ", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be("Enter a threshold value.");
    }

    [Fact]
    public void ConditionalFormatThresholdDialog_TryCreateResult_AcceptsTrimmedThreshold()
    {
        ConditionalFormatThresholdDialog.TryCreateResult("  100  ", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ConditionalFormatThresholdDialogResult("100"));
    }

    [Fact]
    public void ConditionalFormatThresholdDialog_AcceptWarnsAndRefocusesBlankThreshold()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ConditionalFormatThresholdDialog", "public sealed record RowHeightDialogResult");

        source.Should().Contain("if (!TryCreateResult(_thresholdBox.Text, out var result, out var error))");
        source.Should().Contain("ShowInvalidInputWarning(error ?? \"Enter a threshold value.\");");
        source.Should().Contain("MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);");
        source.Should().Contain("_thresholdBox.Focus();");
        source.Should().Contain("_thresholdBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_thresholdBox);");
    }

    [Fact]
    public void ConditionalFormatThresholdDialogOpenedFromKeyboard_FocusesThresholdBox()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ConditionalFormatThresholdDialog", "public sealed record RowHeightDialogResult");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_thresholdBox.Focus();");
        source.Should().Contain("_thresholdBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_thresholdBox);");
    }

    [Fact]
    public void RowHeightDialog_TryCreateResult_RejectsNegativeHeights()
    {
        RowHeightDialog.TryCreateResult("-1", out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a row height from 0 to 409.");
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("409", 409)]
    public void RowHeightDialog_TryCreateResult_AcceptsExcelRowHeightBounds(string input, double expected)
    {
        RowHeightDialog.TryCreateResult(input, out var result, out var error).Should().BeTrue(error);

        result.Should().Be(new RowHeightDialogResult(expected));
    }

    [Fact]
    public void RowHeightDialog_TryCreateResult_RejectsOversizedExcelRowHeight()
    {
        RowHeightDialog.TryCreateResult("409.1", out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a row height from 0 to 409.");
    }

    [Fact]
    public void RowHeightDialogOpenedFromKeyboard_FocusesHeightBox()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class RowHeightDialog", "public sealed record ColumnWidthDialogResult");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_heightBox.Focus();");
        source.Should().Contain("_heightBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_heightBox);");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void RowHeightDialog_TryCreateResult_RejectsNonFiniteHeights(string input)
    {
        RowHeightDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a row height from 0 to 409.");
    }

    [Fact]
    public void ColumnWidthDialog_TryCreateResult_AcceptsPositiveWidth()
    {
        ColumnWidthDialog.TryCreateResult("8.5", out var result, out _).Should().BeTrue();

        result.Should().Be(new ColumnWidthDialogResult(8.5));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("255", 255)]
    public void ColumnWidthDialog_TryCreateResult_AcceptsExcelColumnWidthBounds(string input, double expected)
    {
        ColumnWidthDialog.TryCreateResult(input, out var result, out var error).Should().BeTrue(error);

        result.Should().Be(new ColumnWidthDialogResult(expected));
    }

    [Fact]
    public void ColumnWidthDialog_TryCreateResult_RejectsOversizedExcelColumnWidth()
    {
        ColumnWidthDialog.TryCreateResult("255.1", out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a column width from 0 to 255.");
    }

    [Fact]
    public void ColumnWidthDialogOpenedFromKeyboard_FocusesWidthBox()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ColumnWidthDialog", "public sealed record __NoNextRemainingDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_widthBox.Focus();");
        source.Should().Contain("_widthBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_widthBox);");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ColumnWidthDialog_TryCreateResult_RejectsNonFiniteWidths(string input)
    {
        ColumnWidthDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a column width from 0 to 255.");
    }

    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_AcceptsNegativeStep()
    {
        FillSeriesStepDialog.TryCreateResult("-2", out var result, out _).Should().BeTrue();

        result.Should().Be(new FillSeriesStepDialogResult(-2));
    }

    [Fact]
    public void FillSeriesStepDialog_CreateResult_CapturesExcelSeriesOptions()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("enum FillSeriesDirection");
        source.Should().Contain("enum FillSeriesType");
        source.Should().Contain("enum FillSeriesDateUnit");
        source.Should().Contain("FillSeriesDirection.Rows");
        source.Should().Contain("FillSeriesType.Date");
        source.Should().Contain("FillSeriesDateUnit.Month");
        source.Should().Contain("StopValue");
    }

    [Fact]
    public void FillSeriesStepDialog_FieldLabelsUseUniqueAccessKeys()
    {
        var labels = new[]
        {
            "_Rows",
            "_Columns",
            "_Linear",
            "_Growth",
            "_Date",
            "_AutoFill",
            "Da_y",
            "_Weekday",
            "_Month",
            "Y_ear",
            "Step _value:",
            "S_top value:"
        };

        labels.Select(GetAccessKey).Should().OnlyHaveUniqueItems();

        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");
        foreach (var label in labels)
            source.Should().Contain(label);
    }

    [Fact]
    public void FillSeriesStepDialog_InputFieldsExposeAutomationNames()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("AutomationProperties.SetName(_stepBox, \"Step value\");");
        source.Should().Contain("AutomationProperties.SetName(_stopBox, \"Stop value\");");
    }

    [Fact]
    public void FillSeriesStepDialogOpenedFromKeyboard_FocusesSelectedSeriesDirection()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_columnsButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_columnsButton);");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void FillSeriesStepDialog_TryCreateResult_RejectsNonFiniteSteps(string input)
    {
        FillSeriesStepDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Contain("numeric");
    }

    [Fact]
    public void FillSeriesStepDialogInvalidStep_ShowsOwnedWarningAndRefocusesInput()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("MessageBox.Show(");
        source.Should().Contain("this,");
        source.Should().Contain("error ?? \"Enter a numeric step value.\"");
        source.Should().Contain("MessageBoxImage.Warning");
        source.Should().Contain("FocusInvalidStepInput();");
        source.Should().Contain("private void FocusInvalidStepInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_stepBox);");
    }

    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_RejectsInvalidNonBlankStopValue()
    {
        FillSeriesStepDialog.TryCreateResult(
                FillSeriesDirection.Columns,
                FillSeriesType.Linear,
                FillSeriesDateUnit.Day,
                "1",
                "not-a-number",
                out _,
                out var error)
            .Should()
            .BeFalse();

        error.Should().Contain("stop");
    }

    [Fact]
    public void FillSeriesStepDialogInvalidStop_ShowsOwnedWarningAndRefocusesStopInput()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("FocusInvalidStopInput();");
        source.Should().Contain("private void FocusInvalidStopInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_stopBox);");
        source.Should().Contain("Enter a numeric stop value or leave it blank.");
    }

    [Fact]
    public void FillSeriesStepDialog_DisablesDateUnitsUntilDateTypeSelected()
    {
        var source = ReadClassSource("FillSeriesStepDialog.cs", "public sealed class FillSeriesStepDialog", "public sealed record __NoNextFillSeriesStepDialog");

        source.Should().Contain("_linearButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("_growthButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("_dateButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("_autoFillButton.Checked += (_, _) => UpdateDateUnitAvailability();");
        source.Should().Contain("private void UpdateDateUnitAvailability()");
        source.Should().Contain("var isDateSeries = _dateButton.IsChecked == true;");
        foreach (var button in new[] { "_dayButton", "_weekdayButton", "_monthButton", "_yearButton" })
            source.Should().Contain($"{button}.IsEnabled = isDateSeries;");
    }

    [Fact]
    public void ZoomDialog_TryCreateResult_AcceptsPercentWithinExcelRange()
    {
        ZoomDialog.TryCreateResult("125", out var result, out _).Should().BeTrue();

        result.Should().Be(new ZoomDialogResult(125));
    }

    [Fact]
    public void ZoomDialog_TryCreateResult_RejectsFractionalCustomPercent()
    {
        ZoomDialog.TryCreateResult("125.5", out _, out var error).Should().BeFalse();

        error.Should().Be("Zoom must be a whole percent between 10% and 400%.");
    }

    [Fact]
    public void ZoomDialog_ExposesExcelPresetPercentsAndCustomPercent()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("ZoomPresets");
        source.Should().Contain("[400, 200, 100, 75, 50, 25]");
        source.Should().Contain("400");
        source.Should().Contain("200");
        source.Should().Contain("100");
        source.Should().Contain("75");
        source.Should().Contain("_fitSelectionButton");
        source.Should().Contain("Fit _selection");
        source.Should().Contain("_customZoomButton");
        source.Should().Contain("_zoomBox");
    }

    [Fact]
    public void ZoomDialog_CustomPercentBoxExposesAutomationName()
    {
        var source = ReadClassSource("ZoomDialog.cs", "public sealed class ZoomDialog", "public sealed record __NoNextZoomDialog");

        source.Should().Contain("AutomationProperties.SetName(_zoomBox, \"Custom zoom percent\");");
    }

    [Fact]
    public void ZoomDialogOpenedFromKeyboard_FocusesPresetOrCustomZoomChoice()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("var checkedPreset = _presetButtons.FirstOrDefault(button => button.IsChecked == true);");
        source.Should().Contain("if (checkedPreset is not null)");
        source.Should().Contain("checkedPreset.Focus();");
        source.Should().Contain("Keyboard.Focus(checkedPreset);");
        source.Should().Contain("else");
        source.Should().Contain("_zoomBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_zoomBox);");
    }

    [Fact]
    public void ZoomDialogOpenedWithCustomPercent_FocusesAndSelectsCustomPercent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ZoomDialog(125);
            try
            {
                dialog.Show();
                PumpDispatcher();

                var customButton = GetField<RadioButton>(dialog, "_customZoomButton");
                var zoomBox = GetField<TextBox>(dialog, "_zoomBox");

                customButton.IsChecked.Should().BeTrue();
                Keyboard.FocusedElement.Should().BeSameAs(zoomBox);
                zoomBox.Text.Should().Be("125");
                zoomBox.SelectionStart.Should().Be(0);
                zoomBox.SelectionLength.Should().Be(zoomBox.Text.Length);
            }
            finally
            {
                dialog.Close();
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void ZoomDialog_InvalidCustomInput_ShowsParserErrorAndRefocusesEntry()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("TryCreateResult(input, out var result, out var error)");
        source.Should().Contain("MessageBox.Show(this, error");
        source.Should().Contain("_customZoomButton.IsChecked = true");
        source.Should().Contain("_zoomBox.Focus();");
        source.Should().Contain("_zoomBox.SelectAll();");
    }

    [Fact]
    public void ZoomDialog_CreateFitSelectionResult_RequestsFitSelectionWithoutChangingPercent()
    {
        ZoomDialog.CreateFitSelectionResult(125)
            .Should()
            .Be(new ZoomDialogResult(125, FitSelection: true));
    }

    [Fact]
    public void PageBreakDialog_CreateClearResult_RepresentsClearAll()
    {
        PageBreakDialog.CreateClearResult().Should().Be(new PageBreakDialogResult(PageBreakDialogAction.Clear, null, null));
    }

    [Fact]
    public void PageBreakDialog_TryCreateResult_ParsesRowAndColumnBreaks()
    {
        PageBreakDialog.TryCreateResult(" row 12 ", out var rowResult).Should().BeTrue();
        PageBreakDialog.TryCreateResult(" column 5 ", out var columnResult).Should().BeTrue();
        PageBreakDialog.TryCreateResult(" column C ", out var letterColumnResult).Should().BeTrue();

        rowResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddRow, 12, null));
        columnResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, 5));
        letterColumnResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, 3));
    }

    [Theory]
    [InlineData("row 0")]
    [InlineData("row 1048577")]
    [InlineData("col 0")]
    [InlineData("col 16385")]
    [InlineData("column 0")]
    [InlineData("column XFE")]
    public void PageBreakDialog_TryCreateResult_RejectsOutOfWorksheetBreakEntries(string input)
    {
        PageBreakDialog.TryCreateResult(input, out _).Should().BeFalse();
    }

    [Fact]
    public void PageBreakDialog_ExposesExplicitExcelStyleActionsInsteadOfCommandText()
    {
        var source = ReadRemainingDialogSources();
        var pageBreakSource = source[source.IndexOf("public sealed class PageBreakDialog", StringComparison.Ordinal)..];

        pageBreakSource.Should().Contain("Insert _row page break");
        pageBreakSource.Should().Contain("Insert _column page break");
        pageBreakSource.Should().Contain("_Reset all page breaks");
        pageBreakSource.Should().Contain("_rowBreakBox");
        pageBreakSource.Should().Contain("_columnBreakBox");
        pageBreakSource.Should().NotContain("ObjectSizeDialog.CreateSingleInputContent(\"Page break:\"");
    }

    [Fact]
    public void PageBreakDialogOpenedFromKeyboard_FocusesSelectedBreakEntry()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rowBreakBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(_columnBreakBox);");
        source.Should().Contain("_resetAllButton.Focus();");
    }

    [Fact]
    public void PageBreakDialog_NumberInputsExposeAutomationNames()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("AutomationProperties.SetName(_rowBreakBox, \"Row page break\");");
        source.Should().Contain("AutomationProperties.SetName(_columnBreakBox, \"Column page break\");");
    }

    [Fact]
    public void PageBreakDialogInvalidBreakEntry_ShowsOwnedWarningAndRefocusesEntry()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("MessageBox.Show(");
        source.Should().Contain("this,");
        source.Should().Contain("Enter a row number within the worksheet for the page break.");
        source.Should().Contain("Enter a column number or letter within the worksheet for the page break.");
        source.Should().Contain("MessageBoxImage.Warning");
        source.Should().Contain("PageLayoutInputParser.IsValidRowBreak(rowBreak)");
        source.Should().Contain("FocusInvalidBreakInput(_rowBreakBox);");
        source.Should().Contain("FocusInvalidBreakInput(_columnBreakBox);");
        source.Should().Contain("private static void FocusInvalidBreakInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void PageBreakDialog_EnablesOnlyTheSelectedBreakEntry()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PageBreakDialog("row 12");

            var rowButton = GetField<RadioButton>(dialog, "_insertRowButton");
            var columnButton = GetField<RadioButton>(dialog, "_insertColumnButton");
            var resetButton = GetField<RadioButton>(dialog, "_resetAllButton");
            var rowBox = GetField<TextBox>(dialog, "_rowBreakBox");
            var columnBox = GetField<TextBox>(dialog, "_columnBreakBox");

            rowButton.IsChecked.Should().BeTrue();
            rowBox.IsEnabled.Should().BeTrue();
            columnBox.IsEnabled.Should().BeFalse();

            columnButton.IsChecked = true;
            rowBox.IsEnabled.Should().BeFalse();
            columnBox.IsEnabled.Should().BeTrue();

            resetButton.IsChecked = true;
            rowBox.IsEnabled.Should().BeFalse();
            columnBox.IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void PageBreakDialog_UpdateBreakInputAvailabilityTracksExcelActionChoice()
    {
        var source = ReadClassSource("PageBreakDialog.cs", "public sealed class PageBreakDialog", "public sealed record __NoNextPageBreakDialog");

        source.Should().Contain("_insertRowButton.Checked += (_, _) => UpdateBreakInputAvailability();");
        source.Should().Contain("_insertColumnButton.Checked += (_, _) => UpdateBreakInputAvailability();");
        source.Should().Contain("_resetAllButton.Checked += (_, _) => UpdateBreakInputAvailability();");
        source.Should().Contain("private void UpdateBreakInputAvailability()");
        source.Should().Contain("_rowBreakBox.IsEnabled = _insertRowButton.IsChecked == true;");
        source.Should().Contain("_columnBreakBox.IsEnabled = _insertColumnButton.IsChecked == true;");
    }

    [Fact]
    public void GoalSeekStatusDialog_CreateMessage_DescribesSolvedAndUnsolvedResults()
    {
        GoalSeekStatusDialog.CreateMessage(new(true, 42.25, 100, 4), targetValue: 100)
            .Should()
            .Contain("Goal Seek found a solution")
            .And.Contain("Target value: 100")
            .And.Contain("Current formula result: 100")
            .And.Contain("Changing cell value: 42.25");

        GoalSeekStatusDialog.CreateMessage(new(false, 11, 98.5, 32), targetValue: 100)
            .Should()
            .Contain("could not find a solution")
            .And.Contain("Target value: 100")
            .And.Contain("Current formula result: 98.5")
            .And.Contain("Changing cell value: 11");
    }

    [Fact]
    public void GoalSeekStatusDialog_ExposesKeyboardAccessKeysForButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoalSeekStatusDialog.cs"));

        source.Should().Contain("Content = \"_Keep Result\"");
        source.Should().Contain("Content = \"_Restore Original Values\"");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("IsCancel = true");
    }

    [Fact]
    public void GoalSeekStatusDialogOpenedFromKeyboard_FocusesDefaultButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoalSeekStatusDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("StatusDialogKeyboardFocus.FocusDefaultButton(this);");
    }

    [Fact]
    public void GoalSeekStatusDialog_ReceivesRequestedTargetValueFromWorkflow()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("new GoalSeekStatusDialog(result, targetValue)");
    }

    [Fact]
    public void StatusDialogs_ExposeClearExcelLikeStatusLabelsAndButtons()
    {
        var source = ReadStatusDialogSources();

        source.Should().Contain("Target value:");
        source.Should().Contain("Current formula result:");
        source.Should().Contain("Changing cell value:");
        source.Should().Contain("Content = \"_Keep Result\"");
        source.Should().Contain("Content = \"_Restore Original Values\"");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }

    [Fact]
    public void WorkbookStatisticsDialog_CreateMessage_UsesWorkbookStatisticsFormatter()
    {
        var message = WorkbookStatisticsDialog.CreateMessage(new(
            WorksheetCount: 2,
            CellCount: 12,
            FormulaCount: 3,
            CommentCount: 1,
            ChartCount: 4,
            PictureCount: 5,
            ShapeCount: 6,
            NamedRangeCount: 7));

        message.Should().Contain("Sheets: 2").And.Contain("Named ranges: 7");
    }

    [Fact]
    public void WorkbookStatisticsDialogOpenedFromKeyboard_FocusesOkButton()
    {
        var source = string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookStatisticsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "StatusDialogKeyboardFocus.cs")));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("StatusDialogKeyboardFocus.FocusDefaultButton(this);");
        source.Should().Contain("private static Button? FindDefaultButton");
        source.Should().Contain("button.Focus();");
        source.Should().Contain("Keyboard.Focus(button);");
    }

    [Fact]
    public void WorkbookStatisticsDialog_UsesSingleExcelLikeOkButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookStatisticsDialog.cs"));

        source.Should().Contain("DialogButtonRowFactory.CreateOkOnly");
        source.Should().NotContain("DialogButtonRowFactory.Create(() => Window.GetWindow(stack)!.DialogResult = true");
    }

    [Fact]
    public void WorkbookStatisticsDialog_StatisticsSummaryExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookStatisticsDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(statisticsBlock, \"Workbook statistics\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(statisticsBlock, \"WorkbookStatisticsSummary\");");
        source.Should().Contain("AutomationProperties.SetHelpText(statisticsBlock, \"Summarizes sheet, cell, formula, comment, and object counts for the workbook.\");");
    }

    [Fact]
    public void AccessibilityCheckerDialog_CreateMessage_ReportsCleanAndIssueStates()
    {
        AccessibilityCheckerDialog.CreateMessage([])
            .Should()
            .Be("No accessibility issues found.");

        AccessibilityCheckerDialog.CreateMessage([
            new(
                AccessibilityIssueKind.ChartMissingTitle,
                SheetId.New(),
                "Sheet1",
                "A1:D8",
                "Chart is missing a title.")
        ]).Should().Contain("Sheet1!A1:D8: Chart is missing a title.");
    }

    [Fact]
    public void AccessibilityCheckerDialogOpenedFromKeyboard_FocusesIssueText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AccessibilityCheckerDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_issueList.Focus();");
        source.Should().Contain("Keyboard.Focus(_issueList);");
    }

    [Fact]
    public void AccessibilityCheckerDialog_UsesIssueListAndGoToAction()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AccessibilityCheckerDialog.cs"));
        var reviewSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ReviewCommands.cs"));

        source.Should().Contain("public sealed record AccessibilityCheckerDialogResult");
        source.Should().Contain("private readonly ListBox _issueList");
        source.Should().Contain("private readonly Button _goToButton");
        source.Should().Contain("Content = \"_Go To\"");
        source.Should().Contain("_issueList.MouseDoubleClick +=");
        source.Should().Contain("private void GoToSelectedIssue()");
        reviewSource.Should().Contain("if (dialog.ShowDialog() == true)");
        reviewSource.Should().Contain("NavigateToCell(AccessibilityCheckerDialog.GetNavigationTarget(dialog.Result!.Issue));");
    }

    [Fact]
    public void AccessibilityCheckerDialog_CleanStateUsesSingleExcelLikeOkButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AccessibilityCheckerDialog.cs"));

        source.Should().Contain("DialogButtonRowFactory.CreateOkOnly");
        source.Should().NotContain("DialogButtonRowFactory.Create(() => Window.GetWindow(stack)!.DialogResult = true");
    }

    [Fact]
    public void AccessibilityCheckerDialog_ResultControlsExposeAutomationNames()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AccessibilityCheckerDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_messageBox, \"Accessibility checker result\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_messageBox, \"AccessibilityCheckerResultText\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_messageBox, \"Summarizes the workbook accessibility check when no issues are found.\");");
        source.Should().Contain("AutomationProperties.SetName(_issueList, \"Accessibility issues\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_issueList, \"AccessibilityCheckerIssueList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_issueList, \"Select an accessibility issue and choose Go To to navigate to its workbook location.\");");
        source.Should().Contain("AutomationProperties.SetName(_goToButton, \"Go to selected accessibility issue\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_goToButton, \"AccessibilityCheckerGoToButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_goToButton, \"Navigate to the selected accessibility issue.\");");
    }

    [Fact]
    public void AccessibilityCheckerDialog_GetNavigationTarget_UsesFirstCellInIssueLocation()
    {
        var sheetId = SheetId.New();

        AccessibilityCheckerDialog.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.ChartMissingTitle,
                sheetId,
                "Sheet1",
                "C3:E8",
                "Chart is missing a title."))
            .Should()
            .Be(new CellAddress(sheetId, 3, 3));

        AccessibilityCheckerDialog.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.DefaultWorksheetName,
                sheetId,
                "Sheet1",
                "Sheet1",
                "Worksheet tab names should describe their contents."))
            .Should()
            .Be(new CellAddress(sheetId, 1, 1));
    }

    [Fact]
    public void StatusDialogs_UseSharedExcelStyleButtonRows()
    {
        var source = ReadStatusDialogSources();

        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().NotContain("InsertChartDialog.CreateButtonRow");
    }

    [Fact]
    public void RemainingNonChartDialogs_UseSharedExcelStyleButtonRows()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("DialogButtonRowFactory.Create(Accept, 72)");
        source.Should().NotContain("InsertChartDialog.CreateButtonRow");
    }

    [Fact]
    public void SingleInputMiniDialogs_UseAccessKeyedLabelsAndSharedButtonRows()
    {
        var remainingSource = ReadRemainingDialogSources();
        var objectSource = ReadObjectDialogSources();

        remainingSource.Should().Contain("Format cells greater _than:");
        remainingSource.Should().Contain("Row _height:");
        remainingSource.Should().Contain("Column _width:");
        remainingSource.Should().Contain("Forecast _periods:");
        remainingSource.Should().Contain("Sheet _name:");
        remainingSource.Should().Contain("AutomationProperties.SetName(_thresholdBox, \"Conditional format threshold\");");
        remainingSource.Should().Contain("AutomationProperties.SetName(_heightBox, \"Row height\");");
        remainingSource.Should().Contain("AutomationProperties.SetName(_widthBox, \"Column width\");");
        remainingSource.Should().Contain("AutomationProperties.SetName(_periodsBox, \"Forecast periods\");");
        remainingSource.Should().Contain("AutomationProperties.SetName(_nameBox, \"Sheet name\");");
        objectSource.Should().Contain("Target = box");
        objectSource.Should().Contain("DialogButtonRowFactory.Create(accept, 72)");
    }

    [Fact]
    public void ForecastSheetDialog_TryCreateResult_RequiresPositivePeriods()
    {
        ForecastSheetDialog.TryCreateResult("0", out _, out var error).Should().BeFalse();

        error.Should().Contain("positive");
    }

    [Fact]
    public void ForecastSheetDialog_TryCreateResult_AcceptsPositivePeriods()
    {
        ForecastSheetDialog.TryCreateResult("12", out var result, out var error).Should().BeTrue(error);

        result.Should().Be(new ForecastSheetDialogResult(12));
    }

    [Fact]
    public void ForecastSheetDialogOpenedFromKeyboard_FocusesPeriodsBox()
    {
        var source = ReadClassSource("ForecastSheetDialog.cs", "public sealed class ForecastSheetDialog", "public sealed record __NoNextForecastSheetDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_periodsBox);");
    }

    [Fact]
    public void ForecastSheetDialog_UsesExcelLikeCreateDefaultAction()
    {
        var source = ReadClassSource("ForecastSheetDialog.cs", "public sealed class ForecastSheetDialog", "public sealed record __NoNextForecastSheetDialog");
        var helperSource = ReadClassSource("ObjectSizingDialogs.cs", "public sealed class ObjectSizeDialog", "public sealed class ObjectRotationDialog");

        source.Should().Contain("ObjectSizeDialog.CreateSingleInputContent(\"Forecast _periods:\", _periodsBox, Accept, acceptContent: \"_Create\")");
        helperSource.Should().Contain("string acceptContent = \"_OK\"");
        helperSource.Should().Contain("DialogButtonRowFactory.Create(accept, 72, acceptContent: acceptContent)");
    }

    [Fact]
    public void ForecastSheetDialogInvalidPeriods_ShowsOwnedWarningAndRefocusesInput()
    {
        var source = ReadClassSource("ForecastSheetDialog.cs", "public sealed class ForecastSheetDialog", "public sealed record __NoNextForecastSheetDialog");

        source.Should().Contain("MessageBox.Show(");
        source.Should().Contain("this,");
        source.Should().Contain("error ?? \"Enter a positive whole number of forecast periods.\"");
        source.Should().Contain("MessageBoxImage.Warning");
        source.Should().Contain("FocusInvalidPeriodsInput();");
        source.Should().Contain("private void FocusInvalidPeriodsInput()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_periodsBox);");
    }

    [Fact]
    public void RowAndColumnSizeDialogsInvalidInput_ShowOwnedWarningsAndRefocusInputs()
    {
        var rowSource = ReadClassSource("RemainingDialogs.cs", "public sealed class RowHeightDialog", "public sealed record ColumnWidthDialogResult");
        var columnSource = ReadClassSource("RemainingDialogs.cs", "public sealed class ColumnWidthDialog", "public sealed record SheetNameDialogResult");

        rowSource.Should().Contain("MessageBox.Show(");
        rowSource.Should().Contain("this,");
        rowSource.Should().Contain("error ?? \"Enter a row height from 0 to 409.\"");
        rowSource.Should().Contain("MessageBoxImage.Warning");
        rowSource.Should().Contain("FocusInvalidHeightInput();");
        rowSource.Should().Contain("private void FocusInvalidHeightInput()");

        columnSource.Should().Contain("MessageBox.Show(");
        columnSource.Should().Contain("this,");
        columnSource.Should().Contain("error ?? \"Enter a column width from 0 to 255.\"");
        columnSource.Should().Contain("MessageBoxImage.Warning");
        columnSource.Should().Contain("FocusInvalidWidthInput();");
        columnSource.Should().Contain("private void FocusInvalidWidthInput()");
    }

    [Fact]
    public void SparklineDialog_CreateResult_TrimsRangeAndLocation()
    {
        SparklineDialogPlanner.CreateResult(" A1:E1 ", " F1 ", SparklineKindChoice.Column)
            .Should()
            .Be(new SparklineDialogResult("A1:E1", "F1", SparklineKindChoice.Column));
    }

    [Fact]
    public void SparklineDialog_CreateRangeSelectionRequest_TrimsCurrentTextAndRequestsCollapse()
    {
        SparklineDialogPlanner.CreateRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, " A1:E1 ")
            .Should()
            .Be(new SparklineRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, "A1:E1", CollapseDialog: true));
    }

    [Fact]
    public void SparklineDialog_RangePickerButtonsTriggerWorksheetSelectionIntent()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<SparklineRangeSelectionRequest>();
            var dialog = new SparklineDialog("A1:E1", "F1", SparklineKindChoice.Line, requests.Add);

            GetField<Button>(dialog, "_dataRangePickerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            GetField<Button>(dialog, "_locationPickerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            requests.Should().Equal(
                new SparklineRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, "A1:E1", CollapseDialog: true),
                new SparklineRangeSelectionRequest(SparklineRangeSelectionTarget.Location, "F1", CollapseDialog: true));
            dialog.RangeSelectionRequest.Should().Be(requests[^1]);
        });
    }

    [Fact]
    public void SparklineDialogApplyRangeSelection_UpdatesRequestedInput()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SparklineDialog("A1:E1", "F1", SparklineKindChoice.Line);
            try
            {
                dialog.ApplyRangeSelection(SparklineRangeSelectionTarget.DataRange, "Sheet2!A1:D6");
                dialog.ApplyRangeSelection(SparklineRangeSelectionTarget.Location, "K5");

                GetField<TextBox>(dialog, "_dataRangeBox").Text.Should().Be("Sheet2!A1:D6");
                GetField<TextBox>(dialog, "_locationBox").Text.Should().Be("K5");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void MainWindow_WiresSparklineRangePickersToCurrentSelection()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.InsertCommands.cs"));

        source.Should().Contain("new SparklineDialog(");
        source.Should().Contain("request => ApplySparklineRangeSelection(dialog, request)");
        source.Should().Contain("private void ApplySparklineRangeSelection(");
        source.Should().Contain("SparklineRangeSelectionRequest request");
        source.Should().Contain("request.Target == SparklineRangeSelectionTarget.Location");
        source.Should().Contain("FormatCellReference(selectedRange.Start)");
        source.Should().Contain("FormatWorkbookRange(selectedRange)");
        source.Should().Contain("dialog.ApplyRangeSelection(request.Target, rangeText);");
    }

    [Fact]
    public void SparklineDialog_ExposesRangePickerButtonsForDataAndLocation()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("_dataRangePickerButton");
        source.Should().Contain("_locationPickerButton");
        source.Should().Contain("Select Data Range");
        source.Should().Contain("Select Location Range");
        source.Should().Contain("RequestRangeSelection");
    }

    [Fact]
    public void SparklineDialog_LabelsEditableControlsWithAccessKeyTargets()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("new Label { Content = \"_Data range:\", Target = _dataRangeBox");
        source.Should().Contain("new Label { Content = \"_Location:\", Target = _locationBox");
        source.Should().Contain("new Label { Content = \"Sparkline _type:\", Target = _kindBox");
    }

    [Fact]
    public void SparklineDialog_RangeEditorsExposeAutomationNames()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("AutomationProperties.SetName(_dataRangeBox, \"Sparkline data range\");");
        source.Should().Contain("AutomationProperties.SetName(_locationBox, \"Sparkline location\");");
    }

    [Fact]
    public void SparklineDialog_UsesExcelWinLossLabel()
    {
        SparklineDialogPlanner.GetKindLabel(SparklineKindChoice.Line).Should().Be("Line");
        SparklineDialogPlanner.GetKindLabel(SparklineKindChoice.Column).Should().Be("Column");
        SparklineDialogPlanner.GetKindLabel(SparklineKindChoice.WinLoss).Should().Be("Win/Loss");

        var source = ReadRemainingDialogSources();
        source.Should().Contain("GetKindLabel(choice)");
        source.Should().Contain("Tag = choice");
    }

    [Fact]
    public void SparklineDialogOpenedFromKeyboard_FocusesDataRangeBox()
    {
        var source = ReadClassSource("SparklineDialog.cs", "public sealed class SparklineDialog", "private void RequestRangeSelection");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FocusRangeSelectionInput(_dataRangeBox);");
    }

    [Fact]
    public void SparklineDialogRangePicker_RefocusesSelectedInputWithKeyboardFocus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SparklineDialog.cs"));
        var handlerSource = source[source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..];

        handlerSource.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest);");
        handlerSource.Should().Contain("FocusRangeSelectionInput(textBox);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void SparklineDialogInvalidRanges_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SparklineDialog.cs"));
        var plannerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SparklineDialogPlanner.cs"));
        var insertSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.InsertCommands.cs"));

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("SparklineDialogPlanner.ValidateInputs(_dataRangeBox.Text, _locationBox.Text, _sheetId)");
        plannerSource.Should().Contain("SparklineInputParser.TryParseDataRange(dataRangeText, sheetId, out _)");
        plannerSource.Should().Contain("SparklineInputParser.TryParseLocation(locationText, sheetId, out _)");
        source.Should().Contain("ShowInvalidInputWarning(\"Invalid data range.\", _dataRangeBox)");
        source.Should().Contain("ShowInvalidInputWarning(\"Invalid location cell.\", _locationBox)");
        source.Should().Contain("MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning)");
        source.Should().Contain("FocusRangeSelectionInput(textBox);");
        insertSource.Should().Contain("_currentSheetId,");
    }

    [Fact]
    public void SparklineDialogPlanner_ValidatesInputsWithParser()
    {
        var sheetId = SheetId.New();

        SparklineDialogPlanner.ValidateInputs("A1:E1", "F1", sheetId)
            .Should().Be(SparklineDialogValidationResult.Valid);
        SparklineDialogPlanner.ValidateInputs("A1:E1", "F1:G1", sheetId)
            .Should().Be(SparklineDialogValidationResult.InvalidLocation);
        SparklineDialogPlanner.ValidateInputs("A1", "F1", sheetId)
            .Should().Be(SparklineDialogValidationResult.InvalidDataRange);
    }

    [Fact]
    public void SheetNameDialog_CreateResult_TrimsSheetName()
    {
        SheetNameDialog.CreateResult("  Report  ").Should().Be(new SheetNameDialogResult("Report"));
    }

    [Theory]
    [InlineData("", "Sheet name is invalid: it cannot be blank.")]
    [InlineData("   ", "Sheet name is invalid: it cannot be blank.")]
    [InlineData("This sheet name is far too long for Excel", "Sheet name is invalid: it cannot exceed 31 characters.")]
    [InlineData("Bad/Name", "Sheet name is invalid: it cannot contain : \\ / ? * [ or ].")]
    [InlineData("'Report", "Sheet name is invalid: it cannot begin or end with an apostrophe.")]
    [InlineData("Report'", "Sheet name is invalid: it cannot begin or end with an apostrophe.")]
    public void SheetNameDialog_TryCreateResult_RejectsInvalidExcelSheetNames(string input, string expectedError)
    {
        SheetNameDialog.TryCreateResult(input, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expectedError);
    }

    [Fact]
    public void SheetNameDialog_TryCreateResult_AcceptsTrimmedValidSheetName()
    {
        SheetNameDialog.TryCreateResult("  Report  ", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new SheetNameDialogResult("Report"));
    }

    [Fact]
    public void SheetNameDialog_AcceptWarnsAndRefocusesInvalidName()
    {
        var source = ReadClassSource("SheetNameDialog.cs", "public sealed class SheetNameDialog", "public sealed record __NoNextSheetNameDialog");

        source.Should().Contain("Content = ObjectSizeDialog.CreateSingleInputContent(\"Sheet _name:\", _nameBox, Accept);");
        source.Should().Contain("if (!TryCreateResult(_nameBox.Text, out var result, out var error))");
        source.Should().Contain("MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);");
        source.Should().Contain("DialogFocus.FocusAndSelect(_nameBox);");
    }

    [Fact]
    public void SheetNameDialogOpenedFromKeyboard_FocusesNameBox()
    {
        var source = ReadClassSource("SheetNameDialog.cs", "public sealed class SheetNameDialog", "public sealed record __NoNextSheetNameDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_nameBox);");
    }

    [Fact]
    public void UnhideSheetDialog_CreateResult_CapturesSelectedSheetName()
    {
        UnhideSheetDialog.CreateResult("  Hidden Sheet  ").Should().Be(new UnhideSheetDialogResult("Hidden Sheet"));
    }

    [Fact]
    public void UnhideSheetDialog_LabelsSheetPickerWithAccessKeyTarget()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("new Label { Content = \"_Unhide sheet:\", Target = _sheetBox");
    }

    [Fact]
    public void UnhideSheetDialog_SheetListExposesAutomationName()
    {
        var source = ReadClassSource("UnhideSheetDialog.cs", "public sealed class UnhideSheetDialog", "public sealed record __NoNextUnhideSheetDialog");

        source.Should().Contain("AutomationProperties.SetName(_sheetBox, \"Unhide sheet\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_sheetBox, \"UnhideSheetList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_sheetBox, \"Select the hidden worksheet to make visible.\");");
    }

    [Fact]
    public void UnhideSheetDialog_UsesNonEditableSelectionList()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("private readonly ListBox _sheetBox");
        source.Should().Contain("_sheetBox.SelectedItem");
        source.Should().NotContain("_sheetBox.IsEditable = true");
        source.Should().NotContain("_sheetBox.Text");
    }

    [Fact]
    public void UnhideSheetDialogOpenedFromKeyboard_FocusesSheetList()
    {
        var source = ReadClassSource("UnhideSheetDialog.cs", "public sealed class UnhideSheetDialog", "public sealed record __NoNextUnhideSheetDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_sheetBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_sheetBox);");
    }

    [Fact]
    public void UnhideSheetDialog_OkButtonTracksSelectedSheet()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new UnhideSheetDialog(["Hidden 1", "Hidden 2"]);
            var sheetBox = GetField<ListBox>(dialog, "_sheetBox");
            var okButton = GetField<Button>(dialog, "_okButton");

            okButton.IsDefault.Should().BeTrue();
            okButton.IsEnabled.Should().BeTrue();

            sheetBox.SelectedItem = null;
            okButton.IsEnabled.Should().BeFalse();

            sheetBox.SelectedItem = "Hidden 2";
            okButton.IsEnabled.Should().BeTrue();
        });
    }

    [Fact]
    public void UnhideSheetDialog_ActionButtonsExposeAutomationMetadata()
    {
        var source = ReadClassSource("UnhideSheetDialog.cs", "public sealed class UnhideSheetDialog", "public sealed record __NoNextUnhideSheetDialog");

        source.Should().Contain("private readonly Button _okButton");
        source.Should().Contain("private readonly Button _cancelButton");
        source.Should().Contain("AutomationProperties.SetAutomationId(_okButton, \"UnhideSheetOkButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_okButton, \"Unhide the selected worksheet.\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_cancelButton, \"UnhideSheetCancelButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_cancelButton, \"Close the Unhide Sheet dialog without changing worksheet visibility.\");");
    }

    [Fact]
    public void UnhideSheetDialogSheetList_DoubleClickAcceptsSelectedSheet()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new UnhideSheetDialog(["Hidden 1", "Hidden 2"]);
            var sheetBox = GetField<ListBox>(dialog, "_sheetBox");
            dialog.Dispatcher.BeginInvoke(() =>
            {
                sheetBox.SelectedItem = "Hidden 2";
                sheetBox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                });

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new UnhideSheetDialogResult("Hidden 2"));
        });
    }

    [Fact]
    public void SpellCheckDialog_CreateReplaceResult_CapturesReplacement()
    {
        SpellCheckDialog.CreateReplaceResult("mispelled", "misspelled")
            .Should()
            .Be(new SpellCheckDialogResult(SpellCheckDialogAction.Replace, "misspelled"));
    }

    [Fact]
    public void SpellCheckDialog_CreateReplaceAllResult_CapturesReplacement()
    {
        SpellCheckDialog.CreateReplaceAllResult("mispelled", "misspelled")
            .Should()
            .Be(new SpellCheckDialogResult(SpellCheckDialogAction.ReplaceAll, "misspelled"));
    }

    [Fact]
    public void SpellCheckDialog_CreateIgnoreAllResult_UsesDistinctAction()
    {
        SpellCheckDialog.CreateIgnoreAllResult()
            .Should()
            .Be(new SpellCheckDialogResult(SpellCheckDialogAction.IgnoreAll, null));
    }

    [Fact]
    public void SpellCheckDialog_UsesExcelLikeNotInDictionarySuggestionsAndChangeToLayout()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("private readonly TextBox _notInDictionaryBox");
        source.Should().Contain("private readonly ListBox _suggestionsBox");
        source.Should().Contain("Not in _Dictionary:");
        source.Should().Contain("_Suggestions:");
        source.Should().Contain("_suggestionsBox.Items.Add(suggestion)");
        source.Should().Contain("_suggestionsBox.SelectionChanged");
        source.Should().Contain("new Label { Content = \"_Change to:\", Target = _replacementBox");
        source.Should().Contain("Grid.SetColumn(actionButtons");
    }

    [Fact]
    public void SpellCheckDialog_FieldControlsExposeAutomationNames()
    {
        var source = ReadClassSource("SpellCheckDialog.cs", "public sealed class SpellCheckDialog", "public sealed class __NoNextSpellCheckDialog");

        source.Should().Contain("AutomationProperties.SetName(_notInDictionaryBox, \"Not in Dictionary\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_notInDictionaryBox, \"SpellCheckNotInDictionaryBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_notInDictionaryBox, \"Shows the word that was not found in the dictionary.\");");
        source.Should().Contain("AutomationProperties.SetName(_suggestionsBox, \"Suggestions\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_suggestionsBox, \"SpellCheckSuggestionsList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_suggestionsBox, \"Choose a suggested spelling replacement.\");");
        source.Should().Contain("AutomationProperties.SetName(_replacementBox, \"Change to\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_replacementBox, \"SpellCheckReplacementBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_replacementBox, \"Enter the replacement text for the misspelled word.\");");
    }

    [Fact]
    public void SpellCheckDialog_ExposesExcelLikeIgnoreChangeAndAddActions()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("SpellCheckDialogAction.Add");
        source.Should().Contain("Content = \"Ignore _Once\"");
        source.Should().Contain("Content = \"_Change\"");
        source.Should().Contain("Content = \"_Change\", Width = 90, IsDefault = true");
        source.Should().Contain("Content = \"Add to _Dictionary\"");
        source.Should().Contain("CreateIgnoreAllResult");
        source.Should().Contain("CreateReplaceAllResult(word, _replacementBox.Text)");
        source.Should().Contain("CreateAddResult");
        source.Should().Contain("RefreshChangeButtonState");
    }

    [Fact]
    public void SpellCheckDialog_DisablesChangeActionsUntilReplacementTextExists()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SpellCheckDialog("mispelled", "");
            dialog.Show();
            try
            {
                var replacementBox = GetField<TextBox>(dialog, "_replacementBox");
                var changeButton = GetField<Button>(dialog, "_changeButton");
                var changeAllButton = GetField<Button>(dialog, "_changeAllButton");

                changeButton.IsEnabled.Should().BeFalse();
                changeAllButton.IsEnabled.Should().BeFalse();

                replacementBox.Text = "misspelled";

                changeButton.IsEnabled.Should().BeTrue();
                changeAllButton.IsEnabled.Should().BeTrue();

                replacementBox.Text = " ";

                changeButton.IsEnabled.Should().BeFalse();
                changeAllButton.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SpellCheckDialog_ActionButtonsUseUniqueExcelLikeAccessKeys()
    {
        var labels = new[]
        {
            "Ignore _Once",
            "Ignore _All",
            "_Change",
            "Change A_ll",
            "Add to _Dictionary",
            "Ca_ncel"
        };

        labels.Select(GetAccessKey).Should().OnlyHaveUniqueItems();

        var source = ReadClassSource("SpellCheckDialog.cs", "public sealed class SpellCheckDialog", "public sealed class __NoNextSpellCheckDialog");
        foreach (var label in labels)
        {
            source.Should().Contain($"Content = \"{label}\"");
        }

        source.Should().Contain("AutomationProperties.SetAutomationId(button, automationId);");
        source.Should().Contain("AutomationProperties.SetHelpText(button, helpText);");
        source.Should().Contain("SpellCheckIgnoreOnceButton");
        source.Should().Contain("SpellCheckIgnoreAllButton");
        source.Should().Contain("SpellCheckChangeButton");
        source.Should().Contain("SpellCheckChangeAllButton");
        source.Should().Contain("SpellCheckAddToDictionaryButton");
        source.Should().Contain("SpellCheckCancelButton");
    }

    [Fact]
    public void SpellCheckDialogOpenedFromKeyboard_FocusesSuggestionListOrReplacementBox()
    {
        var source = ReadClassSource("SpellCheckDialog.cs", "public sealed class SpellCheckDialog", "public sealed class __NoNextSpellCheckDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_suggestionsBox.Items.Count > 0");
        source.Should().Contain("_suggestionsBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_suggestionsBox);");
        source.Should().Contain("_replacementBox.Focus();");
        source.Should().Contain("_replacementBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_replacementBox);");
    }

    [Fact]
    public void SpellCheckDialogSuggestionsList_DoubleClickAcceptsSelectedSuggestion()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SpellCheckDialog("mispelled", "misspelled");
            var suggestionsBox = GetField<ListBox>(dialog, "_suggestionsBox");

            dialog.Dispatcher.BeginInvoke(() =>
            {
                suggestionsBox.SelectedItem = "misspelled";
                suggestionsBox.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                });

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new SpellCheckDialogResult(SpellCheckDialogAction.Replace, "misspelled"));
        });
    }

    [Fact]
    public void ExportOptionsDialog_ExposesOnlyHonoredPdfXpsChoices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ExportOptionsDialog.cs"));

        source.Should().Contain("Content = \"_Workbook\"");
        source.Should().Contain("Content = \"Active _sheet(s)\"");
        source.Should().Contain("Content = \"Selected _range\"");
        source.Should().Contain("PDF/XPS options");
        source.Should().Contain("Content = \"_Include document properties\"");
        source.Should().Contain("Content = \"_Open after publishing\"");
        source.Should().Contain("Content = \"_Ignore print areas\"");
        source.Should().NotContain("CSV options");
        source.Should().NotContain("Content = \"CSV _delimiter:\"");
    }

    [Fact]
    public void PrintPreviewDialog_ExposesExcelLikePreviewToolbarAffordances()
    {
        var source = ReadPrintPreviewDialogSources();

        source.Should().Contain("Content = \"_Previous Page\"");
        source.Should().Contain("Content = \"_Next Page\"");
        source.Should().Contain("Content = \"_First Page\"");
        source.Should().Contain("Content = \"_Last Page\"");
        source.Should().Contain("NavigationCommands.FirstPage");
        source.Should().Contain("NavigationCommands.LastPage");
        source.Should().Contain("Content = \"_Zoom:\"");
        source.Should().Contain("Content = \"_Margins\"");
        source.Should().Contain("Content = \"Page _Setup...\"");
        source.Should().Contain("Content = \"_Print...\"");
        source.Should().Contain("Content = \"_Close Preview\"");
        source.Should().Contain("IsCancel = true");
        source.Should().Contain("closeButton.Click += (_, _) => Close();");
    }

    private static char GetAccessKey(string label)
    {
        var marker = label.IndexOf('_', StringComparison.Ordinal);
        marker.Should().BeGreaterThanOrEqualTo(0);
        marker.Should().BeLessThan(label.Length - 1);
        return char.ToUpperInvariant(label[marker + 1]);
    }

    private static string ReadRemainingDialogSources()
    {
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemainingDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PageBreakDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ForecastSheetDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SheetNameDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "UnhideSheetDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FillSeriesStepDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ZoomDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SparklineDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SpellCheckDialog.cs")));
    }

    private static string ReadStatusDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoalSeekStatusDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookStatisticsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AccessibilityCheckerDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "StatusDialogKeyboardFocus.cs")));

    private static string ReadPrintPreviewDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.Layout.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.Helpers.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewSettingsPanelFactory.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewToolbarPlanner.cs")));

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", fileName));
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        if (end < 0)
            end = source.Length;
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string ReadObjectDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HyperlinkDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextEntryDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ThreadedCommentDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ObjectSizingDialogs.cs")));
    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
