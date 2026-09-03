using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r260: two commands r221 grouped as "needing a before/after snapshot comparison, which is a change
/// to how they work rather than a guard bolted on". For PasteColumnWidths that turned out to be
/// half-true: it already holds the BEFORE half, because its undo record is the destination columns'
/// prior widths. SetHyperlink holds five separate pieces of before-state, and the decision compares
/// all five.
/// </summary>
public sealed class R260_ColumnWidthsAndHyperlinkNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) SetUp()
    {
        var wb = new Workbook("R260");
        var sheet = wb.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(wb));
    }

    private static GridRange Columns(Sheet sheet, uint first, uint last) =>
        new(new CellAddress(sheet.Id, 1, first), new CellAddress(sheet.Id, 1, last));

    [Fact]
    public void PasteColumnWidths_OntoColumnsThatAlreadyHaveThemIsANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.ColumnWidths[1] = 30;
        sheet.ColumnWidths[2] = 40;
        sheet.ColumnWidths[4] = 30;
        sheet.ColumnWidths[5] = 40;

        new PasteColumnWidthsCommand(sheet.Id, Columns(sheet, 1, 2), destinationStartCol: 4).Apply(ctx)
            .IsNoOp.Should().BeTrue("the destination columns already have exactly these widths");
        sheet.ColumnWidths[4].Should().Be(30);
    }

    [Fact]
    public void PasteColumnWidths_OntoDifferentWidthsIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.ColumnWidths[1] = 30;
        sheet.ColumnWidths[4] = 12;

        new PasteColumnWidthsCommand(sheet.Id, Columns(sheet, 1, 1), destinationStartCol: 4).Apply(ctx)
            .IsNoOp.Should().BeFalse("column 4 is resized from 12 to 30");
        sheet.ColumnWidths[4].Should().Be(30);
    }

    /// <summary>
    /// Absent is not the same as present-with-the-default: pasting a column that has no explicit
    /// width REMOVES the destination's, which the count comparison has to catch.
    /// </summary>
    [Fact]
    public void PasteColumnWidths_ClearingAnExplicitWidthIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.ColumnWidths[4] = 25;

        new PasteColumnWidthsCommand(sheet.Id, Columns(sheet, 1, 1), destinationStartCol: 4).Apply(ctx)
            .IsNoOp.Should().BeFalse("the source column has no explicit width, so column 4 loses its");
        sheet.ColumnWidths.ContainsKey(4).Should().BeFalse();
    }

    private static CellAddress Cell(Sheet sheet) => new(sheet.Id, 1, 1);

    [Fact]
    public void SetHyperlink_ReapplyingTheSameLinkIsANoOp()
    {
        var (sheet, ctx) = SetUp();

        new SetHyperlinkCommand(sheet.Id, Cell(sheet), "https://example.com", "Example", new HyperlinkMetadata())
            .Apply(ctx).IsNoOp.Should().BeFalse("the cell had no link");

        new SetHyperlinkCommand(sheet.Id, Cell(sheet), "https://example.com", "Example", new HyperlinkMetadata())
            .Apply(ctx).IsNoOp.Should().BeTrue("same target, same display text, same metadata");
    }

    [Fact]
    public void SetHyperlink_ChangingTheTargetIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();

        new SetHyperlinkCommand(sheet.Id, Cell(sheet), "https://example.com", "Example", new HyperlinkMetadata()).Apply(ctx);

        new SetHyperlinkCommand(sheet.Id, Cell(sheet), "https://example.org", "Example", new HyperlinkMetadata())
            .Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void SetHyperlink_ChangingTheDisplayTextIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();

        new SetHyperlinkCommand(sheet.Id, Cell(sheet), "https://example.com", "Example", new HyperlinkMetadata()).Apply(ctx);

        new SetHyperlinkCommand(sheet.Id, Cell(sheet), "https://example.com", "Example site", new HyperlinkMetadata())
            .Apply(ctx).IsNoOp.Should().BeFalse("the cell's text changes");
    }

    [Fact]
    public void SetHyperlink_ChangingOnlyTheScreenTipIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();

        new SetHyperlinkCommand(sheet.Id, Cell(sheet), "https://example.com", "Example", new HyperlinkMetadata()).Apply(ctx);

        new SetHyperlinkCommand(
            sheet.Id, Cell(sheet), "https://example.com", "Example", new HyperlinkMetadata(ScreenTip: "Opens the site"))
            .Apply(ctx).IsNoOp.Should().BeFalse(
                "the screen tip is metadata that round-trips into the saved file, and nothing else differs");
    }

    /// <summary>
    /// Apply REMOVES any rich-text runs on the cell, so re-linking a cell that carries them is a real
    /// change even when the target, the text and the metadata all match. The captured "had runs" flag
    /// is what makes that visible.
    /// </summary>
    [Fact]
    public void SetHyperlink_OverRichTextRunsIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();
        var address = Cell(sheet);

        new SetHyperlinkCommand(sheet.Id, address, "https://example.com", "Example", new HyperlinkMetadata()).Apply(ctx);
        sheet.RichTextRuns[address] = [new CellTextRun("Example", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)];

        new SetHyperlinkCommand(sheet.Id, address, "https://example.com", "Example", new HyperlinkMetadata())
            .Apply(ctx).IsNoOp.Should().BeFalse("the bold run is stripped by the re-link");
        sheet.RichTextRuns.ContainsKey(address).Should().BeFalse();
    }
}
