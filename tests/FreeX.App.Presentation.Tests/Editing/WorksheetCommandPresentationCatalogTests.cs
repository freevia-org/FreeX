using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class WorksheetCommandPresentationCatalogTests
{
    [Theory]
    [InlineData(FillCellsDirection.Down, "Fill Down", "Filled down", "Filled down in A1:A3")]
    [InlineData(FillCellsDirection.Right, "Fill Right", "Filled right", "Filled right in A1:C1")]
    [InlineData(FillCellsDirection.Up, "Fill Up", "Filled up", "Filled up in A1:A3")]
    [InlineData(FillCellsDirection.Left, "Fill Left", "Filled left", "Filled left in A1:C1")]
    public void DescribeFill_OwnsCommandAndStatusWording(
        FillCellsDirection direction,
        string commandTitle,
        string completedAction,
        string status)
    {
        var range = direction is FillCellsDirection.Down or FillCellsDirection.Up ? "A1:A3" : "A1:C1";
        var presentation = WorksheetCommandPresentationCatalog.DescribeFill(direction);

        presentation.CommandTitle.Should().Be(commandTitle);
        presentation.CompletedAction.Should().Be(completedAction);
        WorksheetCommandPresentationCatalog.FormatFillStatus(direction, range).Should().Be(status);
        WorksheetCommandPresentationCatalog.FormatFillFailure(direction).Should().Be($"{completedAction} failed.");
    }

    [Fact]
    public void AlignmentStatus_PreservesRendererOutput()
    {
        WorksheetCommandPresentationCatalog.FormatHorizontalAlignmentStatus("B2:C4", HorizontalAlignment.Center)
            .Should().Be("Aligned B2:C4 center");
        WorksheetCommandPresentationCatalog.FormatVerticalAlignmentStatus("B2:C4", VerticalAlignment.Center)
            .Should().Be("Aligned B2:C4 middle");
    }
}
