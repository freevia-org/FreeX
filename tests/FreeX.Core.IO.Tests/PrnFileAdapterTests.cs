using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static FreeX.Core.IO.Tests.TextFileAdapterTestHelper;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for <see cref="PrnFileAdapter"/> — Excel's "Formatted Text (Space delimited)" format.
/// </summary>
public sealed class PrnFileAdapterTests
{
    // -----------------------------------------------------------------------
    // Adapter metadata
    // -----------------------------------------------------------------------

    [Fact]
    public void Adapter_HasCorrectExtension()
    {
        var adapter = new PrnFileAdapter();
        adapter.Extension.Should().Be(".prn");
    }

    [Fact]
    public void Adapter_HasCorrectFormatName()
    {
        var adapter = new PrnFileAdapter();
        adapter.FormatName.Should().Be("Formatted Text (Space delimited)");
    }

    [Fact]
    public void Adapter_CanOpenAndSave()
    {
        var adapter = new PrnFileAdapter();
        var format = adapter.Formats.Single();
        format.CanOpen.Should().BeTrue();
        format.CanSave.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Save — fixed-width layout
    // -----------------------------------------------------------------------

    [Fact]
    public void Save_ProducesFixedWidthColumnsWithSpaceSeparator()
    {
        // Arrange: a small workbook with mixed text/number cells
        // Row 1: "Name"  "Value"
        // Row 2: "Alice" 42
        // Row 3: "Bob"   1234
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1234));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        // Col 1 width = max("Name"=4, "Alice"=5, "Bob"=3) = 5 (left-aligned text)
        // Col 2 width = max("Value"=5, "42"=2, "1234"=4) = 5 (mixed — has text header, so left-aligned)
        // Actually: col 2 has a text cell ("Value") which makes it left-aligned.
        // Let's check the structure: row separator = " " (1 space between cols), CRLF endings.

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);

        // No line should have trailing spaces (Excel .prn trims them)
        foreach (var line in lines)
            line.Should().NotEndWith(" ", "trailing spaces must be trimmed");

        // Each line must have exactly one space separating the two columns
        // Row 1: "Name  Value" (col1=5 left, col2=5 left, separator=1 space)
        lines[0].Should().StartWith("Name");
        lines[1].Should().StartWith("Alice");
        lines[2].Should().StartWith("Bob");
    }

    [Fact]
    public void Save_NumberColumnsAreRightAligned()
    {
        // A column with only numbers should be right-aligned
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1000));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        // Col 1 width = max("1"=1, "1000"=4) = 4, right-aligned
        // Row 1: "   1"  (3 leading spaces)
        // Row 2: "1000"
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[0].Should().Be("   1");
        lines[1].Should().Be("1000");
    }

    [Fact]
    public void Save_TextColumnsAreLeftAligned()
    {
        // A column with text values should be left-aligned
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hi"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(99));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Hello World"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        // Col 1: text, width=11 ("Hello World"), left-aligned
        // Col 2: numbers only, width=2, right-aligned
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        // Row 1: "Hi" padded to 11 + " " + "99" right-aligned to 2 = "Hi          99"
        // After trailing-space trim: "Hi          99" (no trailing spaces since number is last)
        lines[0].Should().Be("Hi          99");
        // Row 2: "Hello World" + " " + " 1" (right-aligned in width 2)
        lines[1].Should().Be("Hello World  1");
    }

    [Fact]
    public void Save_NoTrailingSpacesOnAnyLine()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("BB"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("CCC"));
        // Row 2 col 2 is empty, which would leave trailing spaces if not trimmed

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        foreach (var line in text.Split("\r\n"))
        {
            if (line.Length > 0)
                line.Should().NotEndWith(" ", $"line '{line}' must have no trailing spaces");
        }
    }

    [Fact]
    public void Save_UsesCrlfLineEndings()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Should().Contain("\r\n", "PRN files use CRLF line endings");
        text.Should().NotContain("\n\r");

        // Count CRLF occurrences — must equal number of rows
        var crlfCount = text.Split("\r\n").Length - 1;
        crlfCount.Should().Be(2);
    }

    [Fact]
    public void Save_EmptyWorkbookProducesEmptyOutput()
    {
        var workbook = new Workbook("Empty");
        workbook.AddSheet("Sheet1");

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Should().BeEmpty();
    }

    [Fact]
    public void Save_WorkbookWithNoSheetsProducesEmptyOutput()
    {
        var workbook = new Workbook("NoSheets");

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Should().BeEmpty();
    }

    [Fact]
    public void Save_OnlyFirstSheetIsExported()
    {
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("from-sheet1"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("from-sheet2"));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Should().Contain("from-sheet1");
        text.Should().NotContain("from-sheet2");
    }

    [Fact]
    public void Save_DateCellFormattedAsIsoDate()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        var date = new DateTime(2024, 3, 15);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(date));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        text.Should().Contain("2024-03-15");
    }

    [Fact]
    public void Save_BoolCellsFormattedAsTrueFalse()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(false));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Trim().Should().Be("TRUE");
        lines[1].Trim().Should().Be("FALSE");
    }

    // -----------------------------------------------------------------------
    // Save — sparse used-range (must not materialize a dense rowCount*colCount matrix)
    // -----------------------------------------------------------------------

    [Fact]
    public void Save_SparseCornersOfFullSheetDoNotOverflowOrOom()
    {
        // A value in A1 and another in the very last cell (XFD1048576) gives a used-range
        // bounding box of 1,048,576 rows x 16,384 cols — 17+ billion cells. The writer must
        // stream line-by-line from the sparse cell set instead of allocating a dense
        // string?[rowCount, colCount] matrix sized to the bounding box (that allocation
        // either throws immediately — "Array dimensions exceeded supported range" — or OOMs).
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("top-left"));
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol), new TextValue("bottom-right"));

        var adapter = new PrnFileAdapter();

        // Act: must complete without throwing (OutOfMemoryException / overflow) and without
        // materializing a matrix anywhere near 17 billion elements.
        var text = SaveToUtf8Text(adapter, workbook);

        var lines = text.Split("\r\n");
        // Trailing split artifact from the final CRLF.
        lines.Should().HaveCount((int)CellAddress.MaxRow + 1);
        lines[^1].Should().BeEmpty();

        // Row 1 contains only "top-left" — nothing else in that row, so the line is exactly
        // that text (trailing padding on later empty columns is all-space and gets trimmed).
        lines[0].Should().Be("top-left");

        // A row in between with no content at all is a fully blank (trimmed) line.
        lines[500_000].Should().BeEmpty();

        // The last row contains only "bottom-right" in the last column — everything before it
        // on that row is blank padding/separators (leading spaces are NOT trimmed, only
        // trailing), so the line ends with the value preceded solely by spaces.
        var lastLine = lines[(int)CellAddress.MaxRow - 1];
        lastLine.Should().EndWith("bottom-right");
        lastLine.TrimStart().Should().Be("bottom-right");
    }

    [Fact]
    public void Save_NormalDenseSheetOutputUnchangedAfterSparseFix()
    {
        // No-regression sibling: a normal small/dense sheet (every used cell populated) must
        // still produce byte-identical output to before the sparse-storage rewrite.
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1234));

        var adapter = new PrnFileAdapter();
        var text = SaveToUtf8Text(adapter, workbook);

        // Col 1 ("Name"/"Alice"/"Bob") width=5, left-aligned.
        // Col 2 ("Value"/42/1234) width=5, left-aligned (contains a text cell, so the whole
        // column is left-aligned even though 42/1234 are numbers) — trailing padding on the
        // last column of each row is trimmed away.
        text.Should().Be("Name  Value\r\nAlice 42\r\nBob   1234\r\n");
    }

    // -----------------------------------------------------------------------
    // Round-trip: save then open
    // -----------------------------------------------------------------------

    [Fact]
    public void RoundTrip_SimpleTextCellsRoundTripCorrectly()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("World"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Bar"));

        var adapter = new PrnFileAdapter();
        var loaded = SaveAndLoad(adapter, workbook);
        var loadedSheet = loaded.Sheets.Single();

        loadedSheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("Hello"));
        loadedSheet.GetCell(1, 2)?.Value.Should().Be(new TextValue("World"));
        loadedSheet.GetCell(2, 1)?.Value.Should().Be(new TextValue("Foo"));
        loadedSheet.GetCell(2, 2)?.Value.Should().Be(new TextValue("Bar"));
    }

    [Fact]
    public void RoundTrip_IntegerNumberCellsRoundTripAsNumbers()
    {
        // Numbers with no ambiguity (no surrounding whitespace issues)
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(-7));

        var adapter = new PrnFileAdapter();
        var loaded = SaveAndLoad(adapter, workbook);
        var loadedSheet = loaded.Sheets.Single();

        loadedSheet.GetCell(1, 1)?.Value.Should().Be(new NumberValue(42));
        loadedSheet.GetCell(2, 1)?.Value.Should().Be(new NumberValue(-7));
    }

    [Fact]
    public void RoundTrip_SingleColumnOfNumbersRoundTripsUnambiguously()
    {
        // A single number column has no adjacent-column alignment ambiguity
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(300));

        var adapter = new PrnFileAdapter();
        var loaded = SaveAndLoad(adapter, workbook);
        var loadedSheet = loaded.Sheets.Single();

        loadedSheet.GetCell(1, 1)?.Value.Should().Be(new NumberValue(100));
        loadedSheet.GetCell(2, 1)?.Value.Should().Be(new NumberValue(200));
        loadedSheet.GetCell(3, 1)?.Value.Should().Be(new NumberValue(300));
    }

    // -----------------------------------------------------------------------
    // Load (open) semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Load_ParsesSpaceDelimitedLine()
    {
        var prn = "hello world\r\n";
        var bytes = Encoding.UTF8.GetBytes(prn);
        var adapter = new PrnFileAdapter();
        using var stream = new MemoryStream(bytes);
        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("hello"));
        sheet.GetCell(1, 2)?.Value.Should().Be(new TextValue("world"));
    }

    [Fact]
    public void Load_CollapseMultipleSpaces()
    {
        // Multiple spaces between tokens should be collapsed into a single separator
        var prn = "a    b     c\r\n";
        var bytes = Encoding.UTF8.GetBytes(prn);
        var adapter = new PrnFileAdapter();
        using var stream = new MemoryStream(bytes);
        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("a"));
        sheet.GetCell(1, 2)?.Value.Should().Be(new TextValue("b"));
        sheet.GetCell(1, 3)?.Value.Should().Be(new TextValue("c"));
    }

    [Fact]
    public void Load_ParsesNumericTokensAsNumbers()
    {
        var prn = "42 3.14\r\n";
        var bytes = Encoding.UTF8.GetBytes(prn);
        var adapter = new PrnFileAdapter();
        using var stream = new MemoryStream(bytes);
        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetCell(1, 1)?.Value.Should().Be(new NumberValue(42));
        sheet.GetCell(1, 2)?.Value.Should().Be(new NumberValue(3.14));
    }

    [Fact]
    public void Load_ParsesMultipleRows()
    {
        var prn = "row1col1 row1col2\r\nrow2col1 row2col2\r\n";
        var bytes = Encoding.UTF8.GetBytes(prn);
        var adapter = new PrnFileAdapter();
        using var stream = new MemoryStream(bytes);
        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("row1col1"));
        sheet.GetCell(1, 2)?.Value.Should().Be(new TextValue("row1col2"));
        sheet.GetCell(2, 1)?.Value.Should().Be(new TextValue("row2col1"));
        sheet.GetCell(2, 2)?.Value.Should().Be(new TextValue("row2col2"));
    }

    [Fact]
    public void Load_EmptyStreamProducesEmptySheet()
    {
        var bytes = Array.Empty<byte>();
        var adapter = new PrnFileAdapter();
        using var stream = new MemoryStream(bytes);
        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.CellCount.Should().Be(0);
    }
}
