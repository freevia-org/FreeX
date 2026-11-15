using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using FluentAssertions;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class PrintPreviewDisplayedCommentsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrintPreviewPaintsOnlyPinnedCommentsOnPrintedCells(bool blackAndWhite)
    {
        await Session.Dispatch(() =>
        {
            var workbook = new Workbook("Pinned comments");
            var sheet = workbook.AddSheet("Sheet1");
            var shownNote = new CellAddress(sheet.Id, 1, 1);
            var shownThread = new CellAddress(sheet.Id, 2, 1);
            var unpinned = new CellAddress(sheet.Id, 3, 1);
            var outside = new CellAddress(sheet.Id, 4, 1);
            sheet.SetCell(shownNote, new TextValue("Sheet cell"));
            sheet.PrintArea = new GridRange(shownNote, unpinned);
            sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
            sheet.PrintBlackAndWhite = blackAndWhite;
            sheet.Comments[shownNote] = "Pinned note";
            sheet.Comments[shownThread] = "Legacy placeholder";
            sheet.ThreadedComments[shownThread] = new ThreadedComment("Review", "Ana")
            {
                Replies = [new CommentReply("Done", "Bo")],
            };
            sheet.Comments[unpinned] = "Unpinned note";
            sheet.Comments[outside] = "Outside print area";
            sheet.ShownComments.UnionWith([shownNote, shownThread, outside]);

            var canvas = BuildCanvas(workbook, sheet);

            var colors = canvas.Children.OfType<Polygon>()
                .Select(polygon => ((ISolidColorBrush)polygon.Fill!).Color);
            colors.Should().Equal(
                blackAndWhite ? Colors.Black : Color.FromRgb(192, 0, 0),
                blackAndWhite ? Colors.Black : Color.FromRgb(124, 55, 158));
            canvas.Children.OfType<Rectangle>()
                .Count(rectangle => rectangle.Stroke is ISolidColorBrush &&
                    rectangle.Fill is ISolidColorBrush fill &&
                    fill.Color == (blackAndWhite ? Colors.White : Color.FromRgb(255, 255, 225)))
                .Should().Be(2);

            string.Join(" ", canvas.Children.OfType<TextBlock>().Select(block => block.Text))
                .Should().Contain("Pinned note").And.Contain("Ana: Review").And.Contain("Bo: Done")
                .And.NotContain("Legacy placeholder").And.NotContain("Unpinned note")
                .And.NotContain("Outside print area");

            sheet.PrintDraftQuality = true;
            var draftCanvas = BuildCanvas(workbook, sheet);
            draftCanvas.Children.OfType<Polygon>().Should().BeEmpty();
            draftCanvas.Children.OfType<TextBlock>()
                .Should().NotContain(block => block.Text == "Pinned note");
        }, CancellationToken.None);
    }

    private static Canvas BuildCanvas(Workbook workbook, Sheet sheet)
    {
        PrintPreviewPaginationContext.TryCreate(workbook, sheet, new AvaloniaTextMeasurer(), out var context)
            .Should().BeTrue();
        var page = MainWindow.BuildPreviewPageView(context, pageIndex: 0).Should().BeOfType<Border>().Subject;
        return page.Child.Should().BeOfType<Canvas>().Subject;
    }
}
