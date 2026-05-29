using System.Xml.Linq;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetCellLayoutReaderTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void ReadExplicitStyleOnlyCells_ParsesStyledBlankCellsWithoutGeneratingSheetIds()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetData",
                    new XElement(WorksheetNs + "row",
                        Cell("A1", style: "3"),
                        Cell("XFD1048576", style: "4"),
                        Cell("B1", "5", null, new XElement(WorksheetNs + "v", "text")),
                        Cell("C1", "6", null, new XElement(WorksheetNs + "f", "A1")),
                        Cell("D1", "7", null, new XElement(WorksheetNs + "is")),
                        Cell("E1"),
                        Cell("NotARef", style: "8")))));

        var cells = XlsxWorksheetCellLayoutReader.ReadExplicitStyleOnlyCells(worksheet, WorksheetNs);

        cells.Should().Equal(
            (1u, 1u, 3),
            (1048576u, 16384u, 4));
        Source().Should().NotContain("SheetId.New()");
    }

    [Fact]
    public void ReadCachedFormulaErrors_MapsFormulaErrorCellsOnly()
    {
        var worksheet = new XDocument(
            new XElement(WorksheetNs + "worksheet",
                new XElement(WorksheetNs + "sheetData",
                    new XElement(WorksheetNs + "row",
                        Cell("A1", null, "e", new XElement(WorksheetNs + "f", "1/0"), new XElement(WorksheetNs + "v", "#DIV/0!")),
                        Cell("B2", null, "e", new XElement(WorksheetNs + "f", "CIRCULAR"), new XElement(WorksheetNs + "v", "#CIRCULAR!")),
                        Cell("C3", null, "e", new XElement(WorksheetNs + "v", "#VALUE!")),
                        Cell("D4", null, "n", new XElement(WorksheetNs + "f", "1/0"), new XElement(WorksheetNs + "v", "#DIV/0!")),
                        Cell("NotARef", null, "e", new XElement(WorksheetNs + "f", "1/0"), new XElement(WorksheetNs + "v", "#REF!"))))));

        var errors = XlsxWorksheetCellLayoutReader.ReadCachedFormulaErrors(worksheet, WorksheetNs);

        errors.Should().Equal(new Dictionary<(uint Row, uint Col), ErrorValue>
        {
            [(1, 1)] = ErrorValue.DivByZero,
            [(2, 2)] = ErrorValue.Circular
        });
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

    private static string Source() =>
        File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxWorksheetCellLayoutReader.cs"));

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
