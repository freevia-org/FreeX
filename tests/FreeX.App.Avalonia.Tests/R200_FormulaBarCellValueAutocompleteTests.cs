using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class R200_FormulaBarCellValueAutocompleteTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormulaBar_PlainTextPrefix_AutoCompletesFromSameColumn()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("AutocompleteFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 0, 0), new TextValue("Apple"));

            var address = new CellAddress(sheet.Id, 1, 0);
            window.Session.SelectCell(address);
            window.BeginFormulaEditForTest(address, "");
            window.SimulateFormulaBoxTypedTextForTest("app", 3);

            window.FormulaBoxTextForTest.Should().Be("Apple");
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }
}
