using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
    [Fact]
    public void SetPageMarginsCommand_SetsMarginsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageMargins = new WorksheetPageMargins(1, 1, 1, 1);
        var narrow = new WorksheetPageMargins(0.5, 0.5, 0.5, 0.5);

        var command = new SetPageMarginsCommand(sheet.Id, narrow);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageMargins.Should().Be(narrow);

        command.Revert(ctx);

        sheet.PageMargins.Should().Be(new WorksheetPageMargins(1, 1, 1, 1));
    }

    [Fact]
    public void WorksheetPageLayout_GetPageSizeInches_AppliesLandscapeOrientation()
    {
        var size = WorksheetPageLayout.GetPageSizeInches(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Landscape);

        size.Width.Should().Be(11.0);
        size.Height.Should().Be(8.5);
    }

    [Fact]
    public void WorksheetPageLayout_GetMarginGuideFractions_ConvertsMarginsToPageFractions()
    {
        var guide = WorksheetPageLayout.GetMarginGuideFractions(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            new WorksheetPageMargins(1.0, 0.5, 2.0, 1.0));

        guide.Left.Should().BeApproximately(1.0 / 8.5, 0.0001);
        guide.Right.Should().BeApproximately(1.0 - (0.5 / 8.5), 0.0001);
        guide.Top.Should().BeApproximately(2.0 / 11.0, 0.0001);
        guide.Bottom.Should().BeApproximately(1.0 - (1.0 / 11.0), 0.0001);
    }

    [Fact]
    public void WorksheetPageLayout_GetMarginsFromGuideFraction_ConvertsDraggedGuidesToMargins()
    {
        var margins = new WorksheetPageMargins(1, 1, 1, 1);

        var left = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Left,
            2.0 / 8.5);
        var right = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Right,
            7.0 / 8.5);
        var top = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Top,
            1.5 / 11.0);
        var bottom = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Bottom,
            9.5 / 11.0);

        left.Left.Should().BeApproximately(2.0, 0.0001);
        right.Right.Should().BeApproximately(1.5, 0.0001);
        top.Top.Should().BeApproximately(1.5, 0.0001);
        bottom.Bottom.Should().BeApproximately(1.5, 0.0001);
    }

    [Fact]
    public void PageMarginInputParser_ParsesFourCommaSeparatedInchValues()
    {
        PageMarginInputParser.TryParse("0.7, 0.8, 0.9, 1.1", out var margins, out var error)
            .Should().BeTrue();

        margins.Should().Be(new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1));
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("0.5,0.5,0.5")]
    [InlineData("0.5,-0.5,0.5,0.5")]
    [InlineData("0.5,nope,0.5,0.5")]
    public void PageMarginInputParser_RejectsInvalidCustomMarginInput(string input)
    {
        PageMarginInputParser.TryParse(input, out _, out var error).Should().BeFalse();

        error.Should().NotBeNullOrWhiteSpace();
    }
}
