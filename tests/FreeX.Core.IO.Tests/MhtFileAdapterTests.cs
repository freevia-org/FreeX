using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the MHTML (.mht/.mhtml) "Single File Web Page" adapter.
/// Covers Save (MIME envelope structure), round-trip (save → open → cell values), and output
/// determinism (identical bytes across two saves of the same workbook).
/// </summary>
public sealed class MhtFileAdapterTests
{
    // ---- helpers ---------------------------------------------------------------------------------

    private static Workbook MakeWorkbook(string cellText = "Hello")
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(cellText));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("World"));
        return wb;
    }

    private static byte[] SaveToBytes(Workbook wb)
    {
        using var stream = new MemoryStream();
        new MhtFileAdapter().Save(wb, stream);
        return stream.ToArray();
    }

    private static string SaveToString(Workbook wb) =>
        Encoding.ASCII.GetString(SaveToBytes(wb));

    private static Workbook Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new MhtFileAdapter().Load(stream);
    }

    private static byte[] BuildQuotedPrintableMht(string html) =>
        Encoding.UTF8.GetBytes(string.Join("\r\n",
            "MIME-Version: 1.0",
            "Content-Type: multipart/related; boundary=FreeXBoundary",
            "",
            "--FreeXBoundary",
            "Content-Type: text/html; charset=utf-8",
            "Content-Transfer-Encoding: quoted-printable",
            "",
            html,
            "--FreeXBoundary--",
            ""));

    // ---- descriptor tests ------------------------------------------------------------------------

    [Fact]
    public void Formats_ContainsMhtAndMhtml()
    {
        var adapter = new MhtFileAdapter();

        adapter.Formats.Should().HaveCount(2);
        adapter.Formats.Should().Contain(f => f.Extension == ".mht");
        adapter.Formats.Should().Contain(f => f.Extension == ".mhtml");
    }

    [Fact]
    public void Formats_BothFormatsCanOpenAndSave()
    {
        var adapter = new MhtFileAdapter();

        foreach (var fmt in adapter.Formats)
        {
            fmt.CanOpen.Should().BeTrue($"format {fmt.Extension} should be openable");
            fmt.CanSave.Should().BeTrue($"format {fmt.Extension} should be saveable");
        }
    }

    // ---- Save: MIME envelope structure -----------------------------------------------------------

    [Fact]
    public void Save_OutputStartsWithMimeVersionHeader()
    {
        var mht = SaveToString(MakeWorkbook());

        mht.Should().StartWith("MIME-Version: 1.0");
    }

    [Fact]
    public void Save_OutputContainsMultipartRelatedContentType()
    {
        var mht = SaveToString(MakeWorkbook());

        mht.Should().Contain("Content-Type: multipart/related");
    }

    [Fact]
    public void Save_OutputContainsBoundaryInContentTypeHeader()
    {
        var mht = SaveToString(MakeWorkbook());

        // The boundary parameter must appear in the Content-Type header.
        mht.Should().Contain("boundary=");
    }

    [Fact]
    public void Save_OutputContainsTextHtmlPartHeader()
    {
        var mht = SaveToString(MakeWorkbook());

        mht.Should().Contain("Content-Type: text/html");
    }

    [Fact]
    public void Save_OutputContainsBase64TransferEncoding()
    {
        var mht = SaveToString(MakeWorkbook());

        mht.Should().Contain("Content-Transfer-Encoding: base64");
    }

    [Fact]
    public void Save_DecodedHtmlPartContainsExpectedCellText()
    {
        var wb = MakeWorkbook("UniqueTestCellValue");
        var mht = SaveToString(wb);

        // Extract the base64 payload: everything after the blank line following the part headers.
        // A simple extraction: find the second blank line in the file (after the outer headers
        // and after the part headers) and decode whatever is before the closing boundary.
        string b64Payload = ExtractBase64Payload(mht);
        b64Payload.Should().NotBeNullOrEmpty();

        byte[] decoded = Convert.FromBase64String(b64Payload);
        string html = Encoding.UTF8.GetString(decoded);

        html.Should().Contain("UniqueTestCellValue",
            because: "the cell text must survive base64 encoding into the MHTML part");
    }

    [Fact]
    public void Save_DecodedHtmlPartIsValidHtmlWithTableTag()
    {
        var wb = MakeWorkbook("TableCheck");
        var mht = SaveToString(wb);
        string b64Payload = ExtractBase64Payload(mht);
        byte[] decoded = Convert.FromBase64String(b64Payload);
        string html = Encoding.UTF8.GetString(decoded);

        html.Should().Contain("<table", because: "the HTML writer always emits a table element");
        html.Should().Contain("</table>", because: "the table must be closed");
    }

    [Fact]
    public void Save_OutputEndsWithClosingBoundary()
    {
        var mht = SaveToString(MakeWorkbook());

        // The closing boundary is "--<boundary>--".
        // We just verify the file contains a line that ends with "--".
        mht.Should().MatchRegex(@"--[-=_\w]+--\r?\n?$",
            because: "RFC 2046 requires the final part to end with --boundary--");
    }

    // ---- Round-trip: save → load ----------------------------------------------------------------

    [Fact]
    public void RoundTrip_PreservesTextCellValues()
    {
        var wb = MakeWorkbook("RoundTripText");
        var bytes = SaveToBytes(wb);
        var loaded = Load(bytes);

        var sheet = loaded.Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("RoundTripText"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("World"));
    }

    [Fact]
    public void RoundTrip_PreservesNumericCellValues()
    {
        var wb = MakeWorkbook();
        var bytes = SaveToBytes(wb);
        var loaded = Load(bytes);

        var sheet = loaded.Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void RoundTrip_MultipleRows()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 3; r++)
            for (uint c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, r, c), new TextValue($"R{r}C{c}"));

        var bytes = SaveToBytes(wb);
        var loaded = Load(bytes);
        var ls = loaded.Sheets.Single();

        for (uint r = 1; r <= 3; r++)
            for (uint c = 1; c <= 3; c++)
                ls.GetValue(new CellAddress(ls.Id, r, c))
                    .Should().Be(new TextValue($"R{r}C{c}"), $"cell R{r}C{c} must survive round-trip");
    }

    [Fact]
    public void RoundTrip_EmptyWorkbookDoesNotThrow()
    {
        var wb = new Workbook("Untitled");
        wb.AddSheet("Sheet1");

        var bytes = SaveToBytes(wb);
        var act = () => Load(bytes);

        act.Should().NotThrow();
    }

    [Fact]
    public void Load_QuotedPrintableDecodesHexSoftBreaksAndLiteralSurrogatePairs()
    {
        var html = "<table><tr><td>R=C3=A9su=\r\nm=C3=A9 =E2=82=AC 😀</td></tr></table>";
        var loaded = Load(BuildQuotedPrintableMht(html));
        var sheet = loaded.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(new TextValue("Résumé € 😀"));
    }

    [Fact]
    public void Load_QuotedPrintableLargeLiteralUnicodeHasBoundedAllocation()
    {
        var literal = new string('é', 100_000);
        var mht = BuildQuotedPrintableMht($"<table><tr><td>{literal}</td></tr></table>");
        Load(mht).Sheets.Single().GetValue(1, 1).Should().Be(new TextValue(literal));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var loaded = Load(mht);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        loaded.Sheets.Single().GetValue(1, 1).Should().Be(new TextValue(literal));
        allocatedBytes.Should().BeLessThan(
            4_000_000,
            "literal Unicode should be UTF-8 encoded in spans instead of allocating arrays per character");
        Console.WriteLine($"MHT quoted-printable literal Unicode allocated {allocatedBytes:N0} bytes.");
    }

    [Fact]
    public void Load_QuotedPrintableSourceGuardBatchesLiteralUtf8Encoding()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("MhtFileAdapter.cs");

        source.Should().Contain("AppendUtf8(bytes, body.AsSpan(literalStart, i - literalStart));");
        source.Should().Contain("body.AsSpan(i + 1, 2)");
        source.Should().NotContain("Encoding.UTF8.GetBytes(new[] { c })");
        source.Should().NotContain("body.Substring(i + 1, 2)");
    }

    // ---- Determinism -------------------------------------------------------------------------

    [Fact]
    public void Save_SameWorkbookTwiceProducesIdenticalBytes()
    {
        var wb = MakeWorkbook("DeterminismTest");

        byte[] first = SaveToBytes(wb);
        byte[] second = SaveToBytes(wb);

        first.Should().Equal(second,
            because: "MHTML output must be deterministic (constant boundary, no timestamps)");
    }

    [Fact]
    public void Save_TwoDistinctCallsWithSameWorkbookAreIdentical()
    {
        // Build the same workbook twice independently to ensure nothing carries over.
        byte[] first = SaveToBytes(MakeWorkbook("Det2"));
        byte[] second = SaveToBytes(MakeWorkbook("Det2"));

        first.Should().Equal(second);
    }

    // ---- Helper: extract raw base64 payload from MHTML string -----------------------------------

    /// <summary>
    /// Naïve extraction of the base64 payload from the single text/html MHTML part.
    /// Finds the blank line after the part headers and collects lines until the closing boundary.
    /// </summary>
    private static string ExtractBase64Payload(string mht)
    {
        var lines = mht.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        bool inPart = false;
        bool pastPartHeaders = false;
        var b64Lines = new List<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];

            if (!inPart)
            {
                // Detect start of a MIME part (a line starting with "--" but not "--...--").
                if (line.StartsWith("--", StringComparison.Ordinal) && !line.EndsWith("--", StringComparison.Ordinal))
                    inPart = true;
                continue;
            }

            if (!pastPartHeaders)
            {
                // Blank line signals end of part headers.
                if (line.Length == 0)
                    pastPartHeaders = true;
                continue;
            }

            // Collect body lines until next boundary or EOF.
            if (line.StartsWith("--", StringComparison.Ordinal))
                break;

            b64Lines.Add(line);
        }

        return string.Concat(b64Lines);
    }
}
