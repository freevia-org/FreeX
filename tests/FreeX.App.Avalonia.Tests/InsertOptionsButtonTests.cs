using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Threading;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Excel's Insert Options button follows an insert of rows and re-formats the new band.</summary>
[Collection("AvaloniaHeadless")]
public sealed class InsertOptionsButtonTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task InsertedRows_ShowTheButton_AndClearFormattingClearsTheBand()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                var sheet = window.Session.ActiveSheet;
                var bold = window.Session.Workbook.RegisterStyle(new CellStyle { Bold = true });
                sheet.SetStyleOnly(1, 10, bold);
                window.RefreshShellForTest();
                window.InsertOptionsButtonForTest.Should().BeNull();

                window.Session.SelectCell(new CellAddress(sheet.Id, 2, 1));
                window.Session.ExecuteCommandPreservingSelection(new InsertRowsCommand(sheet.Id, 2, 1)).Success.Should().BeTrue();
                window.RefreshShellForTest();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                window.InsertOptionsButtonForTest.Should().NotBeNull("the Insert Options button follows an insert");
                window.ApplyInsertOption(new InsertedBand(sheet.Id, true, 2, 1), InsertedBandFormat.SameAsBefore);
                sheet.GetStyleOnly(2, 10).Should().Be(bold, "Format Same As Above copies row 1");

                window.ApplyInsertOption(new InsertedBand(sheet.Id, true, 2, 1), InsertedBandFormat.Clear);

                sheet.GetStyleOnly(2, 10).Should().BeNull();
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
