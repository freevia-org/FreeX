using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Excel's Paste Options button: after Ctrl+V the "(Ctrl)" button appears at the pasted range, and
/// choosing Values from it replaces the paste with values only.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class PasteOptionsButtonTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(100, 50, 40, 500, 400, 100, 50)]
    [InlineData(490, 50, 40, 500, 400, 460, 50)]
    [InlineData(100, 395, 40, 500, 400, 100, 380)]
    [InlineData(100, 395, 40, 0, 0, 100, 395)]
    [InlineData(10, 10, 600, 500, 400, 0, 10)]
    public void ClampPasteOptionsButtonPosition_KeepsButtonInsideOverlayLikeWpf(
        double left,
        double top,
        double buttonWidth,
        double overlayWidth,
        double overlayHeight,
        double expectedLeft,
        double expectedTop)
    {
        var (clampedLeft, clampedTop) = MainWindow.ClampPasteOptionsButtonPosition(
            left, top, buttonWidth, overlayWidth, overlayHeight);

        clampedLeft.Should().Be(expectedLeft);
        clampedTop.Should().Be(expectedTop);
    }

    [Fact]
    public async Task Paste_ShowsTheButton_AndValuesReplacesThePasteWithValues()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            Layout(window);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var c3 = new CellAddress(sheet.Id, 3, 3);
                window.Session.SelectCell(a1);
                window.Session.CommitCellText("=1+1").Success.Should().BeTrue();
                window.Session.SelectCell(a1);
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.C, RawInputModifiers.Control, PhysicalKey.C);
                await DrainAsync();

                window.Session.SelectCell(c3);
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.V, RawInputModifiers.Control, PhysicalKey.V);
                await DrainAsync();

                sheet.GetCell(c3)!.FormulaText.Should().NotBeNullOrEmpty("sanity: Ctrl+V pasted the formula");
                window.PasteOptionsButtonForTest.Should().NotBeNull("the Paste Options button follows a paste");

                window.ApplyPasteOption("Paste Values", new GridRange(c3, c3));
                await DrainAsync();

                sheet.GetCell(c3)!.FormulaText.Should().BeNullOrEmpty("Values replaces the pasted formula");
                sheet.GetValue(c3).Should().Be(new NumberValue(2));
                window.Session.CanUndo.Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

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

    private static async Task DrainAsync()
    {
        for (var i = 0; i < 6; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
