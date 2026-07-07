using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-11 fix bucket R8.
/// </summary>
public sealed class FreeXR11B8Tests
{
    /// <summary>
    /// R11-commands-undo-2: Grouped-sheet edit must strip stale hyperlinks/rich-text runs from
    /// the overwritten cell (matching EditCellsCommand), and undo must restore them.
    /// </summary>
    [Fact]
    public void GroupedEditCellsCommand_Apply_RemovesStaleHyperlinkAndRichTextRuns_AndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet1.Id, 2, 2);
        var target = new CellAddress(sheet2.Id, 2, 2);

        // Both grouped sheets start with a hyperlinked, rich-text cell ("Google").
        sheet1.SetCell(source, Cell.FromValue(new TextValue("Google")));
        sheet1.Hyperlinks[source] = "https://google.com";
        sheet1.HyperlinkMetadata[source] = new HyperlinkMetadata(ScreenTip: "Go to Google");
        sheet1.RichTextRuns[source] = [new CellTextRun("Google", true, null, null, null, null, null, null)];

        sheet2.SetCell(target, Cell.FromValue(new TextValue("Google")));
        sheet2.Hyperlinks[target] = "https://google.com";
        sheet2.HyperlinkMetadata[target] = new HyperlinkMetadata(ScreenTip: "Go to Google");
        sheet2.RichTextRuns[target] = [new CellTextRun("Google", true, null, null, null, null, null, null)];

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(source, Cell.FromValue(new NumberValue(42)))]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        // New content lands on both sheets...
        sheet1.GetValue(source).Should().Be(new NumberValue(42));
        sheet2.GetValue(target).Should().Be(new NumberValue(42));

        // ...and the stale hyperlink/rich-text from the old "Google" content must not survive
        // on either grouped sheet (matches EditCellsCommand's Commands.cs:98-100 behavior).
        sheet1.Hyperlinks.Should().NotContainKey(source);
        sheet1.HyperlinkMetadata.Should().NotContainKey(source);
        sheet1.RichTextRuns.Should().NotContainKey(source);
        sheet2.Hyperlinks.Should().NotContainKey(target);
        sheet2.HyperlinkMetadata.Should().NotContainKey(target);
        sheet2.RichTextRuns.Should().NotContainKey(target);

        command.Revert(ctx);

        // Undo must restore the original value AND the hyperlink/rich-text on both sheets.
        sheet1.GetValue(source).Should().Be(new TextValue("Google"));
        sheet1.Hyperlinks[source].Should().Be("https://google.com");
        sheet1.HyperlinkMetadata[source].Should().Be(new HyperlinkMetadata(ScreenTip: "Go to Google"));
        sheet1.RichTextRuns[source].Should().BeEquivalentTo(
            new[] { new CellTextRun("Google", true, null, null, null, null, null, null) });

        sheet2.GetValue(target).Should().Be(new TextValue("Google"));
        sheet2.Hyperlinks[target].Should().Be("https://google.com");
        sheet2.HyperlinkMetadata[target].Should().Be(new HyperlinkMetadata(ScreenTip: "Go to Google"));
        sheet2.RichTextRuns[target].Should().BeEquivalentTo(
            new[] { new CellTextRun("Google", true, null, null, null, null, null, null) });
    }
}
