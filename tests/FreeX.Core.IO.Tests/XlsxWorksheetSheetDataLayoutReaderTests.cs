using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetSheetDataLayoutReaderTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14AcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac";

    [Fact]
    public void ReadSheetDataLayout_ParsesDirectRowColumnAndCellMetadata()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "cols",
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "2"),
                        new XAttribute("max", "3"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("outlineLevel", "1"),
                        new XAttribute("collapsed", "1"),
                        new XAttribute("customWidth", "1"),
                        new XAttribute("width", "12.75")),
                    new XElement(
                        WorksheetNs + "col",
                        new XAttribute("min", "4"),
                        new XAttribute("max", "4"),
                        new XAttribute("collapsed", "1"))),
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "5"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("ht", "18"),
                        new XAttribute("customHeight", "1"),
                        new XAttribute("outlineLevel", "2"),
                        new XAttribute("collapsed", "1"),
                        Cell("A5", style: "4"),
                        Cell("B5", null, "e", new XElement(WorksheetNs + "f", "1/0"), new XElement(WorksheetNs + "v", "#DIV/0!")),
                        Cell("C5", "6", null, new XElement(WorksheetNs + "v", "text"))),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "6"),
                        new XAttribute("collapsed", "1"))),
                new XElement(WorksheetNs + "extLst",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "9"),
                        new XAttribute("hidden", "1"),
                        Cell("D9", style: "7")),
                    new XElement(
                        WorksheetNs + "cols",
                        new XElement(
                            WorksheetNs + "col",
                            new XAttribute("min", "9"),
                            new XAttribute("max", "9"),
                            new XAttribute("hidden", "1"))))));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        layout.RowColumnLayout.HiddenRows.Should().BeEmpty();
        layout.RowColumnLayout.RowHeights[5].Should().BeApproximately(24, 0.0001);
        layout.RowColumnLayout.RowOutlineLevels.Should().Contain(5u, 2);
        layout.RowColumnLayout.GroupHiddenRows.Should().Equal(5u);
        layout.RowColumnLayout.HiddenCols.Should().BeEmpty();
        layout.RowColumnLayout.ColumnWidths.Should().Contain(2u, 12.75);
        layout.RowColumnLayout.ColumnWidths.Should().Contain(3u, 12.75);
        layout.RowColumnLayout.ColOutlineLevels.Should().Contain(2u, 1);
        layout.RowColumnLayout.ColOutlineLevels.Should().Contain(3u, 1);
        layout.RowColumnLayout.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u]);
        layout.RowColumnLayout.CollapsedAnchorRows.Should().BeEquivalentTo([5u, 6u]);
        layout.RowColumnLayout.CollapsedAnchorCols.Should().BeEquivalentTo([2u, 3u, 4u]);

        layout.CellLayout.HasStyleOnlyCells.Should().BeTrue();
        layout.CellLayout.ExplicitStyleOnlyCells.Should().Equal((5u, 1u, 4));
        layout.CellLayout.CachedFormulaErrors.Should().Equal(new Dictionary<(uint Row, uint Col), ErrorValue>
        {
            [(5, 2)] = ErrorValue.DivByZero
        });
        layout.CellLayout.PopulatedCellCount.Should().Be(2);
    }

    [Fact]
    public void ReadSheetDataLayout_StreamingParserMatchesDirectSheetDataAndFlagsNativeMetadata()
    {
        var sheetData = new XElement(
            WorksheetNs + "sheetData",
            new XElement(
                WorksheetNs + "row",
                new XAttribute("r", "5"),
                new XAttribute("hidden", "1"),
                new XAttribute("ht", "18"),
                new XAttribute("customHeight", "1"),
                new XAttribute("outlineLevel", "2"),
                new XAttribute("collapsed", "1"),
                new XAttribute("thickTop", "1"),
                Cell("A5", style: "4"),
                Cell(
                    "B5",
                    null,
                    "e",
                    new XElement(
                        WorksheetNs + "f",
                        new XAttribute("t", "shared"),
                        "1/0"),
                    new XElement(WorksheetNs + "v", "#DIV/0!")),
                Cell(
                    "C5",
                    "6",
                    "inlineStr",
                    new XElement(
                        WorksheetNs + "is",
                        new XElement(
                            WorksheetNs + "r",
                            new XElement(WorksheetNs + "t", "rich"))))),
            new XElement(
                WorksheetNs + "row",
                new XAttribute("r", "6"),
                new XAttribute("collapsed", "1")));
        var worksheet = new XDocument(new XElement(WorksheetNs + "worksheet", sheetData));

        using var reader = worksheet.Root!.Element(WorksheetNs + "sheetData")!.CreateReader();

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(reader, WorksheetNs);

        layout.RowColumnLayout.HiddenRows.Should().BeEmpty();
        layout.RowColumnLayout.RowHeights[5].Should().BeApproximately(24, 0.0001);
        layout.RowColumnLayout.RowOutlineLevels.Should().Contain(5u, 2);
        layout.RowColumnLayout.GroupHiddenRows.Should().Equal(5u);
        layout.RowColumnLayout.CollapsedAnchorRows.Should().BeEquivalentTo([5u, 6u]);
        layout.CellLayout.HasStyleOnlyCells.Should().BeTrue();
        layout.CellLayout.ExplicitStyleOnlyCells.Should().Equal((5u, 1u, 4));
        layout.CellLayout.ExplicitPopulatedCellStyles.Should().Equal((5u, 3u, 6));
        layout.CellLayout.CachedFormulaErrors.Should().Equal(new Dictionary<(uint Row, uint Col), ErrorValue>
        {
            [(5, 2)] = ErrorValue.DivByZero
        });
        layout.CellLayout.PopulatedCellCount.Should().Be(2);
        layout.HasPreservableSourceSheetDataMetadata.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(SheetDataPreservableMetadataCases))]
    public void ReadSheetDataLayout_StreamingMetadataDetectionMatchesWorksheetPreflight(
        XElement sheetData,
        bool expected)
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet", new XElement(sheetData)));
        using var reader = worksheet.Root!.Element(WorksheetNs + "sheetData")!.CreateReader();

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(reader, WorksheetNs);

        layout.HasPreservableSourceSheetDataMetadata.Should().Be(expected);
        HasPreservableSourceWorksheetMetadataByEntry(worksheet)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ReadSheetDataLayout_TreatsNonCustomTallRowHeightAsAutofitDisplayHint()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "1"),
                        new XAttribute("ht", "15.75")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "2"),
                        new XAttribute("ht", "60.75")),
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "3"),
                        new XAttribute("ht", "18"),
                        new XAttribute("customHeight", "1")))));

        var layout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheet, WorksheetNs);

        layout.RowColumnLayout.RowHeights[1].Should().BeApproximately(20, 0.0001);
        layout.RowColumnLayout.RowHeights[2].Should().BeApproximately(59, 0.0001);
        layout.RowColumnLayout.RowHeights[3].Should().BeApproximately(24, 0.0001);
    }

    [Fact]
    public void LoadSheetDataLayout_UsesDirectSheetDataAndColumnScans()
    {
        var rowColumnSource = Source("XlsxWorksheetRowColumnLayoutReader.cs");
        var cellSource = Source("XlsxWorksheetCellLayoutReader.cs");
        var adapterSource = Source("XlsxFileAdapter.SheetXmlLayout.cs");

        rowColumnSource.Should().Contain("ReadSheetDataLayout(worksheetXml, worksheetNs)");
        rowColumnSource.Should().Contain("root.Element(worksheetNs + \"sheetData\")?.Elements(rowName)");
        rowColumnSource.Should().Contain("root.Elements(worksheetNs + \"cols\")");
        rowColumnSource.Should().Contain("XmlReader reader,");
        rowColumnSource.Should().Contain("detectPreservableSourceSheetDataMetadata");
        rowColumnSource.Should().NotContain("worksheetXml.Descendants(worksheetNs + \"row\")");
        rowColumnSource.Should().NotContain("worksheetXml.Descendants(worksheetNs + \"col\")");
        cellSource.Should().Contain("ReadSheetDataCells(");
        cellSource.Should().NotContain("worksheetXml.Descendants(worksheetNs + \"c\")");
        adapterSource.Should().Contain("TryLoadWorksheetXmlWithoutSheetData(");
        adapterSource.Should().Contain("detectPreservableSourceSheetDataMetadata: true");
        adapterSource.Should().NotContain("HasPreservableSourceWorksheetMetadata(worksheetEntry, worksheetNs)");
        adapterSource.Should().NotContain("ReadCachedFormulaErrors(worksheetXml, worksheetNs)");
        adapterSource.Should().NotContain("ReadExplicitStyleOnlyCells(worksheetXml, worksheetNs)");
    }

    public static TheoryData<XElement, bool> SheetDataPreservableMetadataCases() => new()
    {
        {
            new XElement(
                WorksheetNs + "sheetData",
                new XElement(
                    WorksheetNs + "row",
                    new XAttribute("r", "1"),
                    new XAttribute("spans", "1:2"),
                    new XAttribute(X14AcNs + "dyDescent", "0.25"),
                    Cell("A1", null, null, new XElement(WorksheetNs + "v", "1")),
                    Cell("B1", null, null, new XElement(WorksheetNs + "f", "A1+1"), new XElement(WorksheetNs + "v", "2")))),
            false
        },
        {
            new XElement(
                WorksheetNs + "sheetData",
                new XElement(
                    WorksheetNs + "row",
                    new XAttribute("r", "1"),
                    new XAttribute("nativeRowFlag", "1"),
                    Cell("A1", null, null, new XElement(WorksheetNs + "v", "1")))),
            true
        },
        {
            new XElement(
                WorksheetNs + "sheetData",
                new XElement(
                    WorksheetNs + "row",
                    new XAttribute("r", "1"),
                    new XElement(
                        WorksheetNs + "c",
                        new XAttribute("r", "A1"),
                        new XAttribute("cm", "1"),
                        new XElement(WorksheetNs + "v", "1")))),
            true
        },
        {
            new XElement(
                WorksheetNs + "sheetData",
                new XElement(
                    WorksheetNs + "row",
                    new XAttribute("r", "1"),
                    new XElement(
                        WorksheetNs + "c",
                        new XAttribute("r", "A1"),
                        new XElement(WorksheetNs + "v", "1"),
                        new XElement(WorksheetNs + "extLst")))),
            true
        },
        {
            new XElement(
                WorksheetNs + "sheetData",
                new XElement(
                    WorksheetNs + "row",
                    new XAttribute("r", "1"),
                    Cell(
                        "A1",
                        null,
                        null,
                        new XElement(WorksheetNs + "f", new XAttribute("t", "shared"), "A2"),
                        new XElement(WorksheetNs + "v", "1")))),
            true
        },
        {
            new XElement(
                WorksheetNs + "sheetData",
                new XElement(
                    WorksheetNs + "row",
                    new XAttribute("r", "1"),
                    Cell(
                        "A1",
                        null,
                        "inlineStr",
                        new XElement(
                            WorksheetNs + "is",
                            new XElement(
                                WorksheetNs + "r",
                                new XElement(WorksheetNs + "t", "rich")))))),
            true
        },
        {
            new XElement(
                WorksheetNs + "sheetData",
                new XElement(WorksheetNs + "phoneticPr")),
            true
        }
    };

    private static XElement Cell(string reference, string? style = null, string? type = null, params object[] content)
    {
        var cell = new XElement(WorksheetNs + "c", content);
        cell.SetAttributeValue("r", reference);
        if (style is not null)
            cell.SetAttributeValue("s", style);
        if (type is not null)
            cell.SetAttributeValue("t", type);
        return cell;
    }

    private static bool HasPreservableSourceWorksheetMetadataByEntry(XDocument worksheet)
    {
        using var package = new MemoryStream();
        using (var createArchive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = createArchive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(entry.Open());
            worksheet.Save(writer, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
        using var readArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = readArchive.GetEntry("xl/worksheets/sheet1.xml")!;
        return XlsxWorksheetMetadataPreserver.HasPreservableSourceWorksheetMetadata(worksheetEntry, WorksheetNs);
    }

    private static string Source(string fileName) =>
        TestWorkspaceFiles.ReadCoreIoSource(fileName);
}
