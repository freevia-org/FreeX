using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R78-meta-1 / R78-selfreg-twin-sweep-{2,4,5}: sheet.CellPhoneticGuides (furigana annotations,
/// r76-added) is RichTextRuns' address-keyed companion and must be maintained by the command layer
/// exactly the same way -- cleared on a literal content-replace/Clear-Contents edit (so a stale
/// guide can't be re-emitted onto unrelated new text by a later run-formatting-only save), shifted
/// in lockstep on row/column insert/delete (so it doesn't stay orphaned at a cell's stale pre-shift
/// address), and carried alongside the rich text it decorates on copy/paste. Before this fix, none
/// of EditCellsCommand, ClearContentsCommand, InsertRowsCommand/DeleteRowsCommand,
/// InsertColumnsCommand/DeleteColumnsCommand, CopyRangeCommand, or PasteCellsCommand ever touched
/// sheet.CellPhoneticGuides -- only Sheet.Clone.cs and the xlsx loader wrote to it.
/// </summary>
public sealed class PhoneticGuideCommandTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

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

    // ── EditCellsCommand: literal content-replace must clear the stale guide (R78-meta-1) ──────

    [Fact]
    public void EditCells_ReplacingContent_ClearsStalePhoneticGuide()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[addr] = MakeRuns("furigana");
        sheet.CellPhoneticGuides[addr] = MakeGuide("ri-chi");

        var cmd = new EditCellsCommand(sheet.Id, addr, new TextValue("brand new unrelated text"));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().NotContainKey(addr,
            "a literal content replace must drop the stale phonetic guide, or a later run-formatting edit could re-emit it against the new text");
    }

    [Fact]
    public void EditCells_ReplacingContent_UndoRestoresPhoneticGuide()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[addr] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[addr] = guide;

        var cmd = new EditCellsCommand(sheet.Id, addr, new TextValue("brand new unrelated text"));
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(addr);
        sheet.CellPhoneticGuides[addr].Should().Be(guide);
    }

    // ── Sibling: a plain edit on a cell with no guide stays unaffected ─────────────────────────

    [Fact]
    public void EditCells_PlainCellWithNoGuide_StaysUnaffected()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("plain")));

        var cmd = new EditCellsCommand(sheet.Id, addr, new TextValue("changed"));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().BeEmpty();
    }

    // ── ClearContentsCommand: Delete key must clear the guide (R78-selfreg-twin-sweep-4) ───────

    [Fact]
    public void ClearContents_RemovesPhoneticGuideAndUndoRestoresIt()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[addr] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[addr] = guide;

        var cmd = new ClearContentsCommand(sheet.Id, new GridRange(addr, addr));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().NotContainKey(addr,
            "Clear Contents must remove the phonetic guide along with the text it was attached to");

        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(addr);
        sheet.CellPhoneticGuides[addr].Should().Be(guide);
    }

    [Fact]
    public void ClearContents_CellWithNoGuide_UnaffectedAndNoOrphanEntry()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("plain")));

        var cmd = new ClearContentsCommand(sheet.Id, new GridRange(addr, addr));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.ContainsKey(addr).Should().BeFalse();
        sheet.CellPhoneticGuides.Should().BeEmpty();
    }

    // ── Insert/Delete Rows: the guide must shift with its cell (R78-selfreg-twin-sweep-2) ──────

    [Fact]
    public void InsertRow_AbovePhoneticCell_GuideShiftsToNewAddress()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA5 = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(addrA5, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[addrA5] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[addrA5] = guide;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var addrA6 = new CellAddress(sheet.Id, 6, 1);
        sheet.CellPhoneticGuides.Should().ContainKey(addrA6);
        sheet.CellPhoneticGuides[addrA6].Should().Be(guide);
        sheet.CellPhoneticGuides.Should().NotContainKey(addrA5, "stale guide must not remain at old address");
    }

    [Fact]
    public void InsertRow_AbovePhoneticCell_UndoRestoresOriginalAddress()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA5 = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(addrA5, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[addrA5] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[addrA5] = guide;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(addrA5);
        sheet.CellPhoneticGuides[addrA5].Should().Be(guide);
        sheet.CellPhoneticGuides.Should().NotContainKey(new CellAddress(sheet.Id, 6, 1));
    }

    [Fact]
    public void DeleteRow_WithPhoneticCell_SurvivingGuideShiftsUpAndDeletedGuideIsRemoved()
    {
        var (wb, sheet, ctx) = Setup();
        var addrA2 = new CellAddress(sheet.Id, 2, 1);
        var addrA3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(addrA2, Cell.FromValue(new TextValue("deleted")));
        sheet.CellPhoneticGuides[addrA2] = MakeGuide("de-le-ted");
        sheet.SetCell(addrA3, Cell.FromValue(new TextValue("surviving")));
        var survivingGuide = MakeGuide("sur-vi-ving");
        sheet.CellPhoneticGuides[addrA3] = survivingGuide;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 2, 1);
        sheet.CellPhoneticGuides.Should().ContainKey(newAddr);
        sheet.CellPhoneticGuides[newAddr].Should().Be(survivingGuide);
        sheet.CellPhoneticGuides.Should().NotContainKey(addrA3, "old A3 address must be gone after shift");
    }

    // ── Insert/Delete Columns: same lockstep-shift requirement, on the column axis ──────────────

    [Fact]
    public void InsertColumn_LeftOfPhoneticCell_GuideShiftsRight()
    {
        var (wb, sheet, ctx) = Setup();
        var addrB1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(addrB1, Cell.FromValue(new TextValue("furigana")));
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[addrB1] = guide;

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var addrC1 = new CellAddress(sheet.Id, 1, 3);
        sheet.CellPhoneticGuides.Should().ContainKey(addrC1);
        sheet.CellPhoneticGuides[addrC1].Should().Be(guide);
        sheet.CellPhoneticGuides.Should().NotContainKey(addrB1);
    }

    [Fact]
    public void DeleteColumn_WithPhoneticCell_DeletedGuideRemovedAndSurvivorShifted()
    {
        var (wb, sheet, ctx) = Setup();
        var addrB1 = new CellAddress(sheet.Id, 1, 2);
        var addrC1 = new CellAddress(sheet.Id, 1, 3);
        sheet.CellPhoneticGuides[addrB1] = MakeGuide("de-le-ted");
        var survivingGuide = MakeGuide("sur-vi-ving");
        sheet.CellPhoneticGuides[addrC1] = survivingGuide;

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 1, 2);
        sheet.CellPhoneticGuides.Should().ContainKey(newAddr);
        sheet.CellPhoneticGuides[newAddr].Should().Be(survivingGuide);
        sheet.CellPhoneticGuides.Should().NotContainKey(addrC1, "old C1 address must be gone after shift");
    }

    // ── Copy/Paste: the guide must be carried to the pasted target (R78-selfreg-twin-sweep-5) ──

    [Fact]
    public void CopyRange_PhoneticCell_GuideCarriedToDestination()
    {
        var (wb, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[source] = MakeRuns("furigana");
        var guide = MakeGuide("ri-chi");
        sheet.CellPhoneticGuides[source] = guide;

        var cmd = new CopyRangeCommand(sheet.Id, new GridRange(source, source), destination);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().ContainKey(destination);
        sheet.CellPhoneticGuides[destination].Should().Be(guide);
        // source is left untouched by a copy (unlike a move)
        sheet.CellPhoneticGuides.Should().ContainKey(source);
    }

    [Fact]
    public void CopyRange_PhoneticCell_UndoRemovesGuideAtDestination()
    {
        var (wb, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(source, Cell.FromValue(new TextValue("furigana")));
        sheet.RichTextRuns[source] = MakeRuns("furigana");
        sheet.CellPhoneticGuides[source] = MakeGuide("ri-chi");

        var cmd = new CopyRangeCommand(sheet.Id, new GridRange(source, source), destination);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().NotContainKey(destination);
    }

    [Fact]
    public void PasteCells_WithPhoneticGuides_AppliesGuideAtTargetAndUndoRestoresPrevious()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var oldGuide = MakeGuide("fu-ru-i");
        sheet.SetCell(addr, Cell.FromValue(new TextValue("old")));
        sheet.CellPhoneticGuides[addr] = oldGuide;

        var newGuide = MakeGuide("a-ta-ra-shi-i");
        var cmd = new PasteCellsCommand(
            sheet.Id,
            [(addr, Cell.FromValue(new TextValue("new")))],
            phoneticGuides: new Dictionary<CellAddress, CellPhoneticGuide> { [addr] = newGuide });

        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().ContainKey(addr);
        sheet.CellPhoneticGuides[addr].Should().Be(newGuide);

        cmd.Revert(ctx);

        sheet.CellPhoneticGuides.Should().ContainKey(addr);
        sheet.CellPhoneticGuides[addr].Should().Be(oldGuide);
    }

    [Fact]
    public void PasteCells_WithNoPhoneticGuidesSupplied_ClearsAnyExistingGuideAtTarget()
    {
        var (wb, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new TextValue("old")));
        sheet.CellPhoneticGuides[addr] = MakeGuide("fu-ru-i");

        var cmd = new PasteCellsCommand(sheet.Id, [(addr, Cell.FromValue(new TextValue("new")))]);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CellPhoneticGuides.Should().NotContainKey(addr,
            "pasting a payload with no phonetic guide must not leave the destination's old guide behind");
    }
}
