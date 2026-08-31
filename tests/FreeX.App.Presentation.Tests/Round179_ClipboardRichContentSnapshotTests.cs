using FluentAssertions;

using FreeX.App.Presentation.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Round 179. The copy path stored the LIVE source Sheet on the clipboard snapshot, and
/// PasteCommandFactory reads hyperlinks, rich-text runs, hyperlink metadata and phonetic guides off
/// it at PASTE time. Editing the source cell between Ctrl+C and Ctrl+V therefore changed what got
/// pasted -- change or clear the source cell's hyperlink after copying and the paste carried the
/// new state, not what was on the cell when the user copied it. Cell VALUES were already captured
/// as independent clones; this rich-content side channel was the one left live.
/// </summary>
public sealed class Round179_ClipboardRichContentSnapshotTests
{
    private static (Workbook Workbook, Sheet Sheet) NewSheet()
    {
        var workbook = new Workbook("Clip");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    [Fact]
    public void EditingTheSourceHyperlinkAfterCopying_DoesNotChangeWhatWasCaptured()
    {
        var (_, sheet) = NewSheet();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new TextValue("report")));
        sheet.Hyperlinks[address] = "https://example.com/original";

        var range = new GridRange(address, address);
        var captured = ClipboardRichContentSnapshot.Capture(sheet, range);

        // The user retargets the source cell's link before pasting.
        sheet.Hyperlinks[address] = "https://example.com/CHANGED";

        captured.Hyperlinks[address].Should().Be(
            "https://example.com/original",
            "the paste must carry the link the cell had when it was copied");
    }

    [Fact]
    public void ClearingTheSourceHyperlinkAfterCopying_DoesNotEmptyTheCapture()
    {
        var (_, sheet) = NewSheet();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new TextValue("report")));
        sheet.Hyperlinks[address] = "https://example.com/original";

        var captured = ClipboardRichContentSnapshot.Capture(sheet, new GridRange(address, address));

        sheet.Hyperlinks.Remove(address);

        captured.Hyperlinks.Should().ContainKey(address,
            "removing the source link after the copy must not empty the clipboard's copy of it");
    }

    [Fact]
    public void RichTextRunsAndPhoneticGuides_AreAlsoCapturedAtCopyTime()
    {
        var (_, sheet) = NewSheet();
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, Cell.FromValue(new TextValue("abc")));
        sheet.RichTextRuns[address] = [new CellTextRun("abc", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)];

        var captured = ClipboardRichContentSnapshot.Capture(sheet, new GridRange(address, address));

        sheet.RichTextRuns.Remove(address);

        captured.RichTextRuns.Should().ContainKey(address);
    }

    [Fact]
    public void OnlyTheCopiedRangeIsCaptured()
    {
        // Copying one cell of a sheet carrying thousands of hyperlinks must not duplicate them all.
        var (_, sheet) = NewSheet();
        var copied = new CellAddress(sheet.Id, 1, 1);
        var elsewhere = new CellAddress(sheet.Id, 50, 4);
        sheet.Hyperlinks[copied] = "https://example.com/in-range";
        sheet.Hyperlinks[elsewhere] = "https://example.com/out-of-range";

        var captured = ClipboardRichContentSnapshot.Capture(sheet, new GridRange(copied, copied));

        captured.Hyperlinks.Should().ContainKey(copied);
        captured.Hyperlinks.Should().NotContainKey(elsewhere);
    }
}
