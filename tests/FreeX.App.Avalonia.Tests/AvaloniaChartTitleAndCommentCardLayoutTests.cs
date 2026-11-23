using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.VisualTree;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pins two layout defects found by rendering the Avalonia host offscreen: a chart title dragged onto
/// the plot area vanished behind the plot background and series, and an existing threaded-comment
/// card (conversation, reply editor, resolve toggle, thread actions, Save/Cancel) spilled below its
/// fixed-height card background with its bottom buttons unreachable.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartTitleAndCommentCardLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ChartTitle_isDrawnAfterThePlotBackgroundAndSeries_soADraggedTitleStaysVisible()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                Title = "Regional sales",
                ShowLegend = false,
                TitleLayout = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.2, Y = 0.5 },
            };
            var request = new ChartLayoutRequest
            {
                Chart = chart,
                Categories = ["A", "B", "C", "D"],
                Series = [new ChartSeriesData { SeriesIndex = 0, Name = "S0", Values = [1, 2, 3, 4] }],
                PlotArea = new PlotRect(0, 0, 300, 200),
                TextMeasurer = new AvaloniaTextMeasurer(),
            };

            var canvas = new AvaloniaChartRenderer(chart, WorkbookTheme.Office)
                .Render(ChartLayoutEngine.Layout(request), 300, 200);

            var title = canvas.Children.OfType<TextBlock>().Single(t => t.Text == "Regional sales");
            var titleIndex = canvas.Children.IndexOf(title);
            var lastShapeIndex = canvas.Children.Select((child, index) => (child, index))
                .Where(x => x.child is Rectangle or Polyline or Line)
                .Max(x => x.index);
            titleIndex.Should().BeGreaterThan(lastShapeIndex,
                "the title must paint on top of the plot background, gridlines and series");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThreadedCommentCard_withReplies_isTallEnoughForItsContent()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Width = 1200;
            window.Height = 900;
            var sheet = window.Session.Workbook.AddSheet("CardOverflowFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.ThreadedComments[address] = new ThreadedComment("Root text", "Alex")
            {
                Replies = [new CommentReply("A reply", "Sam")],
            };
            window.Session.SelectCell(address);
            window.Show();
            window.RebuildSheetGridForTest();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var editor = window.GetVisualDescendants().OfType<Border>()
                .First(b => AutomationProperties.GetAutomationId(b) == "WorksheetThreadedCommentInlineEditor");
            var content = editor.Child.Should().NotBeNull().And.BeOfType<StackPanel>().Subject;
            content.Measure(new global::Avalonia.Size(editor.Width - editor.Padding.Left - editor.Padding.Right, double.PositiveInfinity));

            var available = editor.MaxHeight - editor.Padding.Top - editor.Padding.Bottom - editor.BorderThickness.Top - editor.BorderThickness.Bottom;
            content.DesiredSize.Height.Should().BeLessThanOrEqualTo(available,
                "the card's bottom controls (reply editor, resolve toggle, thread actions, Save/Cancel) must fit " +
                "inside the card instead of spilling below its background");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }
}
