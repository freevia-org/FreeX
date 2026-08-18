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

    [Fact]
    public void Sort_Number_OrdersNumerically_NotLexically()
    {
        // Lexical text order would put "10" before "2"; numeric order must not.
        var paragraphs = new[]
        {
            new Paragraph("10"),
            new Paragraph("2"),
            new Paragraph("1"),
        };

        var sorted = ParagraphSort.Sort(
            paragraphs, SortKind.Number, ascending: true, caseSensitive: false, hasHeaderRow: false);

        sorted.Select(p => p.PlainText).Should().Equal("1", "2", "10");
    }

    [Fact]
    public void Sort_Number_UnparseableKeysSortAfterNumbers()
    {
        var paragraphs = new[]
        {
            new Paragraph("apple"),
            new Paragraph("3"),
            new Paragraph("1"),
        };

        var sorted = ParagraphSort.Sort(
            paragraphs, SortKind.Number, ascending: true, caseSensitive: false, hasHeaderRow: false);

        sorted.Select(p => p.PlainText).Should().Equal("1", "3", "apple");
    }

    [Fact]
    public void Sort_Date_OrdersChronologically()
    {
        var paragraphs = new[]
        {
            new Paragraph("2024-12-01"),
            new Paragraph("2024-01-15"),
            new Paragraph("2024-06-30"),
        };

        var sorted = ParagraphSort.Sort(
            paragraphs, SortKind.Date, ascending: true, caseSensitive: false, hasHeaderRow: false);

        sorted.Select(p => p.PlainText).Should().Equal("2024-01-15", "2024-06-30", "2024-12-01");
    }

    [Fact]
    public void Sort_HasHeaderRow_PinsFirstParagraphInPlace()
    {
        var header = new Paragraph("Name");
        var paragraphs = new[]
        {
            header,
            new Paragraph("Charlie"),
            new Paragraph("alpha"),
            new Paragraph("Bravo"),
        };

        var sorted = ParagraphSort.Sort(
            paragraphs, SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: true);

        sorted[0].Should().BeSameAs(header);
        sorted.Select(p => p.PlainText).Should().Equal("Name", "alpha", "Bravo", "Charlie");
    }

    // --- ParagraphSort.SortRows ---

    [Fact]
    public void SortRows_SortsByKeyColumnText()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("3", "Cherry"));
        table.Rows.Add(RowOf("1", "Apple"));
        table.Rows.Add(RowOf("2", "Banana"));

        var sorted = ParagraphSort.SortRows(table.Rows, gridColumn: 1, ascending: true, caseSensitive: false);

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

        var sorted = ParagraphSort.SortRows(table.Rows, gridColumn: 0, ascending: false, caseSensitive: false);

        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("Cherry", "Banana", "Apple");
    }

    [Fact]
    public void SortRows_RaggedRow_MissingKeyColumnSortsAsEmpty()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("z"));            // too short to have column 1 -> empty key, sorts first
        table.Rows.Add(RowOf("a", "Banana"));
        table.Rows.Add(RowOf("b", "Apple"));

        var sorted = ParagraphSort.SortRows(table.Rows, gridColumn: 1, ascending: true, caseSensitive: false);

        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("z", "b", "a");
    }

    [Fact]
    public void SortRows_HasHeaderRow_PinsFirstRowAndSortsBody()
    {
        var table = Table.Create(0, 0);
        var header = RowOf("Rank", "Fruit");
        table.Rows.Add(header);
        table.Rows.Add(RowOf("3", "Cherry"));
        table.Rows.Add(RowOf("1", "Apple"));
        table.Rows.Add(RowOf("2", "Banana"));

        var sorted = ParagraphSort.SortRows(
            table.Rows, gridColumn: 0, SortKind.Number, ascending: true, caseSensitive: false, hasHeaderRow: true);

        sorted[0].Should().BeSameAs(header);
        sorted.Select(r => r.Cells[1].PlainText).Should().Equal("Fruit", "Apple", "Banana", "Cherry");
    }

    [Fact]
    public void SortRows_NumberKind_OrdersNumerically()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("10"));
        table.Rows.Add(RowOf("2"));
        table.Rows.Add(RowOf("1"));

        var sorted = ParagraphSort.SortRows(
            table.Rows, gridColumn: 0, SortKind.Number, ascending: true, caseSensitive: false, hasHeaderRow: false);

        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("1", "2", "10");
    }

    // Regression for sweep83-1: sorting must project every row through its own GridSpan layout, not reuse
    // one row's raw cell-list index as the key column for every row. A row whose leading cell spans two
    // grid columns has one fewer entry in Cells than a row with three plain cells, so a raw index taken
    // from a uniform row reads the wrong cell (or none at all) in the merged row.
    [Fact]
    public void SortRows_RowWithLeadingGridSpanCell_UsesGridProjectedColumnNotRawCellIndex()
    {
        var table = Table.Create(0, 0);
        // Grid column 2 is the sort key for all three rows. "Zulu" is deliberately the alphabetically
        // LAST value (not the first) so this test can't pass by accident: an empty key (the pre-fix bug,
        // reading row.Cells[2] out of range on the 2-cell merged row) sorts FIRST in ascending order,
        // which would put the merged row in the wrong place relative to "Banana"/"Mango" -- a different,
        // detectably wrong order from the correct grid-projected one.
        table.Rows.Add(RowOf("Zebra", "Q", "Mango"));           // 3 plain cells: raw index 2 == grid col 2
        table.Rows.Add(RowOfSpanned(("W", 2), ("Zulu", 1)));    // merged leading cell: raw index 2 is OOB
        table.Rows.Add(RowOf("Kiwi", "R", "Banana"));           // 3 plain cells: raw index 2 == grid col 2

        var sorted = ParagraphSort.SortRows(table.Rows, gridColumn: 2, ascending: true, caseSensitive: false);

        // Correct grid-projected keys sort as Banana, Mango, Zulu. The pre-fix bug read row.Cells[2] on
        // the merged row directly (out of range -> empty key, which sorts before everything), placing
        // that row FIRST instead of last.
        sorted.Select(r => TableGridProjection.At(r, 2)!.Value.Cell.PlainText)
            .Should().Equal("Banana", "Mango", "Zulu");
    }

    // Sibling proof that ordinary uniform-layout tables (the common case, and what every other SortRows
    // test above exercises) are unaffected by projecting per-row instead of indexing row.Cells directly.
    [Fact]
    public void SortRows_UniformRows_StillSortsByPlainCellIndex()
    {
        var table = Table.Create(0, 0);
        table.Rows.Add(RowOf("3", "Cherry"));
        table.Rows.Add(RowOf("1", "Apple"));
        table.Rows.Add(RowOf("2", "Banana"));

        var sorted = ParagraphSort.SortRows(table.Rows, gridColumn: 1, ascending: true, caseSensitive: false);

        sorted.Select(r => r.Cells[1].PlainText).Should().Equal("Apple", "Banana", "Cherry");
        sorted.Select(r => r.Cells[0].PlainText).Should().Equal("1", "2", "3");
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

    // Build a table row from (text, gridSpan) pairs, e.g. a leading merged cell spanning 2 grid columns
    // followed by a plain cell -- fewer Cells entries than the grid is wide, exactly the layout a raw
    // cell-list index misreads.
    private static TableRow RowOfSpanned(params (string Text, int Span)[] cells)
    {
        var row = new TableRow();
        foreach (var (text, span) in cells)
            row.Cells.Add(new TableCell(text) { GridSpan = span });
        return row;
    }
}
