using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip hardening tests for the SpreadsheetML 2003 (.xml) adapter: R1C1↔A1 formula conversion,
/// number-format preservation, whitespace/CR-faithful cell text, and null-URI-safe hyperlinks.
/// </summary>
public sealed partial class SpreadsheetXmlFileAdapterTests
{
    private static Workbook SaveAndReload(Workbook workbook)
    {
        var adapter = new SpreadsheetXmlFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    // ---- R1C1 → A1 on read --------------------------------------------------------------------

    [Theory]
    // Excel writes SpreadsheetML formulas in R1C1; the model expects A1. (formula at cell, expected A1)
    [InlineData(2u, 3u, "=RC[-1]", "B2")]                 // C2 -> one column left = B2
    [InlineData(2u, 3u, "=R[-1]C", "C1")]                 // C2 -> one row up = C1
    [InlineData(5u, 3u, "=R[-1]C+1", "C4+1")]             // operator after a bare-relative ref
    [InlineData(2u, 2u, "=R1C1", "$A$1")]                 // absolute R1C1 -> $A$1
    [InlineData(2u, 2u, "=SUM(R[-1]C:R[-1]C[2])", "SUM(B1:D1)")] // range, mixed offsets at B2
    [InlineData(3u, 3u, "=R[-2]C[-2]*2", "A1*2")]
    public void Load_ConvertsR1C1FormulasToA1(uint row, uint col, string r1c1Formula, string expectedA1)
    {
        var xml = $"""
            <?xml version="1.0"?>
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="S">
                <ss:Table>
                  <ss:Row ss:Index="{row}">
                    <ss:Cell ss:Index="{col}" ss:Formula="{System.Security.SecurityElement.Escape(r1c1Formula)}">
                      <ss:Data ss:Type="Number">0</ss:Data>
                    </ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """;

        using var stream = StreamFromString(xml);
        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.GetCell(row, col)!.FormulaText.Should().Be(expectedA1);
    }

    [Fact]
    public void Load_LeavesA1FormulaUntouched()
    {
        // A FreeX-authored file already stores A1; the R1C1 detector must not mangle it. SUM/IF are not
        // R1C1 refs, and "A1" is a plain A1 reference.
        using var stream = StreamFromString("""
            <?xml version="1.0"?>
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="S">
                <ss:Table>
                  <ss:Row ss:Index="5">
                    <ss:Cell ss:Index="3" ss:Formula="=SUM(A1:A4)+IF(B1&gt;0,1,0)">
                      <ss:Data ss:Type="Number">0</ss:Data>
                    </ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);
        sheet.GetCell(5, 3)!.FormulaText.Should().Be("SUM(A1:A4)+IF(B1>0,1,0)");
    }

    // ---- A1 → R1C1 on write -------------------------------------------------------------------

    [Fact]
    public void Save_WritesFormulasAsR1C1()
    {
        var workbook = new Workbook("R1C1Out");
        var sheet = workbook.AddSheet("S");
        // C2 referencing B2 (one column left, same row) -> RC[-1]
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new Cell { FormulaText = "B2", Value = new NumberValue(0) });
        // C2 referencing absolute $A$1 -> R1C1
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new Cell { FormulaText = "$A$1", Value = new NumberValue(0) });

        using var stream = new MemoryStream();
        new SpreadsheetXmlFileAdapter().Save(workbook, stream);

        var document = LoadSpreadsheetXml(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var formulas = document.Descendants(ss + "Cell")
            .Select(c => c.Attribute(ss + "Formula")?.Value)
            .Where(v => v is not null)
            .ToArray();

        formulas.Should().Contain("=RC[-1]");
        formulas.Should().Contain("=R1C1");
    }

    [Theory]
    [InlineData(2u, 3u, "B2")]
    [InlineData(5u, 3u, "C4+1")]
    [InlineData(2u, 2u, "$A$1")]
    [InlineData(2u, 2u, "SUM(A1:C1)")]
    [InlineData(10u, 10u, "$C$5*J9-1")]
    public void Formula_RoundTripsThroughR1C1(uint row, uint col, string a1Formula)
    {
        var workbook = new Workbook("Roundtrip");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new Cell { FormulaText = a1Formula, Value = new NumberValue(0) });

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);
        reloaded.GetCell(row, col)!.FormulaText.Should().Be(a1Formula);
    }

    // ---- Number-format preservation -----------------------------------------------------------

    [Theory]
    [InlineData("$#,##0.00")]
    [InlineData("0.00%")]
    [InlineData("m/d/yy")]
    [InlineData("mmm-yy")]
    [InlineData("""_("$"* #,##0.00_);_("$"* \(#,##0.00\);_("$"* "-"??_);_(@_)""")]
    public void NumberFormat_RoundTrips(string numberFormat)
    {
        var workbook = new Workbook("Fmt");
        var sheet = workbook.AddSheet("S");
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new NumberValue(1234.5), StyleId = styleId });

        var reloaded = SaveAndReload(workbook);
        var cell = reloaded.GetSheetAt(0).GetCell(1, 1)!;
        reloaded.GetStyle(cell.StyleId).NumberFormat.Should().Be(numberFormat);
    }

    [Fact]
    public void NumberFormat_StyleOnlyCellRoundTrips()
    {
        var workbook = new Workbook("StyleOnlyFmt");
        var sheet = workbook.AddSheet("S");
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0000" });
        sheet.SetStyleOnly(3, 5, styleId);

        var reloaded = SaveAndReload(workbook);
        var rs = reloaded.GetSheetAt(0);
        var entry = rs.GetStyleOnlyEntries().FirstOrDefault(e => e.Key == (3u, 5u));
        entry.Should().NotBe(default);
        reloaded.GetStyle(entry.StyleId).NumberFormat.Should().Be("0.0000");
    }

    // ---- Whitespace / CR-faithful cell text ---------------------------------------------------

    [Theory]
    [InlineData("Note:\r\n\r\nLine two\r\nLine three")]   // CR-LF runs must survive
    [InlineData("a\rb")]                                  // bare CR
    [InlineData("  leading and trailing  ")]
    [InlineData("tab\there")]
    public void CellText_IsByteFaithful(string text)
    {
        var workbook = new Workbook("Whitespace");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);
        reloaded.GetCell(1, 1)!.Value.Should().Be(new TextValue(text));
    }

    // ---- Hyperlink null-URI safety ------------------------------------------------------------

    [Fact]
    public void Hyperlink_InternalLinkWritesHashPrefixAndReloadsAsInternal()
    {
        var workbook = new Workbook("InternalLink");
        var sheet = workbook.AddSheet("Data");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[address] = "Data!A1";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument, ScreenTip: "", Bookmark: "Data!A1");
        sheet.SetCell(address, new TextValue("link"));

        using var stream = new MemoryStream();
        new SpreadsheetXmlFileAdapter().Save(workbook, stream);

        // The emitted HRef must carry the '#' so it is recognised as in-document, not a relative path.
        var document = LoadSpreadsheetXml(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var href = document.Descendants(ss + "Cell")
            .Select(c => c.Attribute(ss + "HRef")?.Value)
            .First(v => v is not null);
        href.Should().Be("#Data!A1");

        stream.Position = 0;
        var reloaded = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);
        reloaded.HyperlinkMetadata[new CellAddress(reloaded.Id, 1, 1)].LinkType
            .Should().Be(HyperlinkTargetKind.PlaceInThisDocument);
    }

    [Fact]
    public void Hyperlink_InternalAndExternalLinks_SurviveXlsxRoundTripWithoutCrashing()
    {
        // The crash this guards against: the xml adapter previously emitted internal links without a '#',
        // so an xlsx re-save classified them as external and handed ClosedXML a null URI
        // (AddHyperlinkRelationship(null)). Round-tripping xml -> xlsx must now succeed.
        var workbook = new Workbook("LinkRoundTrip");
        var sheet = workbook.AddSheet("Data");

        var internalAddr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(internalAddr, new TextValue("go"));
        sheet.Hyperlinks[internalAddr] = "Data!A5";
        sheet.HyperlinkMetadata[internalAddr] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument, "", "Data!A5");

        var externalAddr = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(externalAddr, new TextValue("web"));
        sheet.Hyperlinks[externalAddr] = "https://example.com/";
        sheet.HyperlinkMetadata[externalAddr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage, "", "");

        // xml save -> xml reload -> xlsx save must not throw.
        var viaXml = SaveAndReload(workbook);

        using var xlsxStream = new MemoryStream();
        var act = () => new XlsxFileAdapter().Save(viaXml, xlsxStream);
        act.Should().NotThrow();
    }

    [Fact]
    public void Hyperlink_EmptyTargetIsNotEmitted()
    {
        var workbook = new Workbook("EmptyLink");
        var sheet = workbook.AddSheet("S");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("x"));
        sheet.Hyperlinks[address] = "   ";

        using var stream = new MemoryStream();
        new SpreadsheetXmlFileAdapter().Save(workbook, stream);

        var document = LoadSpreadsheetXml(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        document.Descendants(ss + "Cell")
            .Select(c => c.Attribute(ss + "HRef")?.Value)
            .Where(v => v is not null)
            .Should().BeEmpty();
    }
}
