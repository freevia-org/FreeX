using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class SpreadsheetXmlFileAdapterTests
{
    [Fact]
    public void Load_ReadsSpreadsheetMlCellsWithIndexesAndFormulas()
    {
        using var stream = StreamFromString("""
            <?xml version="1.0"?>
            <?mso-application progid="Excel.Sheet"?>
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Report">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String">Name</ss:Data></ss:Cell>
                    <ss:Cell ss:Index="3"><ss:Data ss:Type="Number">12.5</ss:Data></ss:Cell>
                  </ss:Row>
                  <ss:Row ss:Index="4">
                    <ss:Cell ss:Formula="=SUM(C1:C1)"><ss:Data ss:Type="Number">12.5</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Boolean">1</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="DateTime">2026-05-27T09:30:00</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        workbook.Sheets.Should().ContainSingle();
        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Report");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Name"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new NumberValue(12.5));
        sheet.GetCell(4, 1)!.FormulaText.Should().Be("SUM(C1:C1)");
        sheet.GetCell(4, 1)!.Value.Should().Be(new NumberValue(12.5));
        sheet.GetCell(4, 2)!.Value.Should().Be(new BoolValue(true));
        sheet.GetCell(4, 3)!.Value.Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 27, 9, 30, 0)));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMultipleSheetsAndValueTypes()
    {
        var workbook = new Workbook("XmlRoundTrip");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Text < & >"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42.25));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 27, 13, 45, 5)));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new Cell { FormulaText = "SUM(A2:A2)", Value = new NumberValue(42.25) });
        var second = workbook.AddSheet("Second");
        second.SetCell(new CellAddress(second.Id, 1, 2), new ErrorValue("#VALUE!"));

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.Sheets.Should().HaveCount(2);
        loaded.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue("Text < & >"));
        loaded.GetSheetAt(0).GetCell(2, 1)!.Value.Should().Be(new NumberValue(42.25));
        loaded.GetSheetAt(0).GetCell(3, 1)!.Value.Should().Be(new BoolValue(false));
        loaded.GetSheetAt(0).GetCell(4, 1)!.Value.Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 27, 13, 45, 5)));
        loaded.GetSheetAt(0).GetCell(5, 1)!.FormulaText.Should().Be("SUM(A2:A2)");
        loaded.GetSheetAt(1).GetCell(1, 2)!.Value.Should().Be(new ErrorValue("#VALUE!"));
    }

    [Fact]
    public void Load_ReadsSpreadsheetMlMergeAcrossAndMergeDown()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Merged">
                <ss:Table>
                  <ss:Row ss:Index="2">
                    <ss:Cell ss:Index="3" ss:MergeAcross="2" ss:MergeDown="1">
                      <ss:Data ss:Type="String">Merged heading</ss:Data>
                    </ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(2, 3)!.Value.Should().Be(new TextValue("Merged heading"));
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 5)));
    }

    [Fact]
    public void Load_AdvancesImplicitCellIndexPastMergeAcrossSpan()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Merged">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell ss:MergeAcross="2"><ss:Data ss:Type="String">Merged heading</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="String">After merge</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Merged heading"));
        sheet.GetCell(1, 4)!.Value.Should().Be(new TextValue("After merge"));
        sheet.GetCell(1, 2).Should().BeNull();
        sheet.GetCell(1, 3).Should().BeNull();
    }

    [Fact]
    public void Load_TreatsBackwardCellIndexesAsImplicitNextColumn()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Indexes">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String">First</ss:Data></ss:Cell>
                    <ss:Cell ss:Index="1"><ss:Data ss:Type="String">Second</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("First"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Second"));
    }

    [Fact]
    public void Load_TreatsBackwardRowIndexesAsImplicitNextRow()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Indexes">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String">First</ss:Data></ss:Cell>
                  </ss:Row>
                  <ss:Row ss:Index="1">
                    <ss:Cell><ss:Data ss:Type="String">Second</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("First"));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Second"));
    }

    [Fact]
    public void Save_WritesMergeAcrossAndMergeDownForMergedRegions()
    {
        var workbook = new Workbook("XmlMerges");
        var sheet = workbook.AddSheet("Merged");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Merged heading"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Hidden by merge"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 3)));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 4, 2),
            new CellAddress(sheet.Id, 4, 3)));

        using var stream = new MemoryStream();
        new SpreadsheetXmlFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var cells = document.Descendants(ss + "Cell").ToList();
        var mergedHeadingCell = cells.Single(cell => cell.Element(ss + "Data")?.Value == "Merged heading");
        mergedHeadingCell.Attribute(ss + "MergeAcross")!.Value.Should().Be("2");
        mergedHeadingCell.Attribute(ss + "MergeDown")!.Value.Should().Be("1");
        cells.Select(cell => cell.Element(ss + "Data")?.Value)
            .Should().NotContain("Hidden by merge");

        var blankMergeAnchor = cells.Single(cell => cell.Attribute(ss + "Index")?.Value == "2" &&
                                                   cell.Attribute(ss + "MergeAcross")?.Value == "1");
        blankMergeAnchor.Element(ss + "Data").Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMergedRegions()
    {
        var workbook = new Workbook("XmlMergeRoundTrip");
        var sheet = workbook.AddSheet("Merged");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 3)));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 4, 4),
            new CellAddress(sheet.Id, 4, 5)));

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.GetSheetAt(0).MergedRegions
            .Select(region => (region.Start.Row, region.Start.Col, region.End.Row, region.End.Col))
            .Should()
            .Equal((1u, 1u, 2u, 3u), (4u, 4u, 4u, 5u));
    }

    [Fact]
    public void LoadTransformed_AppliesSafeXsltAndLoadsSpreadsheetMlOutput()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Alpha" amount="12.5"/>
              <row name="Beta" amount="7.25"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:output method="xml" indent="yes"/>
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Transformed">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@amount"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Transformed");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(12.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(7.25));
    }

    [Fact]
    public void LoadTransformed_RejectsExternalDocumentFunction()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <xsl:value-of select="document('file:///C:/Windows/win.ini')"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Load_RejectsDtdPayloads()
    {
        using var stream = StreamFromString("""
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini"> ]>
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Bad"><ss:Table><ss:Row><ss:Cell><ss:Data ss:Type="String">&xxe;</ss:Data></ss:Cell></ss:Row></ss:Table></ss:Worksheet>
            </ss:Workbook>
            """);

        var act = () => new SpreadsheetXmlFileAdapter().Load(stream);

        act.Should().Throw<XmlException>();
    }

    private static MemoryStream StreamFromString(string value) =>
        new(Encoding.UTF8.GetBytes(value));
}
