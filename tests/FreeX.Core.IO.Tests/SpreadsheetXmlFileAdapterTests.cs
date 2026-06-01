using System.Diagnostics;
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
    public void Save_WritesNonFiniteSpreadsheetMlNumbersAsTextCells()
    {
        var workbook = new Workbook("NonFiniteXml");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(double.NaN));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(double.PositiveInfinity));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(double.NegativeInfinity));

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var data = document.Descendants(ss + "Data").ToArray();
        data.Select(element => element.Attribute(ss + "Type")!.Value).Should().Equal("String", "String", "String");
        data.Select(element => element.Value).Should().Equal("NaN", "Infinity", "-Infinity");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
    }

    [Fact]
    public void Save_WritesNonFiniteSpreadsheetMlDateTimesAsTextCells()
    {
        var workbook = new Workbook("NonFiniteDates");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(double.NaN));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(double.PositiveInfinity));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new DateTimeValue(double.NegativeInfinity));

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var data = document.Descendants(ss + "Data").ToArray();
        data.Select(element => element.Attribute(ss + "Type")!.Value).Should().Equal("String", "String", "String");
        data.Select(element => element.Value).Should().Equal("NaN", "Infinity", "-Infinity");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
    }

    [Fact]
    public void Save_WritesOutOfRangeSpreadsheetMlDateTimesAsTextCells()
    {
        var workbook = new Workbook("OutOfRangeDates");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(double.MaxValue));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(double.MinValue));

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var data = document.Descendants(ss + "Data").ToArray();
        data.Select(element => element.Attribute(ss + "Type")!.Value).Should().Equal("String", "String");
        data.Select(element => element.Value).Should().Equal(
            double.MaxValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            double.MinValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue(double.MaxValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue(double.MinValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
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
    public void SaveThenLoad_RoundTripsSpreadsheetMlStyledFormulaCellsThroughValueStreamingPath()
    {
        var workbook = new Workbook("XmlStyledFormulaFastPath");
        var sheet = workbook.AddSheet("Data");
        var currency = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(12.5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(7.25));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new Cell
        {
            FormulaText = "SUM(A1:B1)",
            Value = new NumberValue(19.75),
            StyleId = currency
        });

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var formulaCell = document.Descendants(ss + "Cell")
            .Single(cell => cell.Attribute(ss + "Formula")?.Value == "=SUM(A1:B1)");
        formulaCell.Attribute(ss + "StyleID").Should().NotBeNull();
        formulaCell.Element(ss + "Data")!.Attribute(ss + "Type")!.Value.Should().Be("Number");
        formulaCell.Element(ss + "Data")!.Value.Should().Be("19.75");

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedFormulaCell = loadedSheet.GetCell(1, 3)!;
        loadedFormulaCell.FormulaText.Should().Be("SUM(A1:B1)");
        loadedFormulaCell.Value.Should().Be(new NumberValue(19.75));
        loaded.GetStyle(loadedFormulaCell.StyleId).NumberFormat.Should().Be("$#,##0.00");
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
    public void SaveThenLoad_RoundTripsSpreadsheetMlHyperlinkOnlyCells()
    {
        var workbook = new Workbook("XmlHyperlinkOnly");
        var sheet = workbook.AddSheet("Links");
        var address = new CellAddress(sheet.Id, 3, 2);
        sheet.Hyperlinks[address] = "https://example.com/blank";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open blank link",
            "");

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var linkCell = document.Descendants(ss + "Cell")
            .Single(cell => cell.Attribute(ss + "HRef")?.Value == "https://example.com/blank");
        linkCell.Attribute(ss + "HRefScreenTip")!.Value.Should().Be("Open blank link");
        linkCell.Element(ss + "Data").Should().BeNull();

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(loadedSheet.Id, 3, 2);
        loadedSheet.Hyperlinks[loadedAddress].Should().Be("https://example.com/blank");
        loadedSheet.HyperlinkMetadata[loadedAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open blank link",
            ""));
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
    public void Save_TruncatesSeekableOutputStreamBeforeWritingSpreadsheetMl()
    {
        var largeWorkbook = new Workbook("XmlLarge");
        var largeSheet = largeWorkbook.AddSheet("Data");
        largeSheet.SetCell(new CellAddress(largeSheet.Id, 1, 1), new TextValue(new string('x', 8192)));
        var workbook = new Workbook("XmlTruncate");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Gamma"));
        using var stream = new MemoryStream();

        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(largeWorkbook, stream);
        var largeLength = stream.Length;
        stream.Position = 0;

        adapter.Save(workbook, stream);

        stream.Position.Should().Be(stream.Length);
        stream.Length.Should().BeLessThan(largeLength);
        stream.Position = 0;
        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        document.Descendants(ss + "Data").Single().Value.Should().Be("Gamma");
    }

    [Fact]
    public void Benchmark_SaveDenseWorkbook_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        const int sheetCount = 2;
        const int rowCount = 120;
        const int columnCount = 80;
        var workbook = CreateDenseWorkbook(sheetCount, rowCount, columnCount);
        var adapter = new SpreadsheetXmlFileAdapter();

        using (var warmup = new MemoryStream())
            adapter.Save(workbook, warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var packageSizes = new List<long>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream();
            var step = Stopwatch.StartNew();
            adapter.Save(workbook, stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF SPREADSHEET_XML_SAVE_DENSE " +
            $"sheets={sheetCount} rows={rowCount} cols={columnCount} " +
            $"steps={iterations} bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
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
    public void LoadTransformed_LoadsDisableOutputEscapingSpreadsheetMlOutput()
    {
        using var source = StreamFromString("""
            <rows>
              <workbook>&lt;ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"&gt;&lt;ss:Worksheet ss:Name="Generated"&gt;&lt;ss:Table&gt;&lt;ss:Row&gt;&lt;ss:Cell&gt;&lt;ss:Data ss:Type="String"&gt;Alpha&lt;/ss:Data&gt;&lt;/ss:Cell&gt;&lt;/ss:Row&gt;&lt;/ss:Table&gt;&lt;/ss:Worksheet&gt;&lt;/ss:Workbook&gt;</workbook>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" omit-xml-declaration="yes" />
              <xsl:template match="/rows">
                <xsl:value-of select="workbook" disable-output-escaping="yes" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Generated");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
    }

    [Fact]
    public void LoadTransformed_AppliesXsltParametersToGeneratedSpreadsheetMl()
    {
        using var source = StreamFromString("<rows><row amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:param name="sheetName" />
              <xsl:param name="label" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$sheetName}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="$label" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            new Dictionary<string, string?>
            {
                ["sheetName"] = "Parameterized",
                ["label"] = "Runtime label"
            });

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Parameterized");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Runtime label"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_AppliesNamespacedXsltParametersToGeneratedSpreadsheetMl()
    {
        using var source = StreamFromString("<rows />");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:cfg="urn:freex:xslt:test"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:param name="cfg:sheetName" />
              <xsl:param name="cfg:label" />
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$cfg:sheetName}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="$cfg:label" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(
            source,
            stylesheet,
            new Dictionary<string, string?>
            {
                ["{urn:freex:xslt:test}sheetName"] = "Namespaced",
                ["{urn:freex:xslt:test}label"] = "Namespaced label"
            });

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Namespaced");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Namespaced label"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromXsltKeyLookup()
    {
        using var source = StreamFromString("""
            <catalog>
              <categories>
                <category id="A" name="Hardware" />
                <category id="B" name="Services" />
              </categories>
              <items>
                <item sku="100" category="B" amount="42.5" />
                <item sku="101" category="A" amount="7.25" />
              </items>
            </catalog>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:key name="categoryById" match="category" use="@id" />
              <xsl:template match="/catalog">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Lookup">
                    <ss:Table>
                      <xsl:for-each select="items/item">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@sku" /></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="key('categoryById', @category)/@name" /></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@amount" /></ss:Data></ss:Cell>
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
        sheet.Name.Should().Be("Lookup");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("100"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Services"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("101"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Hardware"));
        sheet.GetCell(2, 3)!.Value.Should().Be(new NumberValue(7.25));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromXsltSort()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Gamma" amount="12.5" />
              <row name="Alpha" amount="42.5" />
              <row name="Beta" amount="42.5" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Sorted">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <xsl:sort select="@amount" data-type="number" order="descending" />
                        <xsl:sort select="@name" data-type="text" order="ascending" />
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name" /></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@amount" /></ss:Data></ss:Cell>
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
        sheet.Name.Should().Be("Sorted");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("Gamma"));
        sheet.GetCell(3, 2)!.Value.Should().Be(new NumberValue(12.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromDynamicXsltElementsAndAttributes()
    {
        using var source = StreamFromString("<rows><row sheet=\"Dynamic\" label=\"Alpha\" amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <xsl:element name="ss:Workbook">
                  <xsl:element name="ss:Worksheet">
                    <xsl:attribute name="ss:Name"><xsl:value-of select="row/@sheet" /></xsl:attribute>
                    <xsl:element name="ss:Table">
                      <xsl:element name="ss:Row">
                        <xsl:element name="ss:Cell">
                          <xsl:element name="ss:Data">
                            <xsl:attribute name="ss:Type">String</xsl:attribute>
                            <xsl:value-of select="row/@label" />
                          </xsl:element>
                        </xsl:element>
                        <xsl:element name="ss:Cell">
                          <xsl:element name="ss:Data">
                            <xsl:attribute name="ss:Type">Number</xsl:attribute>
                            <xsl:value-of select="row/@amount" />
                          </xsl:element>
                        </xsl:element>
                      </xsl:element>
                    </xsl:element>
                  </xsl:element>
                </xsl:element>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Dynamic");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlCopiedFromSourceTemplate()
    {
        using var source = StreamFromString("""
            <payload xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <template>
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Copied">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">Alpha</ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number">42.5</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </template>
            </payload>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/payload">
                <xsl:copy-of select="template/*" />
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Copied");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromCalledTemplates()
    {
        using var source = StreamFromString("<rows><row label=\"Alpha\" amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template name="cell">
                <xsl:param name="type" />
                <xsl:param name="value" />
                <ss:Cell><ss:Data ss:Type="{$type}"><xsl:value-of select="$value" /></ss:Data></ss:Cell>
              </xsl:template>
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Templates">
                    <ss:Table>
                      <ss:Row>
                        <xsl:call-template name="cell">
                          <xsl:with-param name="type" select="'String'" />
                          <xsl:with-param name="value" select="row/@label" />
                        </xsl:call-template>
                        <xsl:call-template name="cell">
                          <xsl:with-param name="type" select="'Number'" />
                          <xsl:with-param name="value" select="row/@amount" />
                        </xsl:call-template>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Templates");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromConditionalTemplates()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Alpha" status="ok" note="Ready" />
              <row name="Beta" status="warn" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Conditional">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name" /></ss:Data></ss:Cell>
                          <ss:Cell>
                            <ss:Data ss:Type="String">
                              <xsl:choose>
                                <xsl:when test="@status = 'ok'">Pass</xsl:when>
                                <xsl:otherwise>Review</xsl:otherwise>
                              </xsl:choose>
                            </ss:Data>
                          </ss:Cell>
                          <xsl:if test="@note">
                            <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@note" /></ss:Data></ss:Cell>
                          </xsl:if>
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
        sheet.Name.Should().Be("Conditional");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Pass"));
        sheet.GetCell(1, 3)!.Value.Should().Be(new TextValue("Ready"));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Review"));
        sheet.GetCell(2, 3).Should().BeNull();
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromApplyTemplates()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Alpha" amount="42.5" />
              <row label="Beta" amount="7.25" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Applied">
                    <ss:Table>
                      <xsl:apply-templates select="row" mode="sheet-row" />
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
              <xsl:template match="row" mode="sheet-row">
                <ss:Row>
                  <xsl:apply-templates select="@label | @amount" mode="cell" />
                </ss:Row>
              </xsl:template>
              <xsl:template match="@label" mode="cell">
                <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="." /></ss:Data></ss:Cell>
              </xsl:template>
              <xsl:template match="@amount" mode="cell">
                <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="." /></ss:Data></ss:Cell>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Applied");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Beta"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(7.25));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromVariablesAndAggregates()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Alpha" amount="42.5" />
              <row label="Beta" amount="7.25" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:variable name="sheetName" select="'Variable Summary'" />
              <xsl:template match="/rows">
                <xsl:variable name="rowCount" select="count(row)" />
                <xsl:variable name="total" select="sum(row/@amount)" />
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{$sheetName}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">Rows</ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="$rowCount" /></ss:Data></ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">Total</ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="$total" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Variable Summary");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Rows"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Total"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(49.75));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromNumberInstruction()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Alpha" />
              <row label="Beta" />
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Numbered">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:number value="position()" format="001" /></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@label" /></ss:Data></ss:Cell>
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
        sheet.Name.Should().Be("Numbered");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("001"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("002"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Beta"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromAttributeSet()
    {
        using var source = StreamFromString("<rows><row amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:attribute-set name="moneyCell">
                <xsl:attribute name="ss:StyleID">money</xsl:attribute>
              </xsl:attribute-set>
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="money">
                      <ss:NumberFormat ss:Format="$#,##0.00" />
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Styled">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell xsl:use-attribute-sets="moneyCell">
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data>
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
        sheet.Name.Should().Be("Styled");
        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42.5));
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlScalarValueTypesAndIndexes()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Ready" amount="42.25" active="1" timestamp="2026-05-31T08:15:30" error="#N/A"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Typed">
                    <ss:Table>
                      <ss:Row ss:Index="3">
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@label"/></ss:Data></ss:Cell>
                        <ss:Cell ss:Index="3"><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Boolean"><xsl:value-of select="row/@active"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="DateTime"><xsl:value-of select="row/@timestamp"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Error"><xsl:value-of select="row/@error"/></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Typed");
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("Ready"));
        sheet.GetCell(3, 2).Should().BeNull();
        sheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(42.25));
        sheet.GetCell(3, 4)!.Value.Should().Be(new BoolValue(true));
        sheet.GetCell(3, 5)!.Value.Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 31, 8, 15, 30)));
        sheet.GetCell(3, 6)!.Value.Should().Be(new ErrorValue("#N/A"));
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
                        <ss:Cell ss:Index="3" ss:StyleID="money"/>
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
        sheet.GetCell(1, 3).Should().BeNull();
        workbook.GetStyle(sheet.GetStyleOnly(1, 3)!.Value).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlInheritedNumberFormatStyles()
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
                    <ss:Style ss:ID="moneyGenerated" ss:Parent="money"/>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:StyleID="moneyGenerated">
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
    public void LoadTransformed_InheritsSpreadsheetMlRowAndColumnNumberFormatStyles()
    {
        using var source = StreamFromString("""
            <rows>
              <row first="12.5" second="7.25" override="3.5"/>
              <row first="42.5" second="9.75" override="6.5"/>
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
                    <ss:Style ss:ID="percent">
                      <ss:NumberFormat ss:Format="0.00%"/>
                    </ss:Style>
                    <ss:Style ss:ID="integer">
                      <ss:NumberFormat ss:Format="0"/>
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <ss:Column ss:StyleID="money"/>
                      <ss:Column ss:Index="3" ss:StyleID="integer"/>
                      <ss:Row ss:StyleID="percent">
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@first"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@second"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row/@override"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row[2]/@first"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row[2]/@second"/></ss:Data>
                        </ss:Cell>
                        <ss:Cell>
                          <ss:Data ss:Type="Number"><xsl:value-of select="row[2]/@override"/></ss:Data>
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
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("0.00%");
        workbook.GetStyle(sheet.GetCell(1, 2)!.StyleId).NumberFormat.Should().Be("0.00%");
        workbook.GetStyle(sheet.GetCell(1, 3)!.StyleId).NumberFormat.Should().Be("0.00%");
        workbook.GetStyle(sheet.GetCell(2, 1)!.StyleId).NumberFormat.Should().Be("$#,##0.00");
        sheet.GetCell(2, 2)!.StyleId.Should().Be(StyleId.Default);
        workbook.GetStyle(sheet.GetCell(2, 3)!.StyleId).NumberFormat.Should().Be("0");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlFormulasAndMergedCells()
    {
        using var source = StreamFromString("""
            <rows>
              <row label="Projected total" first="12.5" second="7.25"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Formulas">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell ss:MergeAcross="2">
                          <ss:Data ss:Type="String"><xsl:value-of select="row/@label"/></ss:Data>
                        </ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@first"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@second"/></ss:Data></ss:Cell>
                        <ss:Cell ss:Formula="=SUM(RC[-2]:RC[-1])"><ss:Data ss:Type="Number">19.75</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Formulas");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3)));
        var formulaCell = sheet.GetCell(2, 3);
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("SUM(RC[-2]:RC[-1])");
        formulaCell.Value.Should().Be(new NumberValue(19.75));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlFormulaCellNumberFormatStyle()
    {
        using var source = StreamFromString("""
            <rows>
              <row first="12.5" second="7.25"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Styles>
                    <ss:Style ss:ID="total">
                      <ss:NumberFormat ss:Format="$#,##0.00"/>
                    </ss:Style>
                  </ss:Styles>
                  <ss:Worksheet ss:Name="Formulas">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@first"/></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@second"/></ss:Data></ss:Cell>
                        <ss:Cell ss:Formula="=SUM(RC[-2]:RC[-1])" ss:StyleID="total">
                          <ss:Data ss:Type="Number">19.75</ss:Data>
                        </ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var formulaCell = workbook.GetSheetAt(0).GetCell(1, 3);
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("SUM(RC[-2]:RC[-1])");
        formulaCell.Value.Should().Be(new NumberValue(19.75));
        workbook.GetStyle(formulaCell.StyleId).NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlWorkbookAndSheetMetadata()
    {
        using var source = StreamFromString("""
            <report sheet="Generated">
              <row name="Alpha" amount="12.5"/>
              <row name="Beta" amount="7.25"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:x="urn:schemas-microsoft-com:office:excel">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="GeneratedData" ss:RefersTo="=Generated!A1:B3"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}" ss:Visible="SheetHidden">
                    <ss:Table>
                      <ss:Column ss:Width="18.5"/>
                      <ss:Column ss:Index="3" ss:Hidden="1"/>
                      <ss:Row ss:Height="27.5">
                        <ss:Cell><ss:Data ss:Type="String">Name</ss:Data></ss:Cell>
                        <ss:Cell ss:Index="3"><ss:Data ss:Type="String">Amount</ss:Data></ss:Cell>
                      </ss:Row>
                      <xsl:for-each select="row">
                        <ss:Row ss:Index="{position() + 1}">
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
                          <ss:Cell ss:Index="3"><ss:Data ss:Type="Number"><xsl:value-of select="@amount"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                      <ss:Row ss:Index="4" ss:Hidden="1">
                        <ss:Cell><ss:Data ss:Type="String">Hidden footer</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                    <x:WorksheetOptions>
                      <x:DoNotDisplayGridlines/>
                      <x:Print>
                        <x:Gridlines/>
                      </x:Print>
                      <x:FreezePanes/>
                      <x:FrozenNoSplit/>
                      <x:SplitHorizontal>1</x:SplitHorizontal>
                      <x:TopRowBottomPane>1</x:TopRowBottomPane>
                      <x:SplitVertical>2</x:SplitVertical>
                      <x:LeftColumnRightPane>2</x:LeftColumnRightPane>
                    </x:WorksheetOptions>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Generated");
        sheet.IsHidden.Should().BeTrue();
        sheet.IsVeryHidden.Should().BeFalse();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(2);
        sheet.RowHeights[1].Should().Be(27.5);
        sheet.HiddenRows.Should().Contain(4u);
        sheet.ColumnWidths[1].Should().Be(18.5);
        sheet.HiddenCols.Should().Contain(3u);
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(7.25));
        workbook.NamedRanges["GeneratedData"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2)));
    }

    [Fact]
    public void LoadTransformed_PreservesQuotedSpreadsheetMlNamedRanges()
    {
        using var source = StreamFromString("""
            <report sheet="Q1 Bob's Team">
              <row name="Alpha"/>
              <row name="Beta"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="TeamRows" ss:RefersTo="='Q1 Bob''s Team'!$A$1:$A$2"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
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
        sheet.Name.Should().Be("Q1 Bob's Team");
        workbook.NamedRanges["TeamRows"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1)));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlColumnSpanLayout()
    {
        using var source = StreamFromString("""
            <layout width="21.25"/>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/layout">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Layout">
                    <ss:Table>
                      <ss:Column ss:Index="2" ss:Span="2" ss:Width="{@width}" ss:Hidden="1"/>
                      <ss:Row>
                        <ss:Cell ss:Index="4"><ss:Data ss:Type="String">After span</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(2, 21.25));
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(3, 21.25));
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(4, 21.25));
        sheet.HiddenCols.Should().Contain([2u, 3u, 4u]);
        sheet.GetCell(1, 4)!.Value.Should().Be(new TextValue("After span"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlRowSpanLayout()
    {
        using var source = StreamFromString("""
            <layout height="24.5"/>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/layout">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Layout">
                    <ss:Table>
                      <ss:Row ss:Index="2" ss:Span="2" ss:Height="{@height}" ss:Hidden="1">
                        <ss:Cell><ss:Data ss:Type="String">Spanned row</ss:Data></ss:Cell>
                      </ss:Row>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String">After span</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(2, 24.5));
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(3, 24.5));
        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(4, 24.5));
        sheet.HiddenRows.Should().Contain([2u, 3u, 4u]);
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Spanned row"));
        sheet.GetCell(5, 1)!.Value.Should().Be(new TextValue("After span"));
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
    public void LoadTransformed_RejectsStylesheetEmittedDtdOutput()
    {
        using var source = StreamFromString("<rows/>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" doctype-system="freex-workbook.dtd" omit-xml-declaration="yes" />
              <xsl:template match="/">
                <ss:Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
                  <ss:Worksheet ss:Name="Bad"><ss:Table /></ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var act = () => SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*XSLT transform output*")
            .WithInnerException<XmlException>();
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

    private static MemoryStream StreamFromString(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static Workbook CreateDenseWorkbook(int sheetCount, int rowCount, int columnCount)
    {
        var workbook = new Workbook("SpreadsheetML Dense");
        var currency = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var percent = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        for (var sheetIndex = 1; sheetIndex <= sheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet {sheetIndex}");
            for (uint row = 1; row <= rowCount; row++)
            {
                for (uint column = 1; column <= columnCount; column++)
                {
                    var address = new CellAddress(sheet.Id, row, column);
                    var selector = (row + column + (uint)sheetIndex) % 11;
                    if (selector == 0)
                    {
                        sheet.SetCell(address, new Cell
                        {
                            FormulaText = $"SUM(A{Math.Max(1u, row - 1)}:A{row})",
                            Value = new NumberValue(row + column),
                            StyleId = currency
                        });
                    }
                    else if (selector == 1)
                    {
                        sheet.SetCell(address, new Cell
                        {
                            Value = new NumberValue(row * column),
                            StyleId = percent
                        });
                    }
                    else if (selector == 2)
                    {
                        sheet.SetCell(address, new TextValue($"R{row}C{column}"));
                    }
                    else
                    {
                        sheet.SetCell(address, new NumberValue(row + column + (uint)sheetIndex));
                    }
                }
            }
        }

        return workbook;
    }

    private static MemoryStream PositionedStreamFromString(string prefix, string value)
    {
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var stream = new MemoryStream(prefixBytes.Concat(valueBytes).ToArray());
        stream.Position = prefixBytes.Length;
        return stream;
    }
}
