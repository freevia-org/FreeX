using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-16 targeted regression tests for the ODS adapter security/correctness findings:
/// <list type="bullet">
///   <item>R16-security-import-1: a crafted row-repeat x column-repeat product must not force
///   materialization (or a proportional iteration cost) of the full product.</item>
///   <item>R16-security-import-3: a spanned-merge extent computed from an attacker-controlled
///   number-rows-spanned/columns-spanned must not overflow uint arithmetic.</item>
///   <item>R16-defined-name-scope-routing-2/3: workbook- and sheet-scoped named ranges/formulas must
///   round-trip through ODS save+load with their scope preserved.</item>
///   <item>R16-security-import-2: ODS XML loading must apply the shared hardened reader policy
///   (including MaxCharactersInDocument), not a bespoke unbounded one.</item>
/// </list>
/// </summary>
public sealed class R16_ods_Tests
{
    private static Workbook RoundTrip(Workbook source)
    {
        var adapter = new OdsFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    /// <summary>
    /// Builds a minimal, valid ODS package (a ZIP with just a content.xml part) so the reader-side
    /// vulnerability can be exercised directly with hand-crafted ODF XML, without going through the
    /// FreeX writer (which never emits pathological repeat counts itself).
    /// </summary>
    private static MemoryStream BuildOdsPackage(string contentXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("content.xml", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(contentXml);
        }
        stream.Position = 0;
        return stream;
    }

    private const string ContentXmlHeader =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<office:document-content " +
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "office:version=\"1.2\">" +
        "<office:body><office:spreadsheet>";

    private const string ContentXmlFooter =
        "</office:spreadsheet></office:body></office:document-content>";

    [Fact]
    public void Load_HugeRowRepeatTimesColumnRepeat_DoesNotMaterializeTheProduct()
    {
        // Row 1 carries the only real value in the file. Row block 2 declares
        // number-rows-repeated="1000000" and its single cell declares
        // number-columns-repeated="16384" but is otherwise blank (no value, no style) — the classic
        // "pad to sheet dimensions" pattern. A reader that materializes every repeat instance
        // individually would have to iterate the full 1,000,000 x 16,384 product (~1.7e10) for a
        // content.xml under 1KB — a decompression-bomb style DoS.
        var contentXml = ContentXmlHeader +
            "<table:table table:name=\"Sheet1\">" +
            "<table:table-row>" +
            "<table:table-cell office:value-type=\"float\" office:value=\"42\">" +
            "<text:p>42</text:p></table:table-cell>" +
            "</table:table-row>" +
            "<table:table-row table:number-rows-repeated=\"1000000\">" +
            "<table:table-cell table:number-columns-repeated=\"16384\"/>" +
            "</table:table-row>" +
            "</table:table>" +
            ContentXmlFooter;

        using var stream = BuildOdsPackage(contentXml);

        var stopwatch = Stopwatch.StartNew();
        var workbook = new OdsFileAdapter().Load(stream);
        stopwatch.Stop();

        // Bounded by the used extent (one real cell), not the declared row*column product: a
        // reader that fell back to materializing every repeat instance would take vastly longer
        // than this, even without storing anything for the blank instances.
        // DefaultTests runs all project lanes concurrently; allow scheduler contention while still
        // staying orders of magnitude below materializing the 1.7e10 declared cell product.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));

        var sheet = workbook.Sheets.Single();
        sheet.CellCount.Should().Be(1);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Load_HugeRowsSpannedAttribute_ClampsMergeExtentWithoutOverflow()
    {
        // number-rows-spanned is attacker-controlled and unclamped by ReadRepeat; picked so that
        // row(10) + rowsSpanned - 1, computed in 32-bit arithmetic, wraps around to a small number
        // (3) instead of the intended "spans to the bottom of the sheet".
        const uint hugeRowsSpanned = 4_294_967_290; // uint.MaxValue - 5
        var contentXml = ContentXmlHeader +
            "<table:table table:name=\"Sheet1\">" +
            "<table:table-row table:number-rows-repeated=\"9\"><table:table-cell/></table:table-row>" +
            "<table:table-row><table:table-cell table:number-rows-spanned=\"" + hugeRowsSpanned + "\" " +
            "office:value-type=\"string\"><text:p>anchor</text:p></table:table-cell></table:table-row>" +
            "</table:table>" +
            ContentXmlFooter;

        using var stream = BuildOdsPackage(contentXml);
        var workbook = new OdsFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.MergedRegions.Should().ContainSingle();
        var merge = sheet.MergedRegions.Single();

        // Must clamp to the sheet's max row, not silently wrap into a corrupt tiny/inverted region.
        merge.Start.Row.Should().Be(10u);
        merge.End.Row.Should().Be(CellAddress.MaxRow);
    }

    [Fact]
    public void RoundTrips_WorkbookAndSheetScopedNamedRangeAndNamedFormula()
    {
        var wb = new Workbook("Untitled");
        var s1 = wb.AddSheet("Sheet1");
        var s2 = wb.AddSheet("Sheet2");
        s1.SetCell(new CellAddress(s1.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        s2.SetCell(new CellAddress(s2.Id, 1, 1), Cell.FromValue(new NumberValue(7)));

        // Workbook-scoped named range + named formula.
        wb.DefineNamedRange("GlobalRange", new GridRange(
            new CellAddress(s1.Id, 1, 1), new CellAddress(s1.Id, 2, 2)));
        wb.NamedFormulas["GlobalFormula"] = "1+2";

        // Sheet-scoped (Sheet2-local) named range + named formula, same name pattern proves the two
        // scopes don't collide.
        wb.DefineNamedRange("LocalRange", new GridRange(
            new CellAddress(s2.Id, 1, 1), new CellAddress(s2.Id, 1, 1)), metadata: null, s2.Id);
        wb.DefineNamedFormula("LocalFormula", "3*4", s2.Id);

        var got = RoundTrip(wb);
        var gotS2 = got.GetSheet("Sheet2")!;

        got.NamedRanges.Should().ContainKey("GlobalRange");
        var globalRange = got.NamedRanges["GlobalRange"];
        (globalRange.Start.Row, globalRange.Start.Col, globalRange.End.Row, globalRange.End.Col)
            .Should().Be((1u, 1u, 2u, 2u));
        got.NamedFormulas.Should().ContainKey("GlobalFormula");
        got.NamedFormulas["GlobalFormula"].Should().Be("1+2");

        got.ScopedNamedRanges.Should().ContainKey(("LocalRange", gotS2.Id));
        got.ScopedNamedFormulas.Should().ContainKey(("LocalFormula", gotS2.Id));
        got.ScopedNamedFormulas[("LocalFormula", gotS2.Id)].Should().Be("3*4");

        // Sheet-scoped names must not leak into workbook scope.
        got.NamedRanges.Should().NotContainKey("LocalRange");
        got.NamedFormulas.Should().NotContainKey("LocalFormula");
    }

    [Fact]
    public void Load_AppliesSharedSecureXmlReaderPolicy_RejectsOversizedContentXml()
    {
        // A content.xml that exceeds the shared SecureXmlReaderSettings.DefaultMaxCharactersInDocument
        // ceiling must be rejected, matching every other package-based adapter (XLSX/DOCX/ODT). Pad
        // with an oversized comment so the character count blows just past the 64MB default ceiling
        // while keeping the document otherwise well-formed and the allocation as small as possible.
        var padding = new string('x', 64 * 1024 * 1024 + 1024);
        var contentXml = ContentXmlHeader +
            "<!--" + padding + "-->" +
            "<table:table table:name=\"Sheet1\"><table:table-row><table:table-cell/></table:table-row></table:table>" +
            ContentXmlFooter;

        using var stream = BuildOdsPackage(contentXml);

        Assert.Throws<System.Xml.XmlException>(() => new OdsFileAdapter().Load(stream));
    }
}
