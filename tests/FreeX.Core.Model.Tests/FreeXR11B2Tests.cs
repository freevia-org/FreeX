using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-11 fix bucket R2 regression tests.
/// </summary>
public class FreeXR11B2Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    /// <summary>
    /// R11-structural-edits-1: Autofill (fill-handle drag) overwrites a cell's value but must
    /// also drop that destination cell's stale hyperlink/rich-text-run annotations. A1 holds a
    /// plain number (source); the destination A2 previously held different, richly formatted,
    /// hyperlinked content (e.g. text left over before the user dragged the fill handle over it).
    /// After Autofill writes the plain numeric series into A2, Excel drops the old hyperlink and
    /// rich-text formatting entirely — a numeric cell must never keep a clickable hyperlink or
    /// orphaned rich-text runs.
    /// </summary>
    [Fact]
    public void Autofill_Down_ClearsDestinationCellsStaleHyperlinkAndRichText()
    {
        var (_, sheet, ctx) = Setup();
        var sourceAddr = new CellAddress(sheet.Id, 1, 1); // A1
        var destAddr = new CellAddress(sheet.Id, 2, 1);   // A2

        sheet.SetCell(sourceAddr, new NumberValue(5));

        // A2 starts out with stale annotations (as if it previously held different, richly
        // formatted, hyperlinked content that the user is about to overwrite via fill).
        sheet.SetCell(destAddr, Cell.FromValue(new TextValue("old")));
        sheet.Hyperlinks[destAddr] = "https://example.invalid/stale";
        sheet.HyperlinkMetadata[destAddr] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Stale");
        sheet.RichTextRuns[destAddr] =
        [
            new CellTextRun("old", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)
        ];

        var sourceRange = new GridRange(sourceAddr, sourceAddr);
        var fillRange = new GridRange(destAddr, destAddr);

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(2, 1).Should().Be(new NumberValue(5));
        sheet.Hyperlinks.ContainsKey(destAddr).Should().BeFalse("a plain numeric fill result must not keep a stale hyperlink");
        sheet.HyperlinkMetadata.ContainsKey(destAddr).Should().BeFalse();
        sheet.RichTextRuns.ContainsKey(destAddr).Should().BeFalse("a plain numeric fill result must not keep orphaned rich-text runs");
    }

    /// <summary>
    /// R11-structural-edits-1 (inward-clear path): dragging the fill handle inward clears the
    /// cells beyond the shrunk boundary using Clear-Contents semantics, which in Excel also drops
    /// hyperlinks and rich-text run formatting (not just the value).
    /// </summary>
    [Fact]
    public void Autofill_InwardClear_ClearsDestinationCellsStaleHyperlinkAndRichText()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);

        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new TextValue("linked"));
        sheet.Hyperlinks[a3] = "https://example.invalid/stale";
        sheet.HyperlinkMetadata[a3] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Stale");
        sheet.RichTextRuns[a3] =
        [
            new CellTextRun("linked", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)
        ];

        // Source range A1:A3, shrink the fill handle inward: the fill (clear) range A3:A3 is the
        // sub-range beyond the new boundary that Excel's Clear-Contents-on-shrink drops.
        var sourceRange = new GridRange(a1, a3);
        var fillRange = new GridRange(a3, a3);

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);
        sheet.Hyperlinks.ContainsKey(a3).Should().BeFalse();
        sheet.HyperlinkMetadata.ContainsKey(a3).Should().BeFalse();
        sheet.RichTextRuns.ContainsKey(a3).Should().BeFalse();
    }
}
