using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Excel's collapsed ribbon ("Show tabs only"): Ctrl+F1 hides the ribbon commands; clicking a tab
/// shows them for one use and clicking back in the sheet hides them again. Real routed input.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class RibbonCollapseTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CtrlF1_CollapsesTheRibbon_AndATabClickShowsItUntilTheSheetIsClicked()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            Layout(window);
            try
            {
                var tabControl = window.RibbonControlForTest.Should().BeOfType<TabControl>().Subject;
                var expandedHeight = tabControl.Bounds.Height;
                SelectedBody(tabControl).IsVisible.Should().BeTrue();
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                Press(window, Key.F1, RawInputModifiers.Control, PhysicalKey.F1);
                await DrainInputAsync();
                Layout(window);

                window.RibbonDisplayState!.IsCollapsed.Should().BeTrue("Ctrl+F1 collapses the ribbon to its tabs");
                SelectedBody(tabControl).IsVisible.Should().BeFalse();
                tabControl.Bounds.Height.Should().BeLessThan(expandedHeight - 40, "only the tab strip is left");

                var homeHeader = (TabItem)tabControl.SelectedItem!;
                (homeHeader.Tag as string).Should().NotBe("FileTab");
                var headerCenter = homeHeader.TranslatePoint(
                    new Point(homeHeader.Bounds.Width / 2, homeHeader.Bounds.Height / 2), window)!.Value;
                window.MouseDown(headerCenter, MouseButton.Left);
                window.MouseUp(headerCenter, MouseButton.Left);
                await DrainInputAsync();
                Layout(window);

                window.RibbonDisplayState.IsTemporarilyShown.Should().BeTrue("a tab click shows the collapsed ribbon");
                SelectedBody(tabControl).IsVisible.Should().BeTrue();

                var sheetPoint = new Point(window.Bounds.Width / 2, window.Bounds.Height - 120);
                window.MouseDown(sheetPoint, MouseButton.Left);
                window.MouseUp(sheetPoint, MouseButton.Left);
                await DrainInputAsync();

                window.RibbonDisplayState.IsTemporarilyShown.Should().BeFalse("clicking the sheet hides it again");
                SelectedBody(tabControl).IsVisible.Should().BeFalse();

                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.F1, RawInputModifiers.Control, PhysicalKey.F1);
                await DrainInputAsync();

                window.RibbonDisplayState.IsCollapsed.Should().BeFalse("Ctrl+F1 again pins the ribbon");
                SelectedBody(tabControl).IsVisible.Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Control SelectedBody(TabControl tabControl) =>
        (Control)((TabItem)tabControl.SelectedItem!).Content!;

    private static void Layout(Window window)
    {
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
    }

    private static void Press(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static async Task DrainInputAsync()
    {
        for (var i = 0; i < 4; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
