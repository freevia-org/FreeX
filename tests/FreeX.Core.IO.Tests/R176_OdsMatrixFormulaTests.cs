using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r176: the ODS reader had no notion of an ODF matrix (array) formula at all -- it stamped
/// FormulaArrayMode.Implicit onto EVERY formula cell. A matrix formula therefore loaded as an ordinary
/// formula whose range result resolves by positional implicit intersection against the formula cell's own
/// row/column, instead of the declared array's top-left element. That is the same defect fixed in
/// XlsxFileAdapter (freex-array-formulas F1) and LegacyXlsFileAdapter (F2); ODS was the third adapter, and
/// went unaddressed because it was never wired for matrices in the first place.
/// </summary>
public sealed class R176_OdsMatrixFormulaTests
{
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    [Fact]
    public void Load_SingleCellMatrixFormula_TakesTopLeftElementNotPositionalIntersection()
    {
        // A1:A5 = 10..50, and C3 holds a 1x1-declared matrix over A1:A5 -- a genuine single-cell CSE
        // array whose body is a multi-cell range that does NOT start at the formula cell's own row.
        using var stream = BuildOds(
            [10, 20, 30, 40, 50],
            row: 3, col: 3, formula: "of:=[.A1:.A5]", matrixRows: 1, matrixCols: 1, cached: 10);

        var workbook = new OdsFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var anchor = sheet.GetCell(3, 3);
        anchor.Should().NotBeNull();
        anchor!.LegacyArrayRows.Should().Be(1u,
            "a declared matrix must route through the LegacyArrayRows/Cols confinement machinery, the " +
            "same one the XLSX and .xls loaders use, so it takes the top-left element");
        anchor.LegacyArrayCols.Should().Be(1u);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(workbook);

        sheet.GetValue(3, 3).Should().Be(new NumberValue(10),
            "the declared array's top-left element is A1=10; positional intersection would instead pick " +
            "the element sharing C3's own row (A3=30)");
    }

    [Fact]
    public void Load_MultiCellMatrixFormula_ConfinesResultToDeclaredExtent()
    {
        // C1 declares a 3x1 matrix over A1:A5: only the first three elements are shown, and the whole
        // declared block behaves as one array.
        using var stream = BuildOds(
            [10, 20, 30, 40, 50],
            row: 1, col: 3, formula: "of:=[.A1:.A5]", matrixRows: 3, matrixCols: 1, cached: 10);

        var workbook = new OdsFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        sheet.GetCell(1, 3)!.LegacyArrayRows.Should().Be(3u);
        sheet.GetCell(1, 3)!.LegacyArrayCols.Should().Be(1u);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(10));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(20));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Load_OrdinaryFormula_KeepsImplicitIntersection()
    {
        // The matrix branch must not capture ordinary formulas: with no matrix attributes, a
        // range-valued formula still resolves by implicit intersection, which is correct for a
        // non-array formula and is the behaviour every ODS formula cell had before r176.
        using var stream = BuildOds(
            [10, 20, 30, 40, 50],
            row: 3, col: 3, formula: "of:=[.A1:.A5]", matrixRows: 0, matrixCols: 0, cached: 30);

        var workbook = new OdsFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var anchor = sheet.GetCell(3, 3);
        anchor!.LegacyArrayRows.Should().Be(0u);
        anchor.ArrayMode.Should().Be(FormulaArrayMode.Implicit);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(workbook);

        sheet.GetValue(3, 3).Should().Be(new NumberValue(30),
            "an ordinary (non-matrix) formula still intersects positionally against its own row");
    }

    [Fact]
    public void Save_DeclaredArray_EmitsMatrixAttributesSoItSurvivesAReopen()
    {
        // Without the writer half, a correctly-loaded matrix degrades back to an implicit-intersection
        // formula on the next open -- a silent round-trip loss of the array.
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), Cell.FromValue(new NumberValue(r * 10)));

        var anchor = Cell.FromFormula("=A1:A5");
        anchor.ArrayMode = FormulaArrayMode.Dynamic;
        anchor.LegacyArrayRows = 1;
        anchor.LegacyArrayCols = 1;
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), anchor);

        var adapter = new OdsFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var contentStream = archive.GetEntry("content.xml")!.Open();
            var content = XDocument.Load(contentStream);
            var matrixAnchor = content.Descendants(XName.Get("table-cell", TableNs))
                .FirstOrDefault(c => c.Attribute(XName.Get("number-matrix-rows-spanned", TableNs)) is not null);
            matrixAnchor.Should().NotBeNull("the declared array extent must be written as ODF matrix attributes");
            matrixAnchor!.Attribute(XName.Get("number-matrix-columns-spanned", TableNs))!.Value.Should().Be("1");
        }

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedAnchor = reloaded.GetSheetAt(0).GetCell(3, 3);
        reloadedAnchor!.LegacyArrayRows.Should().Be(1u, "the array must survive save -> load");
        reloadedAnchor.LegacyArrayCols.Should().Be(1u);
    }

    /// <summary>
    /// Builds a minimal .ods whose Sheet1 has column A populated from <paramref name="columnA"/> plus one
    /// further formula cell, optionally declared as a matrix. Passing 0 for the span counts omits the
    /// matrix attributes entirely, producing an ordinary formula cell.
    /// </summary>
    private static MemoryStream BuildOds(
        double[] columnA,
        uint row,
        uint col,
        string formula,
        uint matrixRows,
        uint matrixCols,
        double cached)
    {
        XNamespace office = OfficeNs;
        XNamespace table = TableNs;
        XNamespace text = TextNs;

        var rows = new List<XElement>();
        var lastRow = Math.Max((uint)columnA.Length, row);
        for (uint r = 1; r <= lastRow; r++)
        {
            var cells = new List<XElement>();
            var lastCol = r == row ? col : 1u;
            for (uint c = 1; c <= lastCol; c++)
            {
                if (c == 1 && r <= columnA.Length)
                {
                    var literal = columnA[r - 1].ToString(CultureInfo.InvariantCulture);
                    cells.Add(new XElement(table + "table-cell",
                        new XAttribute(office + "value-type", "float"),
                        new XAttribute(office + "value", literal),
                        new XElement(text + "p", literal)));
                    continue;
                }

                if (r == row && c == col)
                {
                    var cell = new XElement(table + "table-cell",
                        new XAttribute(table + "formula", formula),
                        new XAttribute(office + "value-type", "float"),
                        new XAttribute(office + "value", cached.ToString(CultureInfo.InvariantCulture)));
                    if (matrixRows > 0)
                    {
                        cell.Add(new XAttribute(table + "number-matrix-rows-spanned", matrixRows));
                        cell.Add(new XAttribute(table + "number-matrix-columns-spanned", matrixCols));
                    }
                    cells.Add(cell);
                    continue;
                }

                cells.Add(new XElement(table + "table-cell"));
            }

            rows.Add(new XElement(table + "table-row", cells));
        }

        var content = new XDocument(
            new XElement(office + "document-content",
                new XAttribute(XNamespace.Xmlns + "office", OfficeNs),
                new XAttribute(XNamespace.Xmlns + "table", TableNs),
                new XAttribute(XNamespace.Xmlns + "text", TextNs),
                new XElement(office + "body",
                    new XElement(office + "spreadsheet",
                        new XElement(table + "table",
                            new XAttribute(table + "name", "Sheet1"),
                            rows)))));

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetype = archive.CreateEntry("mimetype");
            using (var writer = new StreamWriter(mimetype.Open(), Encoding.ASCII))
                writer.Write("application/vnd.oasis.opendocument.spreadsheet");

            var entry = archive.CreateEntry("content.xml");
            using var entryStream = entry.Open();
            content.Save(entryStream);
        }

        stream.Position = 0;
        return stream;
    }
}
