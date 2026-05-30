using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
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
    public void Load_ReadsWorkbookNamedRangesFromSpreadsheetMlNames()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Names>
                <ss:NamedRange ss:Name="SalesData" ss:RefersTo="=Report!A1:B2"/>
                <ss:NamedRange ss:Name="SingleCell" ss:RefersTo="'Q1 Summary'!$C$3"/>
                <ss:NamedRange ss:Name="UnsupportedFormula" ss:RefersTo="=SUM(Report!A1:B2)"/>
              </ss:Names>
              <ss:Worksheet ss:Name="Report"><ss:Table/></ss:Worksheet>
              <ss:Worksheet ss:Name="Q1 Summary"><ss:Table/></ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var report = workbook.GetSheet("Report")!;
        var summary = workbook.GetSheet("Q1 Summary")!;
        workbook.NamedRanges.Should().ContainKey("SalesData");
        workbook.NamedRanges["SalesData"].Should().Be(new GridRange(
            new CellAddress(report.Id, 1, 1),
            new CellAddress(report.Id, 2, 2)));
        workbook.NamedRanges["SingleCell"].Should().Be(new GridRange(
            new CellAddress(summary.Id, 3, 3),
            new CellAddress(summary.Id, 3, 3)));
        workbook.NamedRanges.Should().NotContainKey("UnsupportedFormula");
    }

    [Fact]
    public void SaveThenLoad_RoundTripsWorkbookNamedRangesAsSpreadsheetMlNames()
    {
        var workbook = new Workbook("XmlNames");
        var sheet = workbook.AddSheet("Q1 Summary");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 3));
        workbook.DefineNamedRange("SalesData", range);

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var namedRange = document.Descendants(ss + "NamedRange").Should().ContainSingle().Which;
        namedRange.Attribute(ss + "Name")!.Value.Should().Be("SalesData");
        namedRange.Attribute(ss + "RefersTo")!.Value.Should().Be("='Q1 Summary'!A2:C4");

        stream.Position = 0;
        var loaded = adapter.Load(stream);

        var loadedSheet = loaded.GetSheet("Q1 Summary")!;
        loaded.NamedRanges.Should().ContainKey("SalesData");
        loaded.NamedRanges["SalesData"].Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 2, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
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
    public void Load_ReadsSpreadsheetMlWorksheetVisibility()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Visible"><ss:Table/></ss:Worksheet>
              <ss:Worksheet ss:Name="Hidden" ss:Visible="SheetHidden"><ss:Table/></ss:Worksheet>
              <ss:Worksheet ss:Name="VeryHidden" ss:Visible="SheetVeryHidden"><ss:Table/></ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        workbook.GetSheetAt(0).IsHidden.Should().BeFalse();
        workbook.GetSheetAt(0).IsVeryHidden.Should().BeFalse();
        workbook.GetSheetAt(1).IsHidden.Should().BeTrue();
        workbook.GetSheetAt(1).IsVeryHidden.Should().BeFalse();
        workbook.GetSheetAt(2).IsHidden.Should().BeTrue();
        workbook.GetSheetAt(2).IsVeryHidden.Should().BeTrue();
    }

    [Fact]
    public void Load_ReadsSpreadsheetMlWorksheetOptions()
    {
        using var stream = StreamFromString("""
            <ss:Workbook
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:x="urn:schemas-microsoft-com:office:excel">
              <ss:Worksheet ss:Name="Options">
                <ss:Table/>
                <x:WorksheetOptions>
                  <x:DoNotDisplayGridlines/>
                  <x:Print>
                    <x:Gridlines/>
                  </x:Print>
                  <x:FreezePanes/>
                  <x:FrozenNoSplit/>
                  <x:SplitHorizontal>2</x:SplitHorizontal>
                  <x:TopRowBottomPane>2</x:TopRowBottomPane>
                  <x:SplitVertical>3</x:SplitVertical>
                  <x:LeftColumnRightPane>3</x:LeftColumnRightPane>
                </x:WorksheetOptions>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.ShowGridlines.Should().BeFalse();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(3);
    }

    [Fact]
    public void Load_ReadsSpreadsheetMlRowHeightAndHiddenState()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Layout">
                <ss:Table>
                  <ss:Row ss:Height="27.5">
                    <ss:Cell><ss:Data ss:Type="String">Tall</ss:Data></ss:Cell>
                  </ss:Row>
                  <ss:Row ss:Index="3" ss:Hidden="1">
                    <ss:Cell><ss:Data ss:Type="String">Hidden</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.RowHeights[1].Should().Be(27.5);
        sheet.HiddenRows.Should().Contain(3u);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Tall"));
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("Hidden"));
    }

    [Fact]
    public void Load_ReadsSpreadsheetMlColumnWidthAndHiddenState()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Layout">
                <ss:Table>
                  <ss:Column ss:Width="18.5"/>
                  <ss:Column ss:Index="3" ss:Hidden="1"/>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String">A</ss:Data></ss:Cell>
                    <ss:Cell ss:Index="3"><ss:Data ss:Type="String">Hidden column</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.ColumnWidths[1].Should().Be(18.5);
        sheet.HiddenCols.Should().Contain(3u);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new TextValue("Hidden column"));
    }

    [Fact]
    public void Load_ReadsSpreadsheetMlColumnSpanLayout()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Layout">
                <ss:Table>
                  <ss:Column ss:Index="2" ss:Span="2" ss:Width="21.25" ss:Hidden="1"/>
                  <ss:Row>
                    <ss:Cell ss:Index="4"><ss:Data ss:Type="String">After span</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.ColumnWidths[2].Should().Be(21.25);
        sheet.ColumnWidths[3].Should().Be(21.25);
        sheet.ColumnWidths[4].Should().Be(21.25);
        sheet.HiddenCols.Should().Contain([2u, 3u, 4u]);
        sheet.GetCell(1, 4)!.Value.Should().Be(new TextValue("After span"));
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
    public void Load_ReadsSpreadsheetMlNumberFormatStyles()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Styles>
                <ss:Style ss:ID="currency">
                  <ss:NumberFormat ss:Format="$#,##0.00"/>
                </ss:Style>
                <ss:Style ss:ID="percent">
                  <ss:NumberFormat ss:Format="0.0%"/>
                </ss:Style>
              </ss:Styles>
              <ss:Worksheet ss:Name="Styles">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell ss:StyleID="currency"><ss:Data ss:Type="Number">12.5</ss:Data></ss:Cell>
                    <ss:Cell ss:StyleID="percent"/>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        workbook.GetStyle(sheet.GetStyleOnly(1, 2)!.Value).NumberFormat.Should().Be("0.0%");
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSpreadsheetMlNumberFormatStyles()
    {
        var workbook = new Workbook("XmlStyles");
        var sheet = workbook.AddSheet("Styles");
        var currency = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var percent = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0%" });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new NumberValue(12.5),
            StyleId = currency
        });
        sheet.SetStyleOnly(1, 2, percent);

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var styles = document.Descendants(ss + "Style").ToList();
        styles.Should().HaveCount(2);
        var savedFormats = styles
            .Select(style => style.Element(ss + "NumberFormat")!.Attribute(ss + "Format")!.Value)
            .ToList();
        savedFormats.Should().BeEquivalentTo(["$#,##0.00", "0.0%"]);
        var cells = document.Descendants(ss + "Cell").ToList();
        cells.Should().HaveCount(2);
        cells.Select(cell => cell.Attribute(ss + "StyleID")?.Value)
            .Should().OnlyContain(styleId => !string.IsNullOrWhiteSpace(styleId));
        cells.Single(cell => cell.Attribute(ss + "Index")?.Value == "2").Element(ss + "Data").Should().BeNull();

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        loaded.GetStyle(loadedSheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        loaded.GetStyle(loadedSheet.GetStyleOnly(1, 2)!.Value).NumberFormat.Should().Be("0.0%");
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
    public void Load_InvalidMergeAcrossDoesNotSkipFollowingCells()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Merged">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell ss:MergeAcross="4294967295"><ss:Data ss:Type="String">Bad merge</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="String">Still read</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Bad merge"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Still read"));
        sheet.MergedRegions.Should().BeEmpty();
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
    public void SaveThenLoad_RoundTripsSpreadsheetMlWorksheetVisibility()
    {
        var workbook = new Workbook("XmlVisibility");
        workbook.AddSheet("Visible");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var veryHidden = workbook.AddSheet("VeryHidden");
        veryHidden.IsHidden = true;
        veryHidden.IsVeryHidden = true;

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var worksheets = document.Root!.Elements(ss + "Worksheet").ToList();
        worksheets[0].Attribute(ss + "Visible").Should().BeNull();
        worksheets[1].Attribute(ss + "Visible")!.Value.Should().Be("SheetHidden");
        worksheets[2].Attribute(ss + "Visible")!.Value.Should().Be("SheetVeryHidden");

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        loaded.GetSheetAt(0).IsHidden.Should().BeFalse();
        loaded.GetSheetAt(1).IsHidden.Should().BeTrue();
        loaded.GetSheetAt(1).IsVeryHidden.Should().BeFalse();
        loaded.GetSheetAt(2).IsHidden.Should().BeTrue();
        loaded.GetSheetAt(2).IsVeryHidden.Should().BeTrue();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSpreadsheetMlWorksheetOptions()
    {
        var workbook = new Workbook("XmlWorksheetOptions");
        var sheet = workbook.AddSheet("Options");
        sheet.ShowGridlines = false;
        sheet.PrintGridlines = true;
        sheet.FrozenRows = 2;
        sheet.FrozenCols = 3;

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace x = "urn:schemas-microsoft-com:office:excel";
        var options = document.Descendants(x + "WorksheetOptions").Single();
        options.Element(x + "DoNotDisplayGridlines").Should().NotBeNull();
        options.Element(x + "Print")?.Element(x + "Gridlines").Should().NotBeNull();
        options.Element(x + "FreezePanes").Should().NotBeNull();
        options.Element(x + "FrozenNoSplit").Should().NotBeNull();
        options.Element(x + "SplitHorizontal")!.Value.Should().Be("2");
        options.Element(x + "TopRowBottomPane")!.Value.Should().Be("2");
        options.Element(x + "SplitVertical")!.Value.Should().Be("3");
        options.Element(x + "LeftColumnRightPane")!.Value.Should().Be("3");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.ShowGridlines.Should().BeFalse();
        loaded.PrintGridlines.Should().BeTrue();
        loaded.FrozenRows.Should().Be(2);
        loaded.FrozenCols.Should().Be(3);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSpreadsheetMlRowHeightAndHiddenState()
    {
        var workbook = new Workbook("XmlRowLayout");
        var sheet = workbook.AddSheet("Layout");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Tall"));
        sheet.RowHeights[2] = 31.25;
        sheet.HiddenRows.Add(4);

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var rows = document.Descendants(ss + "Row").ToList();
        var tallRow = rows.Single(row => row.Attribute(ss + "Index")?.Value == "2");
        tallRow.Attribute(ss + "Height")!.Value.Should().Be("31.25");
        var hiddenMetadataOnlyRow = rows.Single(row => row.Attribute(ss + "Index")?.Value == "4");
        hiddenMetadataOnlyRow.Attribute(ss + "Hidden")!.Value.Should().Be("1");
        hiddenMetadataOnlyRow.Elements(ss + "Cell").Should().BeEmpty();

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.RowHeights[2].Should().Be(31.25);
        loaded.HiddenRows.Should().Contain(4u);
        loaded.GetCell(2, 1)!.Value.Should().Be(new TextValue("Tall"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSpreadsheetMlColumnWidthAndHiddenState()
    {
        var workbook = new Workbook("XmlColumnLayout");
        var sheet = workbook.AddSheet("Layout");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Wide"));
        sheet.ColumnWidths[2] = 19.75;
        sheet.HiddenCols.Add(4);

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var columns = document.Descendants(ss + "Column").ToList();
        var wideColumn = columns.Single(column => column.Attribute(ss + "Index")?.Value == "2");
        wideColumn.Attribute(ss + "Width")!.Value.Should().Be("19.75");
        var hiddenMetadataOnlyColumn = columns.Single(column => column.Attribute(ss + "Index")?.Value == "4");
        hiddenMetadataOnlyColumn.Attribute(ss + "Hidden")!.Value.Should().Be("1");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.ColumnWidths[2].Should().Be(19.75);
        loaded.HiddenCols.Should().Contain(4u);
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Wide"));
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
    public void LoadTransformed_PreservesSpreadsheetMlHyperlinksAndComments()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Review" url="https://example.com/review" note="Check generated output"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@url}" ss:HRefScreenTip="Open source">
                            <ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data>
                            <ss:Comment ss:Author="XSLT">
                              <ss:Data><xsl:value-of select="@note"/></ss:Data>
                            </ss:Comment>
                          </ss:Cell>
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
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Review"));
        sheet.Hyperlinks[address].Should().Be("https://example.com/review");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open source",
            ""));
        sheet.Comments[address].Should().Be("Check generated output");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlNumberFormatStyles()
    {
        using var source = StreamFromString("""
            <rows>
              <row amount="12.5"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="money">
                      <ss:NumberFormat ss:Format="$#,##0.00"/>
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:StyleID="money">
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@amount"/></ss:Data>
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
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
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
    public void LoadTransformed_OutputAboveLimit_ReportsTransformSafetyDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Large">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">This output is intentionally over the tiny adapter limit.</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet, maxOutputBytes: 32);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output exceeded the 32 byte safety limit*");
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_SourceAboveInputLimit_ReportsTransformSourceDiagnostic()
    {
        using var source = StreamFromString($"<rows><row value=\"{new string('A', 1024)}\"/></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Limited"><ss:Table/></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 512);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*source XML*");
        source.CanRead.Should().BeTrue();
        stylesheet.CanRead.Should().BeTrue();
    }

    [Fact]
    public void LoadTransformed_StylesheetAboveInputLimit_ReportsTransformStylesheetDiagnostic()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Limited"><ss:Table/></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            maxInputCharacters: 64);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*");
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
    public void LoadTransformed_RejectsStylesheetInclude()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:include href="file:///C:/Windows/win.ini"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_RejectsStylesheetImport()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:import href="file:///C:/Windows/win.ini"/>
              <xsl:template match="/">
                <xsl:value-of select="'blocked'"/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*stylesheet*")
            .WithInnerException<XsltException>();
    }

    [Fact]
    public void LoadTransformed_WrapsMalformedTransformOutputWithXsltContext()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="text"/>
              <xsl:template match="/">
                <xsl:text>&lt;ss:Workbook</xsl:text>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<XmlException>();
    }

    [Fact]
    public void LoadTransformed_WrapsNonSpreadsheetMlOutputWithXsltContext()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/">
                <rows/>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<InvalidDataException>();
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
