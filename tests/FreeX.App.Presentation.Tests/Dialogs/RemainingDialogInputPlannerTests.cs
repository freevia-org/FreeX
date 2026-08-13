using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class RemainingDialogInputPlannerTests
{
    [Fact]
    public void ConditionalFormatThreshold_CreateResult_TrimsThresholdText()
    {
        ConditionalFormatThresholdDialogPlanner.CreateResult("  100  ")
            .Should()
            .Be(new ConditionalFormatThresholdDialogResult("100"));
    }

    [Fact]
    public void ConditionalFormatThreshold_TryCreateResult_RejectsBlankThreshold()
    {
        ConditionalFormatThresholdDialogPlanner.TryCreateResult(" ", out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ConditionalFormatThreshold_TryCreateResult_AcceptsTrimmedThreshold()
    {
        ConditionalFormatThresholdDialogPlanner.TryCreateResult("  100  ", out var result)
            .Should()
            .BeTrue();

        result.Should().Be(new ConditionalFormatThresholdDialogResult("100"));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("409.6")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void WorksheetDimension_TryCreateRowHeightResult_RejectsInvalidHeights(string input)
    {
        WorksheetDimensionDialogPlanner.TryCreateRowHeightResult(input, out var result).Should().BeFalse();

        result.Should().Be(new RowHeightDialogResult(WorksheetDimensionDialogPlanner.DefaultRowHeight));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("409", 409)]
    [InlineData("409.5", 409.5)]
    public void WorksheetDimension_TryCreateRowHeightResult_AcceptsExcelRowHeightBounds(string input, double expected)
    {
        WorksheetDimensionDialogPlanner.TryCreateRowHeightResult(input, out var result).Should().BeTrue();

        result.Should().Be(new RowHeightDialogResult(expected));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("255.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void WorksheetDimension_TryCreateColumnWidthResult_RejectsInvalidWidths(string input)
    {
        WorksheetDimensionDialogPlanner.TryCreateColumnWidthResult(input, out var result).Should().BeFalse();

        result.Should().Be(new ColumnWidthDialogResult(WorksheetDimensionDialogPlanner.DefaultColumnWidth));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("8.5", 8.5)]
    [InlineData("255", 255)]
    public void WorksheetDimension_TryCreateColumnWidthResult_AcceptsExcelColumnWidthBounds(string input, double expected)
    {
        WorksheetDimensionDialogPlanner.TryCreateColumnWidthResult(input, out var result).Should().BeTrue();

        result.Should().Be(new ColumnWidthDialogResult(expected));
    }

    [Theory]
    [InlineData(WorksheetDimensionKind.RowHeight, "409.5", 409.5)]
    [InlineData(WorksheetDimensionKind.ColumnWidth, "255", 255)]
    public void WorksheetDimension_TryCreateResult_ProvidesRendererNeutralValue(
        WorksheetDimensionKind kind,
        string input,
        double expected)
    {
        WorksheetDimensionDialogPlanner.TryCreateResult(kind, input, out var result).Should().BeTrue();

        result.Should().Be(new WorksheetDimensionDialogResult(expected));
    }

    [Fact]
    public void AvaloniaDimensionDialog_DelegatesValidationToPortablePlanner()
    {
        var avaloniaRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Avalonia");
        var source = File.ReadAllText(Path.Combine(avaloniaRoot, "MainWindow.RowColumnVisibility.cs"));

        source.Should().Contain("WorksheetDimensionDialogPlanner.TryCreateResult(");
        source.Should().Contain("WorksheetDimensionKind.RowHeight");
        source.Should().Contain("WorksheetDimensionKind.ColumnWidth");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("Math.Clamp(");
    }
}
