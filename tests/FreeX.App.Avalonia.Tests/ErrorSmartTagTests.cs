using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Excel's error smart tag: a cell holding a number stored as text shows the green indicator and,
/// once active, the warning button whose Convert to Number makes it a real number.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class ErrorSmartTagTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task NumberStoredAsText_ShowsTheSmartTag_AndConvertToNumberFixesIt()
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
                var b1 = new CellAddress(sheet.Id, 1, 2);
                sheet.SetCell(a1, new TextValue("123"));
                window.Session.SelectCell(b1);
                window.RefreshShellForTest();
                await DrainAsync();
                window.ErrorSmartTagButtonForTest.Should().BeNull("the active cell has no error");

                window.Session.SelectCell(a1);
                window.RefreshShellForTest();
                await DrainAsync();

                window.GetErrorIndicatorCode(a1).Should().NotBeNull();
                window.ErrorSmartTagButtonForTest.Should().NotBeNull("the active cell shows the error smart tag");

                window.ExecuteErrorSmartTagAction(ErrorSmartTagAction.ConvertToNumber);
                await DrainAsync();

                sheet.GetValue(a1).Should().Be(new NumberValue(123));
                window.ErrorSmartTagButtonForTest.Should().BeNull("the fixed cell no longer shows the smart tag");
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

    private static async Task DrainAsync()
    {
        for (var i = 0; i < 4; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
