using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetSheetDataLayoutReaderTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

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
                        new XAttribute("width", "12.75"))),
                new XElement(WorksheetNs + "sheetData",
                    new XElement(
                        WorksheetNs + "row",
                        new XAttribute("r", "5"),
                        new XAttribute("hidden", "1"),
                        new XAttribute("ht", "18"),
                        new XAttribute("outlineLevel", "2"),
                        new XAttribute("collapsed", "1"),
                        Cell("A5", style: "4"),
                        Cell("B5", null, "e", new XElement(WorksheetNs + "f", "1/0"), new XElement(WorksheetNs + "v", "#DIV/0!")),
                        Cell("C5", "6", null, new XElement(WorksheetNs + "v", "text")))),
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

        layout.RowColumnLayout.HiddenRows.Should().Equal(5u);
        layout.RowColumnLayout.RowHeights[5].Should().BeApproximately(24, 0.0001);
        layout.RowColumnLayout.RowOutlineLevels.Should().Contain(5u, 2);
        layout.RowColumnLayout.GroupHiddenRows.Should().Equal(5u);
        layout.RowColumnLayout.HiddenCols.Should().Equal(2u, 3u);
        layout.RowColumnLayout.ColumnWidths.Should().Contain(2u, 12);
        layout.RowColumnLayout.ColumnWidths.Should().Contain(3u, 12);
        layout.RowColumnLayout.ColOutlineLevels.Should().Contain(2u, 1);
        layout.RowColumnLayout.ColOutlineLevels.Should().Contain(3u, 1);
        layout.RowColumnLayout.GroupHiddenCols.Should().Equal(2u, 3u);

        layout.CellLayout.ExplicitStyleOnlyCells.Should().Equal((5u, 1u, 4));
        layout.CellLayout.CachedFormulaErrors.Should().Equal(new Dictionary<(uint Row, uint Col), ErrorValue>
        {
            [(5, 2)] = ErrorValue.DivByZero
        });
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
        rowColumnSource.Should().NotContain("worksheetXml.Descendants(worksheetNs + \"row\")");
        rowColumnSource.Should().NotContain("worksheetXml.Descendants(worksheetNs + \"col\")");
        cellSource.Should().Contain("ReadSheetDataCells(");
        cellSource.Should().NotContain("worksheetXml.Descendants(worksheetNs + \"c\")");
        adapterSource.Should().Contain("var sheetDataLayout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheetXml, worksheetNs);");
        adapterSource.Should().NotContain("ReadCachedFormulaErrors(worksheetXml, worksheetNs)");
        adapterSource.Should().NotContain("ReadExplicitStyleOnlyCells(worksheetXml, worksheetNs)");
    }

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

    private static string Source(string fileName) =>
        File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", fileName));

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
