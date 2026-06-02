using System.Xml;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
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
    public void Load_NormalizesSpreadsheetMlDateTimesWithOffsetsToUtc()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Dates">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="DateTime">2026-05-31T11:15:30+03:00</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="DateTime">2026-05-31T08:15:30Z</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);
        var expectedUtc = DateTimeValue.FromDateTime(new DateTime(2026, 5, 31, 8, 15, 30));

        sheet.GetCell(1, 1)!.Value.Should().Be(expectedUtc);
        sheet.GetCell(1, 2)!.Value.Should().Be(expectedUtc);
    }

    [Fact]
    public void Load_KeepsNonFiniteSpreadsheetMlNumbersAsText()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Numbers">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="Number">NaN</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Number">Infinity</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Number">-Infinity</ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Number">42.5</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
        sheet.GetCell(1, 4)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void Load_TrimsSpreadsheetMlBooleanText()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Booleans">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="Boolean"> 1 </ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Boolean"> TRUE </ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Boolean"> 0 </ss:Data></ss:Cell>
                    <ss:Cell><ss:Data ss:Type="Boolean"> false </ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.GetCell(1, 1)!.Value.Should().Be(new BoolValue(true));
        sheet.GetCell(1, 2)!.Value.Should().Be(new BoolValue(true));
        sheet.GetCell(1, 3)!.Value.Should().Be(new BoolValue(false));
        sheet.GetCell(1, 4)!.Value.Should().Be(new BoolValue(false));
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
    public void Load_DropsInvalidSpreadsheetMlNamedRanges()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Names>
                <ss:NamedRange ss:Name="ValidRows" ss:RefersTo="=Report!A1:A2"/>
                <ss:NamedRange ss:Name="1InvalidName" ss:RefersTo="=Report!B1:B2"/>
                <ss:NamedRange ss:Name="MissingSheet" ss:RefersTo="=Missing!A1:A2"/>
                <ss:NamedRange ss:Name="BadAddress" ss:RefersTo="=Report!NotA1"/>
                <ss:NamedRange ss:Name="OutOfBounds" ss:RefersTo="=Report!A1:XFE1"/>
                <ss:NamedRange ss:Name="BlankRef" ss:RefersTo="   "/>
              </ss:Names>
              <ss:Worksheet ss:Name="Report"><ss:Table/></ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var report = workbook.GetSheet("Report")!;
        workbook.NamedRanges.Should().ContainSingle();
        workbook.NamedRanges["ValidRows"].Should().Be(new GridRange(
            new CellAddress(report.Id, 1, 1),
            new CellAddress(report.Id, 2, 1)));
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
    public void Load_TrimsSpreadsheetMlUnsignedIntegerMetadata()
    {
        using var stream = StreamFromString("""
            <ss:Workbook
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:x="urn:schemas-microsoft-com:office:excel">
              <ss:Worksheet ss:Name="Layout">
                <ss:Table>
                  <ss:Column ss:Index=" 2 " ss:Span=" 1 " ss:Width="20"/>
                  <ss:Row ss:Index=" 3 " ss:Span=" 1 " ss:Height="24">
                    <ss:Cell ss:Index=" 4 " ss:MergeAcross=" 1 " ss:MergeDown=" 1 ">
                      <ss:Data ss:Type="String">Merged</ss:Data>
                    </ss:Cell>
                  </ss:Row>
                </ss:Table>
                <x:WorksheetOptions>
                  <x:FreezePanes/>
                  <x:FrozenNoSplit/>
                  <x:SplitHorizontal>
                    2
                  </x:SplitHorizontal>
                  <x:SplitVertical>
                    3
                  </x:SplitVertical>
                </x:WorksheetOptions>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(3);
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(2, 20));
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(3, 20));
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(3, 24));
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(4, 24));
        sheet.GetCell(3, 4)!.Value.Should().Be(new TextValue("Merged"));
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 3, 4),
            new CellAddress(sheet.Id, 4, 5)));
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
    public void Load_ReadsSpreadsheetMlRowSpanLayout()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Layout">
                <ss:Table>
                  <ss:Row ss:Index="2" ss:Span="2" ss:Height="24.5" ss:Hidden="1">
                    <ss:Cell><ss:Data ss:Type="String">Spanned row</ss:Data></ss:Cell>
                  </ss:Row>
                  <ss:Row>
                    <ss:Cell><ss:Data ss:Type="String">After span</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var sheet = new SpreadsheetXmlFileAdapter().Load(stream).GetSheetAt(0);

        sheet.RowHeights[2].Should().Be(24.5);
        sheet.RowHeights[3].Should().Be(24.5);
        sheet.RowHeights[4].Should().Be(24.5);
        sheet.HiddenRows.Should().Contain([2u, 3u, 4u]);
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Spanned row"));
        sheet.GetCell(5, 1)!.Value.Should().Be(new TextValue("After span"));
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
    public void Load_ReadsSpreadsheetMlInheritedNumberFormatStyles()
    {
        using var stream = StreamFromString("""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Styles>
                <ss:Style ss:ID="currency">
                  <ss:NumberFormat ss:Format="$#,##0.00"/>
                </ss:Style>
                <ss:Style ss:ID="currencyChild" ss:Parent="currency"/>
              </ss:Styles>
              <ss:Worksheet ss:Name="Styles">
                <ss:Table>
                  <ss:Row>
                    <ss:Cell ss:StyleID="currencyChild"><ss:Data ss:Type="Number">12.5</ss:Data></ss:Cell>
                  </ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var workbook = new SpreadsheetXmlFileAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
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

    [Fact]
    public void Load_RejectsXmlAboveCharacterLimit()
    {
        using var stream = StreamFromString($"""
            <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <ss:Worksheet ss:Name="Large">
                <ss:Table>
                  <ss:Row><ss:Cell><ss:Data ss:Type="String">{new string('A', 1024)}</ss:Data></ss:Cell></ss:Row>
                </ss:Table>
              </ss:Worksheet>
            </ss:Workbook>
            """);

        var act = () => new SpreadsheetXmlFileAdapter().Load(stream, maxCharactersInDocument: 256);

        act.Should().Throw<XmlException>();
    }

}
