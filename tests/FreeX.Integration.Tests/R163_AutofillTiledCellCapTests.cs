using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// r163 remediation, shared-large-document-limits-F1. The fill handle is the FOURTH path that
/// materialises one entry per destination cell with no ceiling, after the internal-clipboard paste,
/// the external-clipboard paste (the original finding) and Paste Link (the first remediation).
/// <see cref="AutofillCommand"/> sizes five separate per-cell snapshot lists from the fill range's
/// cell count, so selecting a whole row and dragging -- or double-clicking -- the fill handle to the
/// last row asks for ~17.18 billion entries on the synchronous UI thread, which OOMs or hangs with
/// no warning.
///
/// The remediation's own scope audit found this one by searching for the OPERATION (materialise one
/// entry per destination position) rather than for callers of the capped helpers, which is the only
/// search that could have found it: nothing in this file referenced the cap.
/// </summary>
public class R163_AutofillTiledCellCapTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("R163AutofillCap");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void Autofill_JustOverCapFillRange_IsRejectedInsteadOfAllocatingMillionsOfSnapshots()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        // 2,001 x 2,001 = 4,004,001 cells, one over the 4,000,000 limit -- large enough to exercise
        // the pre-fix allocation path, small enough not to risk the OOM the real gesture causes.
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2001));
        var fill = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2002, 2001));

        var outcome = new AutofillCommand(sheet.Id, source, fill).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("too large");
    }

    [Fact]
    public void Autofill_AnOrdinaryDragStillFills()
    {
        // Sibling/no-regression: the cap must not disturb a normal fill. This is the gesture users
        // actually make, and it must keep working exactly as before.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var fill = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 5, 1));

        var outcome = new AutofillCommand(sheet.Id, source, fill).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetCell(new CellAddress(sheet.Id, 5, 1))!.Value.Should().Be(new NumberValue(5));
    }
}
