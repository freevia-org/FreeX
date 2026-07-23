using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R79-meta-{1,2,3,4,5}/R79-selfreg-newfield-sweep-2/R79-commands-undo-redo-5-1: continues the r78
/// sheet.CellPhoneticGuides sweep (see PhoneticGuideCommandTests) into the command-layer mutations
/// that were still missing it: AutofillCommand (fill-handle drag), SortCommand (per-row permute),
/// MoveRangeCommand (cut/drag-move, same-sheet and cross-sheet), FillCellsCommand (Fill Down/Right/
/// Up/Left), PasteSpecialCommand (Operation.None content-replace path), and GroupedEditCellsCommand
/// (grouped-sheet edits). Each of these already maintained sheet.RichTextRuns/Hyperlinks correctly
/// for the exact same class of edit; CellPhoneticGuides was the one address-keyed companion
/// dictionary left behind, so a furigana annotation went stale (mis-rendered onto unrelated content)
/// or was silently dropped by these operations before this fix.
/// </summary>
public sealed class R79_PhoneticGuideCommandSweepTests
{
    // ── helpers (mirrors PhoneticGuideCommandTests) ─────────────────────────────────────────────

    private static IReadOnlyList<CellTextRun> MakeRuns(string text) =>
        [new CellTextRun(text, Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)];

    private static CellPhoneticGuide MakeGuide(string reading) =>
        new([$"""<rPh sb="0" eb="4"><t>{reading}</t></rPh>"""], """<phoneticPr fontId="1" type="noConversion"/>""");

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ── AutofillCommand: fill-handle drag must carry the guide (R79-meta-1) ─────────────────────

    [Fact]
    public void Autofill_PlainCopyOfTextCell_CarriesPhoneticGuideToTarget()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[source] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[source] = guide;

        var cmd = new AutofillCommand(sheet.Id, new GridRange(source, source), new GridRange(target, target));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().ContainKey(target);
        sheet.CellPhoneticGuides[target].Should().Be(guide);
    }

    [Fact]
    public void Autofill_OverwritingTargetWithDifferentGuide_UndoRestoresOriginalTargetGuide()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[source] = MakeRuns("furigana");
        sheet.CellPhoneticGuides[source] = MakeGuide("ri-chi");

        sheet.SetCell(target, Cell.FromValue(new TextValue("unrelated")));
        var staleGuide = MakeGuide("mu-ka-n-ke-i");
        sheet.CellPhoneticGuides[target] = staleGuide;

        var cmd = new AutofillCommand(sheet.Id, new GridRange(source, source), new GridRange(target, target));
        cmd.Apply(ctx).Success.Should().BeTrue();
        // Fill replaced target's content with source's, so the stale guide must not survive:
        sheet.CellPhoneticGuides[target].Should().Be(sheet.CellPhoneticGuides[source]);

        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(target);
        sheet.CellPhoneticGuides[target].Should().Be(staleGuide,
            "undo must restore whatever guide previously sat at the overwritten target");
    }

    // ── SortCommand: a row permute must carry the guide along with its row (R79-meta-2) ────────

    [Fact]
    public void Sort_PermutesPhoneticGuideWithItsRow()
    {
        var (_, sheet, ctx) = Setup();
        // Row 1: "furigana" (guide) / 2   Row 2: "aaa" (no guide) / 1
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("furigana")));
        sheet.SetCell(b1, new NumberValue(2));
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[a1] = guide;
        sheet.SetCell(a2, Cell.FromValue(new TextValue("aaa")));
        sheet.SetCell(b2, new NumberValue(1));

        // Ascending sort by column B (offset 1) moves row2 ("aaa"/1) above row1 ("furigana"/2).
        var range = new GridRange(a1, b2);
        var cmd = new SortCommand(sheet.Id, range, sortByColOffset: 1, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("aaa"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("furigana"));

        sheet.CellPhoneticGuides.Should().NotContainKey(a1,
            "the guide must not stay behind at the old row address after the sort moved the annotated cell away");
        sheet.CellPhoneticGuides.Should().ContainKey(a2);
        sheet.CellPhoneticGuides[a2].Should().Be(guide);
    }

    [Fact]
    public void Sort_Undo_RestoresGuideToOriginalRow()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("furigana")));
        sheet.SetCell(b1, new NumberValue(2));
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[a1] = guide;
        sheet.SetCell(a2, Cell.FromValue(new TextValue("aaa")));
        sheet.SetCell(b2, new NumberValue(1));

        var range = new GridRange(a1, b2);
        var cmd = new SortCommand(sheet.Id, range, sortByColOffset: 1, ascending: true);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(a1);
        sheet.CellPhoneticGuides[a1].Should().Be(guide);
        sheet.CellPhoneticGuides.Should().NotContainKey(a2);
    }

    // ── MoveRangeCommand: cut/drag-move must migrate the guide, same-sheet and cross-sheet
    // (R79-meta-3 / R79-selfreg-newfield-sweep-2) ───────────────────────────────────────────────

    [Fact]
    public void MoveRange_SameSheet_MigratesGuideToDestinationAndClearsSource()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[source] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[source] = guide;

        var cmd = new MoveRangeCommand(sheet.Id, new GridRange(source, source), destination);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().ContainKey(destination);
        sheet.CellPhoneticGuides[destination].Should().Be(guide);
        sheet.CellPhoneticGuides.Should().NotContainKey(source,
            "the vacated source address must not keep showing furigana for content that moved away");
    }

    [Fact]
    public void MoveRange_SameSheet_Undo_RestoresGuideAtSourceAndRemovesFromDestination()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[source] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[source] = guide;

        var cmd = new MoveRangeCommand(sheet.Id, new GridRange(source, source), destination);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(source);
        sheet.CellPhoneticGuides[source].Should().Be(guide);
        sheet.CellPhoneticGuides.Should().NotContainKey(destination);
    }

    [Fact]
    public void MoveRange_CrossSheet_MigratesGuideToDestinationSheet()
    {
        var (wb, sheet1, ctx) = Setup();
        var sheet2 = wb.AddSheet("Sheet2");
        var source = new CellAddress(sheet1.Id, 1, 1);
        var destination = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet1.RichTextRuns[source] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet1.CellPhoneticGuides[source] = guide;

        var cmd = new MoveRangeCommand(sheet1.Id, new GridRange(source, source), destination);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet2.CellPhoneticGuides.Should().ContainKey(destination);
        sheet2.CellPhoneticGuides[destination].Should().Be(guide);
        sheet1.CellPhoneticGuides.Should().NotContainKey(source,
            "a cross-sheet move must not leave the guide registered against the source sheet's now-vacated cell");
    }

    // ── FillCellsCommand: Fill Down/Right/Up/Left must carry the guide (R79-meta-4) ─────────────

    [Fact]
    public void FillCells_Down_CarriesPhoneticGuideToFilledCells()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[source] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[source] = guide;

        var range = new GridRange(source, target);
        var cmd = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().ContainKey(target);
        sheet.CellPhoneticGuides[target].Should().Be(guide);
    }

    [Fact]
    public void FillCells_Down_OverwritingTargetWithNoSourceGuide_RemovesStaleTargetGuideAndUndoRestoresIt()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("plain")));
        // No guide at source.
        var staleGuide = MakeGuide("mu-ka-n-ke-i");
        sheet.SetCell(target, Cell.FromValue(new TextValue("unrelated")));
        sheet.CellPhoneticGuides[target] = staleGuide;

        var range = new GridRange(source, target);
        var cmd = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().NotContainKey(target,
            "a fill with no guide at the source must not leave a stale guide behind at the target");

        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(target);
        sheet.CellPhoneticGuides[target].Should().Be(staleGuide);
    }

    // ── PasteSpecialCommand: Operation.None content-replace must clear the stale destination guide
    // (R79-meta-5) ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PasteSpecial_OperationNone_ClearsStaleGuideAtDestination()
    {
        var (_, sheet, ctx) = Setup();
        var destination = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(destination, Cell.FromValue(new TextValue("old")));
        var staleGuide = MakeGuide("fu-ru-i");
        sheet.CellPhoneticGuides[destination] = staleGuide;

        var sourceAddr = new CellAddress(sheet.Id, 1, 1);
        var sourceCells = new List<(CellAddress Address, Cell Cell)> { (sourceAddr, Cell.FromValue(new TextValue("new"))) };
        var cmd = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(sourceAddr, sourceAddr),
            sourceCells,
            destination,
            new PasteSpecialOptions());

        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().NotContainKey(destination,
            "a content-replacing Paste Special must not leave a stale guide attached to the newly-pasted content");
    }

    [Fact]
    public void PasteSpecial_OperationNone_WithSourceGuideSupplied_CarriesGuideAndUndoRestoresPrevious()
    {
        var (_, sheet, ctx) = Setup();
        var sourceAddr = new CellAddress(sheet.Id, 10, 10);
        var destination = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(destination, Cell.FromValue(new TextValue("old")));
        var oldGuide = MakeGuide("fu-ru-i");
        sheet.CellPhoneticGuides[destination] = oldGuide;

        var newGuide = MakeGuide("a-ta-ra-shi-i");
        var sourceCells = new List<(CellAddress Address, Cell Cell)> { (sourceAddr, Cell.FromValue(new TextValue("new"))) };
        var cmd = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(sourceAddr, sourceAddr),
            sourceCells,
            destination,
            new PasteSpecialOptions(),
            sourcePhoneticGuides: new Dictionary<CellAddress, CellPhoneticGuide> { [sourceAddr] = newGuide });

        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().ContainKey(destination);
        sheet.CellPhoneticGuides[destination].Should().Be(newGuide);

        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(destination);
        sheet.CellPhoneticGuides[destination].Should().Be(oldGuide);
    }

    // ── GroupedEditCellsCommand: a grouped-sheet content replace must clear the stale guide on
    // every grouped sheet, and Undo must restore it (R79-commands-undo-redo-5-1) ─────────────────

    [Fact]
    public void GroupedEdit_ReplacingContent_ClearsStaleGuideOnBothSheets()
    {
        var (wb, sheet1, ctx) = Setup();
        var sheet2 = wb.AddSheet("Sheet2");
        var addr1 = new CellAddress(sheet1.Id, 1, 1);
        var addr2 = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(addr1, Cell.FromValue(new TextValue("furigana")));
        sheet1.CellPhoneticGuides[addr1] = MakeGuide("ri-chi");
        sheet2.SetCell(addr2, Cell.FromValue(new TextValue("furigana")));
        sheet2.CellPhoneticGuides[addr2] = MakeGuide("ri-chi");

        var cmd = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(addr1, Cell.FromValue(new TextValue("brand new")))]);

        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet1.CellPhoneticGuides.Should().NotContainKey(addr1,
            "grouped-sheet content replace must clear the stale guide on the source sheet");
        sheet2.CellPhoneticGuides.Should().NotContainKey(addr2,
            "grouped-sheet content replace must clear the stale guide on every grouped sheet, not just the source");
    }

    [Fact]
    public void GroupedEdit_Undo_RestoresGuideOnBothSheets()
    {
        var (wb, sheet1, ctx) = Setup();
        var sheet2 = wb.AddSheet("Sheet2");
        var addr1 = new CellAddress(sheet1.Id, 1, 1);
        var addr2 = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(addr1, Cell.FromValue(new TextValue("furigana")));
        var guide1 = MakeGuide("ri-chi");
        sheet1.CellPhoneticGuides[addr1] = guide1;
        sheet2.SetCell(addr2, Cell.FromValue(new TextValue("furigana")));
        var guide2 = MakeGuide("ri-chi");
        sheet2.CellPhoneticGuides[addr2] = guide2;

        var cmd = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(addr1, Cell.FromValue(new TextValue("brand new")))]);

        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet1.CellPhoneticGuides.Should().ContainKey(addr1);
        sheet1.CellPhoneticGuides[addr1].Should().Be(guide1);
        sheet2.CellPhoneticGuides.Should().ContainKey(addr2);
        sheet2.CellPhoneticGuides[addr2].Should().Be(guide2);
    }

    // ── RemoveDuplicateRowsCommand: surviving row's guide must shift with it (bonus fix found by
    // the same-gap grep sweep this round's guidance requested) ────────────────────────────────────

    [Fact]
    public void RemoveDuplicateRows_SurvivingGuideShiftsUpAndDuplicateGuideIsDropped()
    {
        var (_, sheet, ctx) = Setup();
        // Row1: A/1   Row2: B/2   Row3: A/1 (duplicate of row1, removed)   Row4: furigana/3 (guide)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1));
        var surviveAddr = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(surviveAddr, new TextValue("furigana"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));
        var survivingGuide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[surviveAddr] = survivingGuide;

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        var cmd = new RemoveDuplicateRowsCommand(sheet.Id, range);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Row3 (duplicate) is removed, so row4's content -- and its guide -- compacts up to row3.
        var shiftedAddr = new CellAddress(sheet.Id, 3, 1);
        sheet.CellPhoneticGuides.Should().ContainKey(shiftedAddr);
        sheet.CellPhoneticGuides[shiftedAddr].Should().Be(survivingGuide);
        sheet.CellPhoneticGuides.Should().NotContainKey(surviveAddr,
            "the guide's old pre-compaction address must be gone after the shift");
    }
}
