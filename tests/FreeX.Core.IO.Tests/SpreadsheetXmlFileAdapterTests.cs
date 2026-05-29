using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

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
    public void Load_NormalizesInvalidBlankDuplicateAndLongWorksheetNames()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="'Bad:/?*[]Name'"><ss:Table/></ss:Worksheet>
              <ss:Worksheet ss:Name="bad:/?*[]name"><ss:Table/></ss:Worksheet>
              <ss:Worksheet ss:Name="   "><ss:Table/></ss:Worksheet>
              <ss:Worksheet ss:Name="''"><ss:Table/></ss:Worksheet>
              <ss:Worksheet ss:Name="ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890"><ss:Table/></ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal(
            "Bad______Name",
            "bad______name (1)",
            "Sheet3",
            "Sheet",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ12345");
        workbook.Sheets.Select(sheet => sheet.Name).Should().OnlyHaveUniqueItems();
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
    public void Load_ReadsSpreadsheetMlCellHyperlinks()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Links">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell ss:HRef="https://example.com/report" ss:HRefScreenTip="Open report">
                      <ss:Data ss:Type="String">Report</ss:Data>
                    </ss:Cell>
                    <ss:Cell ss:HRef="#Links!R1C1">
                      <ss:Data ss:Type="String">Back</ss:Data>
                    </ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        var externalAddress = new CellAddress(sheet.Id, 1, 1);
        var internalAddress = new CellAddress(sheet.Id, 1, 2);
        sheet.GetCell(externalAddress)!.Value.Should().Be(new TextValue("Report"));
        sheet.Hyperlinks[externalAddress].Should().Be("https://example.com/report");
        sheet.HyperlinkMetadata[externalAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open report",
            ""));
        sheet.Hyperlinks[internalAddress].Should().Be("#Links!R1C1");
        sheet.HyperlinkMetadata[internalAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "",
            "Links!R1C1"));
    }

    [Fact]
    public void Load_ReadsSpreadsheetMlCellComments()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Notes">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell>
                      <ss:Data ss:Type="String">Needs review</ss:Data>
                      <ss:Comment ss:Author="Finance">
                        <ss:Data>Check &amp; approve total</ss:Data>
                      </ss:Comment>
                    </ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Needs review"));
        sheet.Comments[address].Should().Be("Check & approve total");
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
    public void SaveThenLoad_RoundTripsSpreadsheetMlCellHyperlinks()
    {
        var workbook = new Workbook("XmlLinks");
        var sheet = workbook.AddSheet("Links");
        var externalAddress = new CellAddress(sheet.Id, 1, 1);
        var mailAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(externalAddress, new TextValue("Report"));
        sheet.Hyperlinks[externalAddress] = "https://example.com/report";
        sheet.HyperlinkMetadata[externalAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open report",
            "");
        sheet.SetCell(mailAddress, new TextValue("Email"));
        sheet.Hyperlinks[mailAddress] = "mailto:team@example.com";
        sheet.HyperlinkMetadata[mailAddress] = new HyperlinkMetadata(HyperlinkTargetKind.EmailAddress);

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var reportCell = document.Descendants(ss + "Cell")
            .Single(cell => cell.Element(ss + "Data")?.Value == "Report");
        reportCell.Attribute(ss + "HRef")!.Value.Should().Be("https://example.com/report");
        reportCell.Attribute(ss + "HRefScreenTip")!.Value.Should().Be("Open report");

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedExternalAddress = new CellAddress(loadedSheet.Id, 1, 1);
        var loadedMailAddress = new CellAddress(loadedSheet.Id, 2, 1);
        loadedSheet.Hyperlinks[loadedExternalAddress].Should().Be("https://example.com/report");
        loadedSheet.HyperlinkMetadata[loadedExternalAddress].ScreenTip.Should().Be("Open report");
        loadedSheet.Hyperlinks[loadedMailAddress].Should().Be("mailto:team@example.com");
        loadedSheet.HyperlinkMetadata[loadedMailAddress].LinkType.Should().Be(HyperlinkTargetKind.EmailAddress);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSpreadsheetMlCellComments()
    {
        var workbook = new Workbook("XmlNotes");
        var sheet = workbook.AddSheet("Notes");
        var valueAddress = new CellAddress(sheet.Id, 1, 1);
        var noteOnlyAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(valueAddress, new TextValue("Total"));
        sheet.Comments[valueAddress] = "Check < & > total";
        sheet.Comments[noteOnlyAddress] = "Standalone note";

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var comments = document.Descendants(ss + "Comment").ToList();
        comments.Should().HaveCount(2);
        comments.All(comment => comment.Attribute(ss + "Author")?.Value == "FreeX").Should().BeTrue();
        comments.Select(comment => comment.Element(ss + "Data")?.Value)
            .Should().BeEquivalentTo("Check < & > total", "Standalone note");

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.Comments[new CellAddress(loadedSheet.Id, 1, 1)].Should().Be("Check < & > total");
        loadedSheet.Comments[new CellAddress(loadedSheet.Id, 2, 2)].Should().Be("Standalone note");
    }

    [Fact]
    public void Load_UsesCurrentStreamPositionAndLeavesInputStreamOpen()
    {
        using var stream = PositionedStreamFromString("ignored", """
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Offset">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String">Gamma</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Offset");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Gamma"));
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public void Save_UsesCurrentStreamPositionAndLeavesOutputStreamOpen()
    {
        var workbook = new Workbook("OffsetSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Gamma"));
        var prefixBytes = Encoding.UTF8.GetBytes("ignored");
        using var stream = new MemoryStream();
        stream.Write(prefixBytes);

        new SpreadsheetXmlFileAdapter().Save(workbook, stream);

        stream.CanWrite.Should().BeTrue();
        stream.ToArray().Take(prefixBytes.Length).Should().Equal(prefixBytes);
        stream.Position = prefixBytes.Length;
        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        document.Descendants(ss + "Data").Single().Value.Should().Be("Gamma");
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
    public void LoadTransformed_UsesCurrentStreamPositionsAndLeavesInputStreamsOpen()
    {
        using var source = PositionedStreamFromString("ignored", "<rows><row name=\"Gamma\"/></rows>");
        using var stylesheet = PositionedStreamFromString("ignored", """
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Offset">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell>
                          <ss:Data ss:Type="String"><xsl:value-of select="/rows/row/@name"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Offset");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Gamma"));
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
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

    private static MemoryStream PositionedStreamFromString(string prefix, string value)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var stream = new MemoryStream(prefixBytes.Concat(valueBytes).ToArray());
        stream.Position = prefixBytes.Length;
        return stream;
    }
}
