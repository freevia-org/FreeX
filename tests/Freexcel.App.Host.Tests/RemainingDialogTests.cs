using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;
using System.IO;

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemainingDialogs.cs"));

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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemainingDialogs.cs"));

        source.Should().Contain("ZoomPresets");
        source.Should().Contain("200");
        source.Should().Contain("100");
        source.Should().Contain("75");
        source.Should().Contain("_customZoomButton");
        source.Should().Contain("_zoomBox");
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
    public void GoalSeekStatusDialog_CreateMessage_DescribesSolvedAndUnsolvedResults()
    {
        GoalSeekStatusDialog.CreateMessage(new(true, 42.25, 100, 4))
            .Should()
            .Contain("Goal Seek found a solution")
            .And.Contain("42.25");

        GoalSeekStatusDialog.CreateMessage(new(false, 11, 98.5, 32))
            .Should()
            .Contain("could not find a solution")
            .And.Contain("98.5");
    }

    [Fact]
    public void GoalSeekStatusDialog_ExposesKeyboardAccessKeysForButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "StatusDialogs.cs"));

        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemainingDialogs.cs"));

        source.Should().Contain("_dataRangePickerButton");
        source.Should().Contain("_locationPickerButton");
        source.Should().Contain("Select Data Range");
        source.Should().Contain("Select Location Range");
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
    public void SpellCheckDialog_CreateReplaceResult_CapturesReplacement()
    {
        SpellCheckDialog.CreateReplaceResult("mispelled", "misspelled")
            .Should()
            .Be(new SpellCheckDialogResult(SpellCheckDialogAction.Replace, "misspelled"));
    }

    [Fact]
    public void SpellCheckDialog_ExposesExcelLikeIgnoreChangeAndAddActions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemainingDialogs.cs"));

        source.Should().Contain("SpellCheckDialogAction.Add");
        source.Should().Contain("Content = \"_Ignore\"");
        source.Should().Contain("Content = \"_Change\"");
        source.Should().Contain("Content = \"_Add\"");
        source.Should().Contain("CreateAddResult");
    }
}
