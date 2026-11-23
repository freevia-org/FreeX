using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Tests;

public sealed class ZoomViewportTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(DocumentViewMode.WebLayout)]
    [InlineData(DocumentViewMode.Draft)]
    public async Task ReflowViewsFollowViewportWidthAndPrintLayoutRestoresHorizontalScrolling(DocumentViewMode mode)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]) { Width = 1040, Height = 720 };
            try
            {
                window.Show();
                window.Editor.ViewMode = mode;
                window.UpdateLayout();
                var scroller = window.WorkspaceScrollerForTests!;
                scroller.Measure(new Size(700, 500));
                scroller.Arrange(new Rect(0, 0, 700, 500));
                var narrowWidth = window.Editor.Bounds.Width;
                scroller.Measure(new Size(1000, 500));
                scroller.Arrange(new Rect(0, 0, 1000, 500));
                window.Editor.Bounds.Width.Should().BeGreaterThan(narrowWidth,
                    "Web and Draft views reflow to the available viewport width");

                window.Editor.ViewMode = DocumentViewMode.PrintLayout;
                window.ApplyZoomForTests(2);
                window.UpdateLayout();
                scroller.Offset = new Vector(100, 0);
                window.UpdateLayout();
                scroller.Offset.X.Should().BeGreaterThan(0);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZoomedPageCanScrollHorizontallyIncludingAfterLeavingSplitView(bool leaveSplit)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]) { Width = 1040, Height = 720 };
            try
            {
                window.Show();
                if (leaveSplit)
                {
                    window.ToggleSplit();
                    window.ToggleSplit();
                }
                window.ApplyZoomForTests(2);
                window.UpdateLayout();

                var scroller = window.WorkspaceScrollerForTests!;
                scroller.Viewport.Width.Should().BeGreaterThan(0);
                scroller.Extent.Width.Should().BeGreaterThan(scroller.Viewport.Width,
                    "a 200% print-layout page extends beyond this narrow window");
                scroller.Offset = new Vector(200, scroller.Offset.Y);
                window.UpdateLayout();
                scroller.Offset.X.Should().BeGreaterThan(0,
                    "the right side of a zoomed page must remain reachable");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
