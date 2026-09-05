using System.Text;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r405: FreeX's workbook commands must undo exactly -- the contract r387/r388 pinned for FreeW and
/// r389 for FreeP, applied to the third app.
///
/// <para><c>IWorkbookCommand</c> has 243 implementations, each with Apply and Revert. A revert that
/// restores most of the state leaves a workbook the user believes they undid, and in a spreadsheet
/// that is easy to miss: the damage may be one cell's style or a formula three screens away.</para>
///
/// <para>Comparison is over the MODEL rather than a written file. r389 learned the hard way that
/// hashing a saved package is timing-sensitive -- ZIP entry stamps move with the wall clock, in
/// FreeX's writer as in Office's -- and reported a broken undo that was only the clock. Reading the
/// model avoids that entirely and pins what the user actually keeps.</para>
///
/// <para>Each case asserts the workbook ACTUALLY CHANGED between apply and revert. Without that, a
/// command whose Apply silently did nothing would satisfy the undo assertion trivially; that guard
/// caught two bad fixtures of mine in FreeW and one in FreeP.</para>
/// </summary>
public sealed class R405_WorkbookCommandUndoRestoresTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("undo-probe");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= 6; row++)
        {
            for (uint col = 1; col <= 4; col++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, col),
                    row % 2 == 0
                        ? new NumberValue(row * 10 + col)
                        : new TextValue($"r{row}c{col}"));
            }
        }

        // Non-cell state that the insert/delete commands have to shift and then restore. Without
        // it the snapshot below would be describing a workbook that has none of the state most at
        // risk from a row or column edit.
        sheet.RowHeights[3] = 42.5;
        sheet.ColumnWidths[3] = 17.25;
        sheet.AddMergedRegion(GridRange.Parse("C5:D5", sheet.Id));

        return (workbook, sheet, new TestCommandContext(workbook));
    }

    /// <summary>
    /// A readable projection of everything a command in this set can touch: values, formulas and
    /// style ids across every sheet, plus sheet identity and order.
    /// </summary>
    private static string Snapshot(Workbook workbook)
    {
        var builder = new StringBuilder();
        foreach (var sheet in workbook.Sheets)
        {
            builder.Append("sheet:").Append(sheet.Name).Append('|').Append(sheet.Id).AppendLine();

            // Row heights, column widths and merges are exactly what the insert/delete commands
            // shift, so a snapshot of cell contents alone would let a revert lose them unnoticed --
            // the instrument has to reach the state the command under test actually touches.
            foreach (var (row, height) in sheet.RowHeights.OrderBy(pair => pair.Key))
                builder.Append("  rowH ").Append(row).Append('=').Append(height).AppendLine();

            foreach (var (col, width) in sheet.ColumnWidths.OrderBy(pair => pair.Key))
                builder.Append("  colW ").Append(col).Append('=').Append(width).AppendLine();

            foreach (var region in sheet.MergedRegions.OrderBy(r => r.ToString(), StringComparer.Ordinal))
                builder.Append("  merge ").Append(region).AppendLine();

            foreach (var (address, cell) in sheet.EnumerateCells().OrderBy(pair => pair.Address.Row).ThenBy(pair => pair.Address.Col))
            {
                builder
                    .Append("  ").Append(address.Row).Append(',').Append(address.Col)
                    .Append(" = ").Append(cell.Value?.ToString() ?? "<null>")
                    .Append(" f=").Append(cell.FormulaText ?? "-")
                    .Append(" s=").Append(cell.StyleId)
                    .AppendLine();
            }
        }

        return builder.ToString();
    }

    private static void Check(string label, Func<Sheet, IWorkbookCommand> factory)
    {
        var (workbook, sheet, context) = Setup();
        var before = Snapshot(workbook);

        var command = factory(sheet);
        command.Apply(context);

        Snapshot(workbook).Should().NotBe(before,
            "{0} must actually change the workbook, or the undo assertion below proves nothing", label);

        command.Revert(context);

        Snapshot(workbook).Should().Be(before, "{0}: undo must restore the workbook exactly", label);
    }

    /// <summary>
    /// The snapshot is the whole instrument, so it must be shown to contain the state it claims to
    /// compare. Widening a projection that silently omits a field produces a confident green over
    /// exactly the state a row or column edit is most likely to lose -- this suite has hit that
    /// failure in its own probes more than once.
    /// </summary>
    [Fact]
    public void TheSnapshotActuallyRecordsTheStateItClaimsTo()
    {
        var (workbook, _, _) = Setup();
        var snapshot = Snapshot(workbook);

        snapshot.Should().Contain("rowH 3=42.5", "row heights must be part of the comparison");
        snapshot.Should().Contain("colW 3=17.25", "column widths must be part of the comparison");
        snapshot.Should().Contain("merge ", "merged regions must be part of the comparison");
        snapshot.Should().Contain("1,1 = ", "cell values must be part of the comparison");
    }

    [Fact]
    public void EveryCoveredCommandUndoesExactly()
    {
        Check("ClearContents", sheet => new ClearContentsCommand(
            sheet.Id, GridRange.Parse("A1:B2", sheet.Id)));

        Check("InsertRows", sheet => new InsertRowsCommand(sheet.Id, 2, 1));

        Check("DeleteRows", sheet => new DeleteRowsCommand(sheet.Id, 2, 1));

        Check("InsertColumns", sheet => new InsertColumnsCommand(sheet.Id, 2, 1));

        Check("DeleteColumns", sheet => new DeleteColumnsCommand(sheet.Id, 2, 1));
    }
}
