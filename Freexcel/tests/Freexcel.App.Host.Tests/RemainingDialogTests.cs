using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;
using System.IO;
using System.Reflection;

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
    public void RowHeightDialog_TryCreateResult_RejectsNonPositiveHeights()
    {
        RowHeightDialog.TryCreateResult("0", out _, out var error).Should().BeFalse();

        error.Should().Contain("positive");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void RowHeightDialog_TryCreateResult_RejectsNonFiniteHeights(string input)
    {
        RowHeightDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Contain("positive");
    }

    [Fact]
    public void ColumnWidthDialog_TryCreateResult_AcceptsPositiveWidth()
    {
        ColumnWidthDialog.TryCreateResult("8.5", out var result, out _).Should().BeTrue();

        result.Should().Be(new ColumnWidthDialogResult(8.5));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ColumnWidthDialog_TryCreateResult_RejectsNonFiniteWidths(string input)
    {
        ColumnWidthDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Contain("positive");
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

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void FillSeriesStepDialog_TryCreateResult_RejectsNonFiniteSteps(string input)
    {
        FillSeriesStepDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Contain("numeric");
    }

    [Fact]
    public void ZoomDialog_TryCreateResult_AcceptsPercentWithinExcelRange()
    {
        ZoomDialog.TryCreateResult("125", out var result, out _).Should().BeTrue();

        result.Should().Be(new ZoomDialogResult(125));
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

        rowResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddRow, 12, null));
        columnResult.Should().Be(new PageBreakDialogResult(PageBreakDialogAction.AddColumn, null, 5));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "StatusDialogs.cs"));

        source.Should().Contain("Content = \"_Keep Result\"");
        source.Should().Contain("Content = \"_Restore Original Values\"");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "StatusDialogs.cs"));

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
    public void StatusDialogs_UseSharedExcelStyleButtonRows()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "StatusDialogs.cs"));

        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().NotContain("InsertChartDialog.CreateButtonRow");
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
    public void SparklineDialog_CreateResult_TrimsRangeAndLocation()
    {
        SparklineDialog.CreateResult(" A1:E1 ", " F1 ", SparklineKindChoice.Column)
            .Should()
            .Be(new SparklineDialogResult("A1:E1", "F1", SparklineKindChoice.Column));
    }

    [Fact]
    public void SparklineDialog_ExposesRangePickerButtonsForDataAndLocation()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("_dataRangePickerButton");
        source.Should().Contain("_locationPickerButton");
        source.Should().Contain("Select Data Range");
        source.Should().Contain("Select Location Range");
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
    public void SparklineDialog_UsesExcelWinLossLabel()
    {
        SparklineDialog.GetKindLabel(SparklineKindChoice.Line).Should().Be("Line");
        SparklineDialog.GetKindLabel(SparklineKindChoice.Column).Should().Be("Column");
        SparklineDialog.GetKindLabel(SparklineKindChoice.WinLoss).Should().Be("Win/Loss");

        var source = ReadRemainingDialogSources();
        source.Should().Contain("GetKindLabel(choice)");
        source.Should().Contain("Tag = choice");
    }

    [Fact]
    public void SheetNameDialog_CreateResult_TrimsSheetName()
    {
        SheetNameDialog.CreateResult("  Report  ").Should().Be(new SheetNameDialogResult("Report"));
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

        source.Should().Contain("new Label { Content = \"_Sheet:\", Target = _sheetBox");
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
    public void SpellCheckDialog_ExposesExcelLikeIgnoreChangeAndAddActions()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("SpellCheckDialogAction.Add");
        source.Should().Contain("Content = \"_Ignore\"");
        source.Should().Contain("Content = \"_Change\"");
        source.Should().Contain("Content = \"_Add\"");
        source.Should().Contain("CreateIgnoreAllResult");
        source.Should().Contain("CreateReplaceAllResult(word, _replacementBox.Text)");
        source.Should().Contain("CreateAddResult");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

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
        source.Should().Contain("closeButton.Click += (_, _) => Close();");
    }

    private static string ReadRemainingDialogSources()
    {
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemainingDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FillSeriesStepDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ZoomDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SparklineDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SpellCheckDialog.cs")));
    }
    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }}
