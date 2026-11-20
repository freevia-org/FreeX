using System.Reflection;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaPageLayoutRulerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PageLayoutRuler_TogglesAndStaysOutOfPageBreakPreview()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindow();
            try
            {
                Invoke(window, "SetPageLayoutView");
                var pageLayout = Find<Canvas>(window, "PageLayoutOverlay");
                pageLayout.Should().NotBeNull();
                pageLayout!.Children.OfType<TextBlock>().Should().BeEmpty();
                var ruler = Find<Canvas>(window, "PageLayoutMarginRuler");
                ruler.Should().NotBeNull();
                ruler!.Children.OfType<Line>().Should().HaveCount(4);
                ruler.Children.OfType<Rectangle>().Should().HaveCount(4);

                Invoke(window, "ToggleShowRulers");
                window.Session.IsShowingRulers.Should().BeFalse();
                Find<Canvas>(window, "PageLayoutOverlay").Should().NotBeNull();
                Find<Canvas>(window, "PageLayoutMarginRuler").Should().BeNull();

                Invoke(window, "ToggleShowRulers");
                Find<Canvas>(window, "PageLayoutMarginRuler").Should().NotBeNull();
                Invoke(window, "TogglePageBreakPreview");
                Find<Canvas>(window, "PageBreakPreviewOverlay").Should().NotBeNull();
                Find<Canvas>(window, "PageLayoutMarginRuler").Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DraggingTheLeftMarginHandle_PreviewsThenCommitsAnUndoableMarginEdit()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateWindow();
            try
            {
                Invoke(window, "SetPageLayoutView");
                await Dispatcher.UIThread.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Background);
                var sheet = window.Session.ActiveSheet;
                var margins = sheet.PageMargins;
                var ruler = Find<Canvas>(window, "PageLayoutMarginRuler")!;
                var handle = Find<Rectangle>(window, "PageLayoutMarginLeft")!;
                handle.Bounds.Width.Should().BeGreaterThan(0);
                var start = handle.TranslatePoint(
                    new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
                var end = new Point(start.X + 24, start.Y);
                var originalLeft = Canvas.GetLeft(handle);

                window.MouseMove(start, RawInputModifiers.None);
                window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseMove(end, RawInputModifiers.LeftMouseButton);

                var previewHandle = ruler.Children.OfType<Rectangle>().Single(
                    child => AutomationProperties.GetAutomationId(child) == "PageLayoutMarginLeft");
                Canvas.GetLeft(previewHandle).Should().BeGreaterThan(originalLeft);
                sheet.PageMargins.Should().Be(margins);

                window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);

                sheet.PageMargins.Left.Should().BeGreaterThan(margins.Left);
                (sheet.PageMargins with { Left = margins.Left }).Should().Be(margins);
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                sheet.PageMargins.Should().Be(margins);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow([]);
        var sheet = window.Session.ActiveSheet;
        sheet.SetPrintAreas([new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 14, 7))]);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        return window;
    }

    private static void Invoke(MainWindow window, string method) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);

    private static T? Find<T>(MainWindow window, string automationId) where T : Control =>
        window.GetVisualDescendants().OfType<T>().SingleOrDefault(
            control => AutomationProperties.GetAutomationId(control) == automationId);
}
