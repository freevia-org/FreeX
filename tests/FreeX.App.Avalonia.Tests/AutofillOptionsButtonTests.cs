using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Threading;

using FluentAssertions;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Excel's Auto Fill Options button follows a fill-handle drag and redoes it another way.</summary>
[Collection("AvaloniaHeadless")]
public sealed class AutofillOptionsButtonTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FillDrag_ShowsTheButton_AndFillSeriesRedoesTheDrag()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var sheet = window.Session.ActiveSheet;
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
                window.RefreshShellForTest();
                window.AutofillOptionsButtonForTest.Should().BeNull();

                var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
                var fill = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));
                window.Session.AutofillDragRange(source, fill).Success.Should().BeTrue();
                window.RefreshShellForTest();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                window.AutofillOptionsButtonForTest.Should().NotBeNull("the Auto Fill Options button follows a fill");
                window.ApplyAutofillOption(AutofillMode.FillSeries);
                sheet.GetValue(3, 1).Should().Be(new NumberValue(7));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
