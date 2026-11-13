using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Excel: after Ctrl+C, while the copied range is still outlined, pressing Enter on the destination
/// pastes there and ends copy mode. Real routed key input through the headless window.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class EnterPastesCopiedRangeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Enter_WithACopiedRangeOutlined_PastesAndEndsCopyMode()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            try
            {
                var sheet = window.Session.ActiveSheet;
                var source = new CellAddress(sheet.Id, 1, 1);
                var target = new CellAddress(sheet.Id, 3, 2);
                sheet.SetCell(source, new NumberValue(42));
                window.Session.SelectCell(source);
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                Press(window, Key.C, RawInputModifiers.Control, PhysicalKey.C);
                await DrainInputAsync();
                window.ClipboardMarqueeRangeForTest.Should().NotBeNull("sanity: Ctrl+C outlines the copied cell");

                window.Session.SelectCell(target);
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetValue(target).Should().Be(new NumberValue(42), "Enter pastes the copied cell");
                window.ClipboardMarqueeRangeForTest.Should().BeNull("pasting with Enter ends copy mode, as in Excel");
                window.Session.ActiveCell.Should().Be(target, "Enter pastes in place instead of moving the selection");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
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
