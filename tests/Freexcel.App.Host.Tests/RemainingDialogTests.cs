using FluentAssertions;

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

    [Fact]
    public void ColumnWidthDialog_TryCreateResult_AcceptsPositiveWidth()
    {
        ColumnWidthDialog.TryCreateResult("8.5", out var result, out _).Should().BeTrue();

        result.Should().Be(new ColumnWidthDialogResult(8.5));
    }

    [Fact]
    public void FillSeriesStepDialog_TryCreateResult_AcceptsNegativeStep()
    {
        FillSeriesStepDialog.TryCreateResult("-2", out var result, out _).Should().BeTrue();

        result.Should().Be(new FillSeriesStepDialogResult(-2));
    }

    [Fact]
    public void ZoomDialog_TryCreateResult_AcceptsPercentWithinExcelRange()
    {
        ZoomDialog.TryCreateResult("125", out var result, out _).Should().BeTrue();

        result.Should().Be(new ZoomDialogResult(125));
    }

    [Fact]
    public void PageBreakDialog_CreateClearResult_RepresentsClearAll()
    {
        PageBreakDialog.CreateClearResult().Should().Be(new PageBreakDialogResult(PageBreakDialogAction.Clear, null, null));
    }

    [Fact]
    public void ForecastSheetDialog_TryCreateResult_RequiresPositivePeriods()
    {
        ForecastSheetDialog.TryCreateResult("0", out _, out var error).Should().BeFalse();

        error.Should().Contain("positive");
    }

    [Fact]
    public void SparklineDialog_CreateResult_TrimsRangeAndLocation()
    {
        SparklineDialog.CreateResult(" A1:E1 ", " F1 ", SparklineKindChoice.Column)
            .Should()
            .Be(new SparklineDialogResult("A1:E1", "F1", SparklineKindChoice.Column));
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
}
