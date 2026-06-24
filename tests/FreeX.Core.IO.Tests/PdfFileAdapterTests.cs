using System.IO;
using System.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the read-only PDF table import adapter. PDFs are synthesized in-test with PdfPig's
/// <see cref="PdfDocumentBuilder"/> so no binary fixtures are checked in — mirrors FreeW.Core.IO.Tests.
/// </summary>
public sealed class PdfFileAdapterTests
{
    // ── Descriptor / capability ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Capabilities_AreOpenOnly()
    {
        var adapter = new PdfFileAdapter();

        adapter.Extension.Should().Be(".pdf");
        adapter.FormatName.Should().Be("PDF Document");

        var format = adapter.Formats.Should().ContainSingle().Subject;
        format.Extension.Should().Be(".pdf");
        format.CanOpen.Should().BeTrue();
        format.CanSave.Should().BeFalse();
        format.OpensAsTemplate.Should().BeFalse();
    }

    [Fact]
    public void Save_Throws_NotSupported()
    {
        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream();

        var act = () => adapter.Save(new Workbook("x"), stream);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Save As .xlsx*");
    }

    // ── Single-page grid import ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A 3×3 grid (header row + 2 data rows) with three clearly-separated X bands. The adapter must
    /// recover the grid and coerce the numeric cells to <see cref="NumberValue"/> (not text).
    /// </summary>
    [Fact]
    public void Load_SinglePage_ThreeColumnGrid_RecoversCellsAndCoercesNumbers()
    {
        // Build a page with three X-band columns at x=50, x=200, x=350 — well-separated (150 pt apart).
        //   Row 1 (y=700): "Name"        "Age"       "Score"
        //   Row 2 (y=660): "Alice"       "30"        "95.5"
        //   Row 3 (y=620): "Bob"         "25"        "87"
        var pdfBytes = BuildGridPdf(
        [
            new CellSpec("Name",  50, 700), new CellSpec("Age",   200, 700), new CellSpec("Score", 350, 700),
            new CellSpec("Alice", 50, 660), new CellSpec("30",    200, 660), new CellSpec("95.5",  350, 660),
            new CellSpec("Bob",   50, 620), new CellSpec("25",    200, 620), new CellSpec("87",    350, 620),
        ]);

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        var workbook = adapter.Load(stream);

        workbook.Sheets.Should().HaveCount(1);
        var sheet = workbook.Sheets[0];
        sheet.Name.Should().Be("Page 1");

        // Header row — text values must appear somewhere in the first row
        var row1Strings = AllRowValues(sheet, 1)
            .OfType<TextValue>()
            .Select(tv => tv.Value)
            .ToList();
        row1Strings.Should().Contain(s => s.Contains("Name"));
        row1Strings.Should().Contain(s => s.Contains("Age"));
        row1Strings.Should().Contain(s => s.Contains("Score"));

        // Numeric cells must coerce to NumberValue (not TextValue)
        var allNumbers = AllCellValues(sheet).OfType<NumberValue>().Select(n => n.Value).ToList();
        allNumbers.Should().Contain(v => Math.Abs(v - 30) < 0.001, "Age=30 should coerce to NumberValue");
        allNumbers.Should().Contain(v => Math.Abs(v - 95.5) < 0.001, "Score=95.5 should coerce to NumberValue");
        allNumbers.Should().Contain(v => Math.Abs(v - 25) < 0.001, "Age=25 should coerce to NumberValue");
        allNumbers.Should().Contain(v => Math.Abs(v - 87) < 0.001, "Score=87 should coerce to NumberValue");
    }

    // ── Regression: tight mixed-alignment boundary must not merge columns ──────────────────────────────

    /// <summary>
    /// Regression for the column-merge defect found by Excel→PDF round-trip validation: a right-aligned
    /// number column abutting a left-aligned text column produces only a cell-padding-wide gap, which the
    /// old whitespace-gutter histogram (≥6 pt empty span) merged into one column. The whitespace-vote
    /// detector keys on the gap's recurring X position instead, so the columns stay separate. Here the
    /// number tokens end near x≈107 and the text tokens start at x=112 — a ~5 pt gap, narrower than the old
    /// minimum gutter — repeated across every row. The number and the text must land in DIFFERENT cells.
    /// </summary>
    [Fact]
    public void Load_TightBoundary_RightNumberThenLeftText_DoesNotMergeColumns()
    {
        var pdfBytes = BuildGridPdf(
        [
            new CellSpec("1", 100, 700), new CellSpec("Item", 112, 700),
            new CellSpec("2", 100, 680), new CellSpec("Item", 112, 680),
            new CellSpec("3", 100, 660), new CellSpec("Item", 112, 660),
            new CellSpec("4", 100, 640), new CellSpec("Item", 112, 640),
        ]);

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        var sheet = adapter.Load(stream).Sheets[0];

        // The number must coerce to a NumberValue in its own cell, and "Item" must be a standalone
        // TextValue — never a merged "1 Item" text cell.
        var values = AllCellValues(sheet).ToList();
        values.OfType<NumberValue>().Should().Contain(v => Math.Abs(v.Value - 1) < 0.001);
        values.OfType<TextValue>().Should().Contain(t => t.Value == "Item");
        values.OfType<TextValue>().Should().NotContain(t => t.Value.Contains(" "),
            "the right-aligned number and left-aligned text must not be merged into one cell");
    }

    // ── Regression: ISO date must not be timezone-shifted ──────────────────────────────────────────────

    /// <summary>
    /// Regression for the date-coercion timezone defect found by round-trip validation: a plain ISO date
    /// "2026-01-01" was parsed through <c>DateTimeOffset…UtcDateTime</c>, which injected the host's local
    /// offset and shifted the value (e.g. to 2025-12-31 22:00 on a UTC+2 machine). Offset-less ISO dates
    /// must round-trip as wall-clock with no shift and no spurious time component.
    /// </summary>
    [Fact]
    public void Load_IsoDate_NoTimezoneShift()
    {
        var pdfBytes = BuildGridPdf([new CellSpec("2026-01-01", 50, 700)]);

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        var sheet = adapter.Load(stream).Sheets[0];

        var dates = AllCellValues(sheet).OfType<DateTimeValue>().Select(d => d.ToDateTime()).ToList();
        dates.Should().ContainSingle().Which.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0));
    }

    // ── Multi-page PDF ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_MultiPage_ProducesOneSheetPerPage_WithCorrectNames()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page1 = builder.AddPage(PageSize.A4);
        page1.AddText("SheetOne", 12, new PdfPoint(50, 700), font);

        var page2 = builder.AddPage(PageSize.A4);
        page2.AddText("SheetTwo", 12, new PdfPoint(50, 700), font);

        var pdfBytes = builder.Build();

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        var workbook = adapter.Load(stream);

        workbook.Sheets.Should().HaveCount(2);
        workbook.Sheets[0].Name.Should().Be("Page 1");
        workbook.Sheets[1].Name.Should().Be("Page 2");

        // Page 1 should contain "SheetOne" somewhere
        AllCellStrings(workbook.Sheets[0]).Should().Contain(s => s.Contains("SheetOne"));

        // Page 2 should contain "SheetTwo" somewhere
        AllCellStrings(workbook.Sheets[1]).Should().Contain(s => s.Contains("SheetTwo"));
    }

    // ── Textless / image-only page ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_TextlessPage_ProducesEmptySheetWithoutCrashing()
    {
        // Build a page with no text whatsoever (blank page geometry only).
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4); // no text added
        var pdfBytes = builder.Build();

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        Workbook? workbook = null;

        var act = () => { workbook = adapter.Load(stream); };
        act.Should().NotThrow();

        // Must yield at least one sheet (even if blank).
        workbook!.Sheets.Should().NotBeEmpty();
        workbook.Sheets[0].Name.Should().Be("Page 1");
    }

    // ── Stream ownership ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_DoesNotDisposeCallerStream()
    {
        var pdfBytes = BuildSimplePdf("Stream stays open");

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(pdfBytes);
        adapter.Load(stream);

        // Adapter contract: the caller owns the stream. Accessing Length after Load proves it is not disposed.
        var act = () => _ = stream.Length;
        act.Should().NotThrow();
    }

    // ── Malformed input ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_MalformedBytes_ThrowsInvalidDataException()
    {
        var garbage = "This is not a PDF"u8.ToArray();

        var adapter = new PdfFileAdapter();
        using var stream = new MemoryStream(garbage);

        var act = () => adapter.Load(stream);

        // Must surface a clean, user-meaningful exception — not a raw library type leaking through.
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*PDF*");
    }

    // ── Value coercion (unit tests of the coercion helper directly) ──────────────────────────────────

    [Theory]
    [InlineData("TRUE",  typeof(BoolValue))]
    [InlineData("false", typeof(BoolValue))]
    [InlineData("42",    typeof(NumberValue))]
    [InlineData("3.14",  typeof(NumberValue))]
    [InlineData("50%",   typeof(NumberValue))]
    [InlineData("hello", typeof(TextValue))]
    public void CoerceValue_ReturnsCorrectType(string input, Type expectedType)
    {
        var result = PdfTableReader.CoerceValue(input);
        result.Should().BeOfType(expectedType);
    }

    [Fact]
    public void CoerceValue_Number42_IsExactly42()
    {
        var result = PdfTableReader.CoerceValue("42");
        result.Should().BeOfType<NumberValue>().Which.Value.Should().Be(42.0);
    }

    [Fact]
    public void CoerceValue_Percentage50_IsPointFive()
    {
        var result = PdfTableReader.CoerceValue("50%");
        result.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(0.5, 1e-9);
    }

    // ── Helper: PDF synthesis ────────────────────────────────────────────────────────────────────────

    private sealed record CellSpec(string Text, float X, float Y);

    /// <summary>Builds a one-page PDF where each <see cref="CellSpec"/> is a separate text chunk.</summary>
    private static byte[] BuildGridPdf(IEnumerable<CellSpec> cells)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);

        foreach (var cell in cells)
            page.AddText(cell.Text, 12, new PdfPoint(cell.X, cell.Y), font);

        return builder.Build();
    }

    /// <summary>Builds a one-page PDF with a single line of text.</summary>
    private static byte[] BuildSimplePdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(50, 700), font);
        return builder.Build();
    }

    // ── Helper: cell accessors ───────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<ScalarValue> AllRowValues(Sheet sheet, uint row)
    {
        return sheet.EnumerateCells()
            .Where(t => t.Address.Row == row)
            .Select(t => t.Cell.Value)
            .ToList();
    }

    private static IReadOnlyList<ScalarValue> AllCellValues(Sheet sheet)
    {
        return sheet.EnumerateCells().Select(t => t.Cell.Value).ToList();
    }

    private static List<string> AllCellStrings(Sheet sheet)
    {
        var results = new List<string>();
        foreach (var (_, cell) in sheet.EnumerateCells())
        {
            if (cell.Value is TextValue tv)
                results.Add(tv.Value);
            else if (cell.Value is not BlankValue and not null)
                results.Add(cell.Value.ToString() ?? "");
        }
        return results;
    }
}
