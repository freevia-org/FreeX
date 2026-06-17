namespace FreeW.Core.Model.Tests;

public class SortConvertTests
{
    // --- ParagraphSort.Sort ---

    [Fact]
    public void Sort_Ascending_OrdersByPlainText()
    {
        var paragraphs = new[]
        {
            new Paragraph("Charlie"),
            new Paragraph("alpha"),
            new Paragraph("Bravo"),
        };

        var sorted = ParagraphSort.Sort(paragraphs, ascending: true, caseSensitive: false);

        sorted.Select(p => p.PlainText).Should().Equal("alpha", "Bravo", "Charlie");
        // The input is never mutated — the original array keeps its order.
        paragraphs.Select(p => p.PlainText).Should().Equal("Charlie", "alpha", "Bravo");
    }

    [Fact]
    public void Sort_Descending_ReversesOrder()
    {
        var paragraphs = new[]
        {
            new Paragraph("alpha"),
            new Paragraph("Charlie"),
            new Paragraph("Bravo"),
        };

        var sorted = ParagraphSort.Sort(paragraphs, ascending: false, caseSensitive: false);

        sorted.Select(p => p.PlainText).Should().Equal("Charlie", "Bravo", "alpha");
    }

    [Fact]
    public void Sort_ReturnsSameInstances()
    {
        var a = new Paragraph("a");
        var b = new Paragraph("b");
        var sorted = ParagraphSort.Sort(new[] { b, a }, ascending: true, caseSensitive: false);

        sorted[0].Should().BeSameAs(a);
        sorted[1].Should().BeSameAs(b);
    }

    [Fact]
    public void Sort_CaseInsensitive_TreatsCaseAlike_AndIsStable()
    {
        // Two keys differing only in case must keep their original relative order (stable sort).
        var first = new Paragraph("apple");
        var second = new Paragraph("Apple");
        var paragraphs = new[] { new Paragraph("Banana"), first, second };

        var sorted = ParagraphSort.Sort(paragraphs, ascending: true, caseSensitive: false);

        sorted.Select(p => p.PlainText).Should().Equal("apple", "Apple", "Banana");
        sorted[0].Should().BeSameAs(first);
        sorted[1].Should().BeSameAs(second);
    }

    [Fact]
    public void Sort_CaseSensitive_OrdersByOrdinal()
    {
        // Ordinal: uppercase letters (lower code points) sort before lowercase.
        var paragraphs = new[]
        {
            new Paragraph("banana"),
            new Paragraph("Banana"),
            new Paragraph("Apple"),
        };

        var sorted = ParagraphSort.Sort(paragraphs, ascending: true, caseSensitive: true);

        sorted.Select(p => p.PlainText).Should().Equal("Apple", "Banana", "banana");
    }

    // --- ParagraphSort.SortRows ---

    [Fact]
    public void SortRows_SortsByKeyColumnText()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("3", "Cherry"));
        table.Rows.Add(RowOf("1", "Apple"));
        table.Rows.Add(RowOf("2", "Banana"));

        var sorted = ParagraphSort.SortRows(table.Rows, keyColumn: 1, ascending: true, caseSensitive: false);

        sorted.Select(r => r.Cells[1].PlainText).Should().Equal("Apple", "Banana", "Cherry");
        // The companion (key) column travels with its row.
        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void SortRows_Descending_ReversesOrder()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("Apple"));
        table.Rows.Add(RowOf("Cherry"));
        table.Rows.Add(RowOf("Banana"));

        var sorted = ParagraphSort.SortRows(table.Rows, keyColumn: 0, ascending: false, caseSensitive: false);

        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("Cherry", "Banana", "Apple");
    }

    [Fact]
    public void SortRows_RaggedRow_MissingKeyColumnSortsAsEmpty()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("z"));            // too short to have column 1 -> empty key, sorts first
        table.Rows.Add(RowOf("a", "Banana"));
        table.Rows.Add(RowOf("b", "Apple"));

        var sorted = ParagraphSort.SortRows(table.Rows, keyColumn: 1, ascending: true, caseSensitive: false);

        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("z", "b", "a");
    }

    // --- TextTableConvert.TextToTable ---

    [Fact]
    public void TextToTable_SplitsEachParagraphOnDelimiter()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("a,b,c"));
        doc.Blocks.Add(new Paragraph("d,e,f"));

        var table = TextTableConvert.TextToTable(doc.Blocks.OfType<Paragraph>().ToList(), ',');

        table.RowCount.Should().Be(2);
        table.ColumnCount.Should().Be(3);
        table.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("a", "b", "c");
        table.Rows[1].Cells.Select(c => c.PlainText).Should().Equal("d", "e", "f");
    }

    [Fact]
    public void TextToTable_PadsRaggedRowsToWidestColumnCount()
    {
        var paragraphs = new[]
        {
            new Paragraph("a\tb\tc"),
            new Paragraph("d"),
            new Paragraph("e\tf"),
        };

        var table = TextTableConvert.TextToTable(paragraphs, '\t');

        table.ColumnCount.Should().Be(3);
        // Every row is padded out to three cells; short rows get trailing empties.
        table.Rows.Should().OnlyContain(r => r.Cells.Count == 3);
        table.Rows[1].Cells.Select(c => c.PlainText).Should().Equal("d", string.Empty, string.Empty);
        table.Rows[2].Cells.Select(c => c.PlainText).Should().Equal("e", "f", string.Empty);
    }

    // --- TextTableConvert.TableToText + round-trip ---

    [Fact]
    public void TableToText_JoinsRowCellsWithDelimiter()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("a", "b", "c"));
        table.Rows.Add(RowOf("d", "e", "f"));

        var paragraphs = TextTableConvert.TableToText(table, ',');

        paragraphs.Select(p => p.PlainText).Should().Equal("a,b,c", "d,e,f");
    }

    [Fact]
    public void TextToTable_TableToText_RoundTrips()
    {
        var original = new[]
        {
            new Paragraph("Name,Age,City"),
            new Paragraph("Ada,36,London"),
            new Paragraph("Alan,41,Bletchley"),
        };

        var table = TextTableConvert.TextToTable(original, ',');
        var back = TextTableConvert.TableToText(table, ',');

        back.Select(p => p.PlainText).Should().Equal(original.Select(p => p.PlainText));
    }

    // Build a table row from the given cell texts.
    private static TableRow RowOf(params string[] cells)
    {
        var row = new TableRow();
        foreach (var text in cells)
            row.Cells.Add(new TableCell(text));
        return row;
    }
}
