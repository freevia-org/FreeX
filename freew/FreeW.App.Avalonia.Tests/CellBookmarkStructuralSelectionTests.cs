using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class CellBookmarkStructuralSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(false, "delete")]
    [InlineData(true, "delete")]
    [InlineData(false, "backspace")]
    [InlineData(true, "backspace")]
    [InlineData(false, "typing")]
    [InlineData(true, "typing")]
    [InlineData(false, "paste")]
    [InlineData(true, "paste")]
    public Task SameCellMergePreservesAllParagraphBookmarkNamesAndRetainedRunMetadata(bool reversed, string action) =>
        Session.Dispatch(() =>
        {
            var section = new Section(new PageSettings(), SectionBreakKind.NextPage);
            var first = new Paragraph
            {
                StyleId = "StartStyle", SectionBreak = section,
                PreservedNumbering = new PreservedNumbering(7, 2),
                ParagraphFormatRevision = new ParagraphFormatRevision(ParagraphFormatting.Default,
                    "Review author", "2026-07-26T00:00:00Z"),
                Runs = { new Run("A", RunFormatting.Default with { Bold = true }), new Run("BC") },
            };
            first.BookmarkNames.Add("First");
            var middle = new Paragraph("deleted middle");
            middle.BookmarkNames.Add("Middle");
            var last = new Paragraph { Runs = { new Run("XY") { StyleId = "SuffixCharacter" }, new Run("Z") } };
            last.BookmarkNames.Add("Last");
            var (view, cell) = View(first, middle, last);
            Select(view, 0, 1, 2, 1, reversed);
            view.HasUnsupportedRichDisplaySelection().Should().BeFalse();

            switch (action)
            {
                case "delete": view.DeleteForwardPublic(); break;
                case "backspace": view.BackspacePublic(); break;
                case "typing": view.InsertText("X"); break;
                case "paste": view.PastePlainText("X").Should().BeTrue(); break;
            }

            var expected = action is "typing" or "paste" ? "AXYZ" : "AYZ";
            cell.Paragraphs.Should().ContainSingle();
            var result = cell.Paragraphs[0];
            result.PlainText.Should().Be(expected);
            result.BookmarkNames.Should().Equal("First", "Middle", "Last");
            result.StyleId.Should().Be("StartStyle");
            result.SectionBreak.Should().BeSameAs(section);
            result.PreservedNumbering.Should().Be(new PreservedNumbering(7, 2));
            result.ParagraphFormatRevision!.Author.Should().Be("Review author");
            result.Runs[0].Formatting.Bold.Should().BeTrue();
            result.Runs.Should().Contain(run => run.Text.Contains('Y') && run.StyleId == "SuffixCharacter");
            view.Document.Blocks[1].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("body untouched");

            view.Undo();
            cell.Paragraphs.Should().HaveCount(3);
            cell.PlainText.Should().Be("ABC\ndeleted middle\nXYZ");
            cell.Paragraphs.SelectMany(paragraph => paragraph.BookmarkNames).Should().Equal("First", "Middle", "Last");
            cell.Paragraphs[0].SectionBreak.Should().BeSameAs(section);
            view.Redo();
            cell.PlainText.Should().Be(expected);
            cell.Paragraphs.Single().BookmarkNames.Should().Equal("First", "Middle", "Last");
        }, CancellationToken.None);

    [Theory]
    [InlineData("field")]
    [InlineData("zero-width")]
    [InlineData("boundaries")]
    [InlineData("crlf")]
    [InlineData("table-formula")]
    [InlineData("cross-reference")]
    [InlineData("custom-xml")]
    public Task UnsafeLaterParagraphMetadataStillRefusesTheWholeSelection(string kind) => Session.Dispatch(() =>
    {
        var first = new Paragraph("AB");
        first.BookmarkNames.Add("KeepFirst");
        var last = new Paragraph("CD");
        if (kind == "field") last.Runs.Insert(0, Run.DateField());
        if (kind == "zero-width") last.Runs.Insert(0, new Run(""));
        if (kind == "boundaries")
        {
            last.BookmarkBoundaries.Add(new("pair", BookmarkBoundaryKind.Start, 0, "Later"));
            last.BookmarkBoundaries.Add(new("pair", BookmarkBoundaryKind.End, 1));
        }
        if (kind == "crlf") last.Runs[0].Text = "C\r\nD";
        if (kind == "table-formula") last.Runs[0].TableFormula = new TableFormulaField("=SUM(ABOVE)");
        if (kind == "cross-reference") last.Runs[0].CrossReference =
            new CrossReferenceField(CrossRefFieldKind.Ref, "target", CrossRefInsertAs.Text, false);
        if (kind == "custom-xml") last.BlockCustomXml = new BlockCustomXml("region", "urn:freew:test");
        var (view, cell) = View(first, last);
        var before = cell.PlainText;
        Select(view, 0, 1, 1, 1, false);

        view.HasUnsupportedRichDisplaySelection().Should().BeTrue();
        view.DeleteForwardPublic();

        cell.PlainText.Should().Be(before);
        cell.Paragraphs.Should().HaveCount(2);
        first.BookmarkNames.Should().Equal("KeepFirst");
        view.CanUndo.Should().BeFalse();
        if (kind == "field") last.Runs[0].FieldKind.Should().Be(RunFieldKind.Date);
        if (kind == "boundaries") last.BookmarkBoundaries.Should().HaveCount(2);
        if (kind == "table-formula") last.Runs[0].TableFormula.Should().NotBeNull();
        if (kind == "cross-reference") last.Runs[0].CrossReference.Should().NotBeNull();
        if (kind == "custom-xml")
        {
            cell.Paragraphs[1].Should().BeSameAs(last);
            last.BlockCustomXml.Should().Be(new BlockCustomXml("region", "urn:freew:test"));
        }
    }, CancellationToken.None);

    [Theory]
    [InlineData("a😀b", 2)]
    [InlineData("ae\u0301b", 2)]
    public Task StructuralMergeDoesNotSplitAUnicodeTextElement(string text, int offset) => Session.Dispatch(() =>
    {
        var first = new Paragraph(text);
        first.BookmarkNames.Add("Keep");
        var (view, cell) = View(first, new Paragraph("CD"));
        Select(view, 0, offset, 1, 1, false);

        view.DeleteForwardPublic();

        cell.PlainText.Should().Be(text + "\nCD");
        cell.Paragraphs.Should().HaveCount(2);
        view.CanUndo.Should().BeFalse();
    }, CancellationToken.None);

    private static (DocumentView View, TableCell Cell) View(params Paragraph[] paragraphs)
    {
        var table = Table.Create(1, 1);
        var cell = table.Rows[0].Cells[0];
        cell.Paragraphs.Clear();
        cell.Paragraphs.AddRange(paragraphs);
        var document = new TextDocument();
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("body untouched"));
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(900, 4000));
        return (view, cell);
    }

    private static void Select(DocumentView view, int firstParagraph, int firstOffset, int lastParagraph, int lastOffset, bool reversed)
    {
        view.PlaceCaretInCell(0, 0, 0, reversed ? firstParagraph : lastParagraph, reversed ? firstOffset : lastOffset);
        view.SetCellSelectionAnchorForTest(0, 0, 0, reversed ? lastParagraph : firstParagraph, reversed ? lastOffset : firstOffset);
    }
}
