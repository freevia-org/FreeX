using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
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
    public void Save_SkipsOutOfBoundsSpreadsheetMlNamedRanges()
    {
        var workbook = new Workbook("XmlInvalidNames");
        var sheet = workbook.AddSheet("Data");
        workbook.DefineNamedRange(
            "ValidName",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)));
        workbook.NamedRanges["InvalidName"] = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 2));

        using var stream = new MemoryStream();
        new SpreadsheetXmlFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var namedRange = document.Descendants(ss + "NamedRange").Should().ContainSingle().Which;
        namedRange.Attribute(ss + "Name")!.Value.Should().Be("ValidName");
        namedRange.Attribute(ss + "RefersTo")!.Value.Should().Be("=Data!A1:B2");
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
    public void Save_OrdersValueCellsInsertedOutOfOrder()
    {
        var workbook = new Workbook("XmlUnorderedValues");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("B2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("C1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));

        using var stream = new MemoryStream();
        var adapter = new SpreadsheetXmlFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var rows = document.Descendants(ss + "Row").ToList();
        rows.Select(row => row.Attribute(ss + "Index")!.Value).Should().Equal("1", "2");
        rows[0].Elements(ss + "Cell").Select(cell => cell.Attribute(ss + "Index")!.Value).Should().Equal("1", "3");
        rows[1].Elements(ss + "Cell").Select(cell => cell.Attribute(ss + "Index")!.Value).Should().Equal("1", "2");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("A1"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("C1"));
        loaded.GetCell(2, 1)!.Value.Should().Be(new TextValue("A2"));
        loaded.GetCell(2, 2)!.Value.Should().Be(new TextValue("B2"));
    }

    [Fact]
    public void SaveValueStreaming_UsesCompactXmlAndSkipsEmptyRowLayoutAllocation()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "SpreadsheetXmlFileAdapter.cs"));

        source.Should().Contain("Indent = false");
        source.Should().Contain("if (sheet.RowHeights.Count == 0 && sheet.HiddenRows.Count == 0)");
        source.Should().Contain("return [];");
    }

    [Fact]
    public void SaveValueStreaming_FormatsNumericHotPathWithoutPerValueStringAllocation()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "SpreadsheetXmlFileAdapter.Write.cs"));

        source.Should().Contain("[ThreadStatic]");
        source.Should().Contain("WriteRoundTripDoubleText(writer, number.Value);");
        source.Should().Contain("value.TryFormat(buffer.AsSpan(), out var charsWritten, \"R\", CultureInfo.InvariantCulture)");
        source.Should().Contain("writer.WriteChars(buffer, 0, charsWritten);");
        source.Should().Contain("value.TryFormat(buffer.AsSpan(), out var charsWritten, provider: CultureInfo.InvariantCulture)");
        source.Should().NotContain("NumberValue number when double.IsFinite(number.Value) => (\"Number\", number.Value.ToString");
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
    public void Save_IgnoresOutOfBoundsSpreadsheetMlCellsAndRanges()
    {
        var workbook = new Workbook("XmlInvalidBounds");
        var sheet = workbook.AddSheet("Data");
        var validAddress = new CellAddress(sheet.Id, 1, 1);
        var invalidRow = new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1);
        var invalidColumn = new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1);
        sheet.SetCell(validAddress, new TextValue("kept"));
        sheet.SetCell(invalidRow, new TextValue("drop-row"));
        sheet.SetCell(invalidColumn, new TextValue("drop-column"));
        sheet.Comments[invalidRow] = "drop comment";
        sheet.Hyperlinks[invalidColumn] = "https://example.invalid/drop";
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00" });
        sheet.SetStyleOnly(CellAddress.MaxRow + 1, 2, styleId);
        sheet.AddMergedRegion(new GridRange(
            validAddress,
            invalidColumn));

        using var stream = new MemoryStream();
        new SpreadsheetXmlFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        var document = XDocument.Load(stream);
        XNamespace ss = "urn:schemas-microsoft-com:office:spreadsheet";
        var cells = document.Descendants(ss + "Cell").ToList();
        cells.Should().ContainSingle();
        cells.Single().Element(ss + "Data")!.Value.Should().Be("kept");
        cells.Single().Attribute(ss + "MergeAcross").Should().BeNull();
        document.ToString(SaveOptions.DisableFormatting).Should().NotContain("drop-");
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

}
