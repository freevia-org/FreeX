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

        // r407: annotation layers, so the clear-* commands have something to clear. Without these
        // their Apply is a no-op and the change-gate rejects them -- correctly, since a test that
        // clears nothing proves nothing about restoring it.
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "first note";
        sheet.Comments[new CellAddress(sheet.Id, 2, 2)] = "second note";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 1, 2)] = "https://example.invalid/a";

        // r416: validation and conditional-format rules, so the commands that clear them have
        // something to clear. The change-gate rejects a no-op Apply, which is how the earlier
        // annotation commands were caught needing this in r407.
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
        });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = GridRange.Parse("A1:A5", sheet.Id),
        });

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

            // r407: the annotation layers. A command that clears comments, validations, hyperlinks
            // or conditional formats and reverts imperfectly would be invisible to a snapshot of
            // cells and geometry alone -- the state lives beside the grid, not in it.
            foreach (var (address, comment) in sheet.Comments.OrderBy(pair => pair.Key.Row).ThenBy(pair => pair.Key.Col))
                builder.Append("  comment ").Append(address.Row).Append(',').Append(address.Col).Append('=').Append(comment).AppendLine();

            foreach (var (address, target) in sheet.Hyperlinks.OrderBy(pair => pair.Key.Row).ThenBy(pair => pair.Key.Col))
                builder.Append("  link ").Append(address.Row).Append(',').Append(address.Col).Append('=').Append(target).AppendLine();

            builder.Append("  validations=").Append(sheet.DataValidations.Count).AppendLine();
            builder.Append("  conditionalFormats=").Append(sheet.ConditionalFormats.Count).AppendLine();

            // Added because the change-gate rejected ToggleWorksheetAutoFilter: the command changes
            // only this field, so with it missing the snapshot could not see the command work at
            // all. The gate refusing to let that test through is the whole reason it exists.
            // r416: added for the same reason the autofilter line was -- AllowEditRange changes only
            // this collection, so without it the change-gate correctly refused to let that command
            // be tested against a snapshot that could not see it work.
            foreach (var range in sheet.AllowEditRanges.OrderBy(r => r.ToString(), StringComparer.Ordinal))
                builder.Append("  allowEdit ").Append(range).AppendLine();

            builder.Append("  autoFilter=").Append(sheet.AutoFilter?.Reference ?? "none")
                .Append(" cols=").Append(sheet.AutoFilter?.FilterColumns.Count ?? 0).AppendLine();

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
        snapshot.Should().Contain("comment 1,1=first note", "comments must be part of the comparison");
        snapshot.Should().Contain("link 1,2=", "hyperlinks must be part of the comparison");
        snapshot.Should().Contain("validations=", "data validations must be part of the comparison");
        snapshot.Should().Contain("conditionalFormats=", "conditional formats must be part of the comparison");
        snapshot.Should().Contain("autoFilter=", "the autofilter must be part of the comparison");
        snapshot.Should().Contain("validations=1", "the seeded validation rule must be visible to the comparison");
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

        // r406: extending the sample, which the previous entry called "a line per command". These
        // reach state the first six did not: merges as their own operation, style ids, the row and
        // column sizing the snapshot was widened for, and workbook-level sheet identity and order.
        Check("MergeCells", sheet => new MergeCellsCommand(sheet.Id, GridRange.Parse("A1:B1", sheet.Id)));

        Check("UnmergeCells", sheet => new UnmergeCellsCommand(sheet.Id, GridRange.Parse("C5:D5", sheet.Id)));

        Check("ApplyStyle", sheet => new ApplyStyleCommand(
            sheet.Id, GridRange.Parse("A1:B2", sheet.Id), new StyleDiff { Bold = true }));

        Check("SetRowHeight", sheet => new SetRowHeightCommand(sheet.Id, 2, 4, 33.5));

        Check("SetColumnWidth", sheet => new SetColumnWidthCommand(sheet.Id, 2, 4, 21.75));

        Check("AddSheet", _ => new AddSheetCommand("Added"));

        Check("RenameSheet", sheet => new RenameSheetCommand(sheet.Id, "Renamed"));

        // r407: the annotation layers the snapshot was just deepened for.
        Check("ClearComments", sheet => new ClearCommentsCommand(
            sheet.Id, GridRange.Parse("A1:B2", sheet.Id)));

        Check("ClearHyperlinks", sheet => new ClearHyperlinksCommand(
            sheet.Id, GridRange.Parse("A1:B2", sheet.Id)));

        Check("ToggleWorksheetAutoFilter", sheet => new ToggleWorksheetAutoFilterCommand(
            sheet.Id, GridRange.Parse("A1:D6", sheet.Id)));

        // r416: the cell-shifting family, which moves data rather than editing it in place. These
        // are the ones where a partial Revert is hardest to notice -- the grid still looks
        // plausible, just with a row of values one column left of where it belongs.
        Check("InsertCells (shift down)", sheet => new InsertCellsCommand(
            sheet.Id, GridRange.Parse("A2:B3", sheet.Id), InsertCellsShiftDirection.Down));

        Check("InsertCells (shift right)", sheet => new InsertCellsCommand(
            sheet.Id, GridRange.Parse("A2:B3", sheet.Id), InsertCellsShiftDirection.Right));

        Check("DeleteCells (shift up)", sheet => new DeleteCellsCommand(
            sheet.Id, GridRange.Parse("A2:B3", sheet.Id), DeleteCellsShiftDirection.Up));

        Check("DeleteCells (shift left)", sheet => new DeleteCellsCommand(
            sheet.Id, GridRange.Parse("A2:B3", sheet.Id), DeleteCellsShiftDirection.Left));

        Check("CopyRange", sheet => new CopyRangeCommand(
            sheet.Id, GridRange.Parse("A1:B2", sheet.Id), new CellAddress(sheet.Id, 10, 1)));

        Check("MoveRange", sheet => new MoveRangeCommand(
            sheet.Id, GridRange.Parse("A1:B2", sheet.Id), new CellAddress(sheet.Id, 10, 1)));

        Check("FillCells", sheet => new FillCellsCommand(
            sheet.Id, GridRange.Parse("A1:A4", sheet.Id), FillCellsDirection.Down));

        Check("AllowEditRange", sheet => new AllowEditRangeCommand(
            sheet.Id, GridRange.Parse("A1:B2", sheet.Id)));

        Check("ClearConditionalFormats", sheet => new ClearConditionalFormatsCommand(
            sheet.Id, GridRange.Parse("A1:A5", sheet.Id)));

        Check("ClearDataValidation", sheet => new ClearDataValidationCommand(
            sheet.Id, GridRange.Parse("A1:A5", sheet.Id)));
    }

    /// <summary>
    /// r416: needs duplicate rows to remove, which the shared fixture deliberately does not have --
    /// every cell there is distinct so the other commands operate on unambiguous data. Adding
    /// duplicates to the shared fixture to accommodate one command would change what every other
    /// command is tested against, so it gets its own.
    /// </summary>
    [Fact]
    public void RemovingDuplicateRowsUndoesExactly()
    {
        var workbook = new Workbook("dupes");
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= 4; row++)
        {
            // Rows 1 and 3 identical, 2 and 4 identical: two removals, not one, so a Revert that
            // restores only the last removed row fails here.
            var value = row % 2 == 1 ? "odd" : "even";
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(value));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(value + "-b"));
        }

        var context = new TestCommandContext(workbook);
        var before = Snapshot(workbook);

        var command = new RemoveDuplicateRowsCommand(sheet.Id, GridRange.Parse("A1:B4", sheet.Id));
        command.Apply(context);

        Snapshot(workbook).Should().NotBe(before, "the fixture has duplicate rows, so some must go");

        command.Revert(context);

        Snapshot(workbook).Should().Be(before, "RemoveDuplicateRows: undo must restore every removed row");
    }

    /// <summary>
    /// Sheet ORDER is workbook-level state that a per-sheet snapshot would miss, so it gets its own
    /// fixture with a second sheet to move.
    /// </summary>
    [Fact]
    public void MovingASheetUndoesExactly()
    {
        var (workbook, _, context) = Setup();
        workbook.AddSheet("Second");

        var before = Snapshot(workbook);
        var command = new MoveSheetCommand(0, 1);
        command.Apply(context);

        Snapshot(workbook).Should().NotBe(before, "moving a sheet must reorder the workbook");

        command.Revert(context);

        Snapshot(workbook).Should().Be(before, "MoveSheet: undo must restore the original order");
    }
}
