using Avalonia.Headless;
using System.Threading;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Functional parity coverage for recalculation after an AutoFilter mutation. WPF explicitly
/// recalculates after filter and sort commands because SUBTOTAL/AGGREGATE formulas depend on row
/// visibility, which is not a normal cell dependency mutation.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AutoFilterRecalculationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task FilterApply_RecalculatesSubtotalIgnoringHiddenRows() =>
        Session.Dispatch(() =>
        {
            var fixture = CreateFixture();
            fixture.Window.RunAutoFilterForTest(fixture.Range, 0, ["North"]);

            fixture.Sheet.GetValue(fixture.TotalAddress).Should().Be(new NumberValue(10));
            fixture.Window.AllowCloseWithoutDirtyPromptForParityCapture();
            fixture.Window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task FilterChange_RecalculatesSubtotalForNewVisibleRows() =>
        Session.Dispatch(() =>
        {
            var fixture = CreateFixture();
            fixture.Window.RunAutoFilterForTest(fixture.Range, 0, ["North"]);
            fixture.Sheet.GetValue(fixture.TotalAddress).Should().Be(new NumberValue(10));

            fixture.Window.RunAutoFilterForTest(fixture.Range, 0, ["South"]);

            fixture.Sheet.GetValue(fixture.TotalAddress).Should().Be(new NumberValue(20));
            fixture.Window.AllowCloseWithoutDirtyPromptForParityCapture();
            fixture.Window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task FilterClear_RecalculatesSubtotalAfterRowsBecomeVisible() =>
        Session.Dispatch(() =>
        {
            var fixture = CreateFixture();
            fixture.Window.RunAutoFilterForTest(fixture.Range, 0, ["North"]);
            fixture.Sheet.GetValue(fixture.TotalAddress).Should().Be(new NumberValue(10));

            fixture.Window.RunAutoFilterForTest(fixture.Range, 0, []);

            fixture.Sheet.GetValue(fixture.TotalAddress).Should().Be(new NumberValue(30));
            fixture.Window.AllowCloseWithoutDirtyPromptForParityCapture();
            fixture.Window.Close();
        }, CancellationToken.None);

    private static FilterFixture CreateFixture()
    {
        var window = new MainWindow([]);
        var sheet = window.Session.Workbook.AddSheet("FilterRecalc");
        window.Session.SelectSheet(sheet.Id);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        window.Session.SelectRange(range);
        var toggle = window.Session.ToggleSelectedRangeAutoFilter();
        toggle.Success.Should().BeTrue(toggle.ErrorMessage);

        var totalAddress = new CellAddress(sheet.Id, 4, 2);
        sheet.SetFormula(totalAddress, "SUBTOTAL(109,B2:B3)");
        window.Session.RecalculateWorkbook();
        sheet.GetValue(totalAddress).Should().Be(new NumberValue(30));

        return new FilterFixture(window, sheet, range, totalAddress);
    }

    private sealed record FilterFixture(
        MainWindow Window,
        Sheet Sheet,
        GridRange Range,
        CellAddress TotalAddress);
}
