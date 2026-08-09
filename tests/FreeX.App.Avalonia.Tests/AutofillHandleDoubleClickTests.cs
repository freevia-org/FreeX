using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for round-62 finding R62-render-fill-handle-6-1: the Avalonia shell had no
/// double-click-fill-handle-to-extend gesture at all -- <c>TryBeginAutofillDrag</c> never
/// inspected <c>args.ClickCount</c>, so both PointerPressed events of a double-click were treated
/// as ordinary (zero-movement) autofill drags that silently no-op. The fix routes a ClickCount
/// &gt;= 2 press to <c>CommitAutofillHandleDoubleClick</c>, which mirrors the WPF host's
/// <c>OnAutofillHandleDoubleClicked</c>: fill straight down to match the populated extent of the
/// nearest non-blank adjacent column, matching real Excel.
///
/// These drive the real production commit path via the internal test seam
/// <c>RaiseAutofillHandleDoubleClickForTest</c> (mirroring the pre-existing
/// <c>RaiseAutofillDragForTest</c> seam), so the sheet state after the call reflects the actual
/// runtime behavior rather than a source-string proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AutofillHandleDoubleClickTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task DoubleClick_FillsDownToAdjacentColumnPopulatedExtent()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            // The default new-window workbook is the seeded port-preview demo (has content like
            // "Windows" at B1) -- run the scenario on a fresh, guaranteed-empty sheet instead.
            var sheet = window.Session.Workbook.AddSheet("DoubleClickFillFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Jan"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Feb"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Mar"));
            sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Apr"));
            sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("May"));

            var source = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));
            window.Session.SelectCell(source.Start);

            window.RaiseAutofillHandleDoubleClickForTest(source);

            // A lone plain-number source cell's fill-handle default is a verbatim copy (Excel only
            // switches to an incrementing series when Ctrl is held), so A2:A5 should all read 1,
            // matching the B column's populated extent (B1:B5) rather than staying blank.
            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(1),
                "double-clicking the fill handle must extend down to match the adjacent column's extent");
            sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new NumberValue(1));
            sheet.GetValue(new CellAddress(sheet.Id, 4, 1)).Should().Be(new NumberValue(1));
            sheet.GetValue(new CellAddress(sheet.Id, 5, 1)).Should().Be(new NumberValue(1));
            sheet.GetValue(new CellAddress(sheet.Id, 6, 1)).Should().Be(BlankValue.Instance,
                "the fill must stop at the adjacent column's last populated row, not run past it");

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
                "the completed selection must cover the source plus the newly filled cells");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DoubleClick_NoAdjacentPopulatedData_IsNoOp()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("DoubleClickFillNoAdjFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
            var source = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1));
            window.Session.SelectCell(source.Start);

            window.RaiseAutofillHandleDoubleClickForTest(source);

            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(BlankValue.Instance,
                "with no populated adjacent column, the double-click gesture must not fill anything");
            window.Session.SelectedRange.Should().Be(source,
                "the selection must stay on the source cell when there is nothing to fill");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }
}
