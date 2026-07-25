using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R90-io-table-style-banding-5-1 / R90-io-table-style-banding-5-2 regression tests.
///
/// These exercise <see cref="XlsxStructuredTableModelMapper.MaterializeStyle"/> through the real
/// product entry point: <see cref="XlsxFileAdapter.Load"/> loading an on-disk xlsx package whose
/// <c>styles.xml</c> defines a CUSTOM &lt;tableStyle&gt; (the load path calls MaterializeStyle right
/// after building each table — <see cref="XlsxFileAdapter"/>'s per-sheet load loop).
///
/// Bug 1 (5-1): a custom style's firstColumn/lastColumn dxf used to be scoped to the table's FULL
/// height (including the header and totals rows), so it got applied on top of headerRow/totalRow and
/// stomped the header/totals corner cell whenever the style didn't define firstHeaderCell/
/// firstTotalCell/lastHeaderCell/lastTotalCell overrides (very common for custom styles).
///
/// Bug 2 (5-2): MaterializeStyle unconditionally overwrote a data-body cell's pre-existing explicit
/// fill with the table style's dynamic fill, losing a direct Format-Cells fill the user (or source
/// file) had set on that cell — inverting Excel's direct-format-wins-over-table-style precedence.
/// </summary>
public sealed class R90_CustomTableStyleBandingTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // -------------------------------------------------------------------------
    // Bug 1 — firstColumn/lastColumn must not stomp the header/totals corner
    // -------------------------------------------------------------------------

    [Fact]
    public void HeaderAndTotalsCorner_KeepHeaderRowAndTotalRowFill_NotStompedByFirstOrLastColumn()
    {
        // Style defines headerRow/totalRow/firstColumn/lastColumn but deliberately NOT
        // firstHeaderCell/lastHeaderCell/firstTotalCell/lastTotalCell — the common case the finding
        // describes, where the corner must fall back to headerRow/totalRow, never to firstColumn.
        using var stream = BuildCornerCellPackage();

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var a1 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!.StyleId);
        var c1 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 1, 3))!.StyleId);
        var a4 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 4, 1))!.StyleId);

        a1.FillColor.Should().Be(HeaderFill,
            "the header row's first-column cell must keep headerRow's fill, not firstColumn's, " +
            "because firstColumn only governs the data body");
        c1.FillColor.Should().Be(HeaderFill,
            "the header row's last-column cell must keep headerRow's fill, not lastColumn's");
        a4.FillColor.Should().Be(TotalFill,
            "the totals row's first-column cell must keep totalRow's fill, not firstColumn's");
    }

    [Fact]
    public void DataBodyFirstAndLastColumn_StillGetFirstLastColumnFill_NoRegression()
    {
        // No-regression sibling: the actual data-body cells of the first/last column must still
        // receive the firstColumn/lastColumn dxf — only the header/totals corner scoping changed.
        using var stream = BuildCornerCellPackage();

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var a2 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId);
        var a3 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.StyleId);
        var c2 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 3))!.StyleId);

        a2.FillColor.Should().Be(FirstColumnFill, "data-body first-column cells must keep receiving firstColumn's fill");
        a3.FillColor.Should().Be(FirstColumnFill, "data-body first-column cells must keep receiving firstColumn's fill");
        c2.FillColor.Should().Be(LastColumnFill, "data-body last-column cells must keep receiving lastColumn's fill");
    }

    private static readonly CellColor HeaderFill = CellColor.FromArgb(0x00, 0x00, 0x80);
    private static readonly CellColor TotalFill = CellColor.FromArgb(0x40, 0x40, 0x40);
    private static readonly CellColor FirstColumnFill = CellColor.FromArgb(0xCC, 0xCC, 0xCC);
    private static readonly CellColor LastColumnFill = CellColor.FromArgb(0x00, 0xFF, 0x00);

    private static MemoryStream BuildCornerCellPackage()
    {
        var workbook = new Workbook("CornerCellTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Id"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Bar"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "CornerTable",
            DisplayName = "CornerTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            HasAutoFilter = true,
            TotalsRowShown = true,
            StyleName = "CustomBandStyle",
            ShowFirstColumn = true,
            ShowLastColumn = true,
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Id"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Amount"));
        sheet.StructuredTables.Add(table);

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        PatchStylesXmlWithCustomTableStyle(
            stream,
            styleName: "CustomBandStyle",
            dxfXmls:
            [
                "<dxf><fill><patternFill><fgColor rgb=\"FF000080\"/></patternFill></fill>" +
                "<font><b/><color rgb=\"FFFFFFFF\"/></font></dxf>",
                "<dxf><fill><patternFill><fgColor rgb=\"FF404040\"/></patternFill></fill></dxf>",
                "<dxf><fill><patternFill><fgColor rgb=\"FFCCCCCC\"/></patternFill></fill><font><b/></font></dxf>",
                "<dxf><fill><patternFill><fgColor rgb=\"FF00FF00\"/></patternFill></fill></dxf>"
            ],
            tableStyleElementXmls:
            [
                "<tableStyleElement type=\"headerRow\" dxfId=\"0\"/>",
                "<tableStyleElement type=\"totalRow\" dxfId=\"1\"/>",
                "<tableStyleElement type=\"firstColumn\" dxfId=\"2\"/>",
                "<tableStyleElement type=\"lastColumn\" dxfId=\"3\"/>"
            ]);

        stream.Position = 0;
        return stream;
    }

    // -------------------------------------------------------------------------
    // Bug 2 — a pre-existing direct fill must survive custom-style materialization
    // -------------------------------------------------------------------------

    [Fact]
    public void DataBodyCell_WithPreexistingDirectFill_KeepsItInsteadOfTableStyleFill()
    {
        using var stream = BuildDirectFillPackage();

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var b3 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.StyleId);

        b3.FillColor.Should().Be(DirectFill,
            "a cell's pre-existing explicit fill must win over the custom table style's dynamic " +
            "wholeTable fill (direct cell formatting takes precedence over table styling in Excel)");
    }

    [Fact]
    public void DataBodyCells_WithoutPreexistingFill_StillGetWholeTableFill_NoRegression()
    {
        // No-regression sibling: cells that had NO pre-existing fill must still receive the custom
        // style's dynamic fill — the new guard must not suppress fill application generally.
        using var stream = BuildDirectFillPackage();

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);

        var a2 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.StyleId);
        var b2 = workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.StyleId);

        a2.FillColor.Should().Be(WholeTableFill, "a cell with no pre-existing fill must still receive the table style's fill");
        b2.FillColor.Should().Be(WholeTableFill, "a cell with no pre-existing fill must still receive the table style's fill");
    }

    private static readonly CellColor WholeTableFill = CellColor.FromArgb(0xFF, 0xFF, 0xCC);
    private static readonly CellColor DirectFill = CellColor.FromArgb(0xFF, 0x00, 0x00);

    private static MemoryStream BuildDirectFillPackage()
    {
        var workbook = new Workbook("DirectFillTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Id"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));

        // B3 carries an explicit direct fill (as if the user had applied it via Format Cells, or the
        // source file's own `s` attribute) that must survive table-style materialization on load.
        var directStyleId = workbook.RegisterStyle(new CellStyle { FillColor = DirectFill });
        var b3 = new Cell { Value = new NumberValue(20), StyleId = directStyleId };
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), b3);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "DirectFillTable",
            DisplayName = "DirectFillTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HasAutoFilter = true,
            TotalsRowShown = false,
            StyleName = "CustomFillWinStyle",
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Id"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        sheet.StructuredTables.Add(table);

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        PatchStylesXmlWithCustomTableStyle(
            stream,
            styleName: "CustomFillWinStyle",
            dxfXmls:
            [
                "<dxf><fill><patternFill><fgColor rgb=\"FFFFFFCC\"/></patternFill></fill></dxf>"
            ],
            tableStyleElementXmls:
            [
                "<tableStyleElement type=\"wholeTable\" dxfId=\"0\"/>"
            ]);

        stream.Position = 0;
        return stream;
    }

    // -------------------------------------------------------------------------
    // Shared helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Injects a custom &lt;tableStyle&gt; (with the given per-element dxf XML fragments) into an
    /// already-saved package's <c>xl/styles.xml</c>, mirroring how Excel embeds a workbook-level
    /// custom table style. The package's table part already references this style by name (set via
    /// <see cref="StructuredTableModel.StyleName"/> before the initial save).
    /// </summary>
    private static void PatchStylesXmlWithCustomTableStyle(
        MemoryStream stream,
        string styleName,
        IReadOnlyList<string> dxfXmls,
        IReadOnlyList<string> tableStyleElementXmls)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var stylesEntry = archive.GetEntry("xl/styles.xml")!;
            XDocument stylesXml;
            using (var s = stylesEntry.Open())
                stylesXml = XDocument.Load(s);

            var dxfs = new XElement(
                MainNs + "dxfs",
                new XAttribute("count", dxfXmls.Count.ToString()),
                dxfXmls.Select(ParseNamespaced));
            stylesXml.Root!.Add(dxfs);

            var tableStyles = new XElement(
                MainNs + "tableStyles",
                new XAttribute("count", "1"),
                new XElement(
                    MainNs + "tableStyle",
                    new XAttribute("name", styleName),
                    new XAttribute("pivot", "0"),
                    new XAttribute("table", "1"),
                    new XAttribute("count", tableStyleElementXmls.Count.ToString()),
                    tableStyleElementXmls.Select(ParseNamespaced)));
            stylesXml.Root.Add(tableStyles);

            stylesEntry.Delete();
            var newEntry = archive.CreateEntry("xl/styles.xml", CompressionLevel.Optimal);
            using var ws = newEntry.Open();
            stylesXml.Save(ws, SaveOptions.DisableFormatting);
        }

        stream.Position = 0;
    }

    /// <summary>
    /// Parses a raw XML fragment (with no namespace declared) into the spreadsheetml main
    /// namespace, so it — and every descendant — matches the elements the reader looks up by
    /// qualified name. Inserts <c>xmlns="..."</c> right after the root tag name so it applies as
    /// the default namespace for the whole fragment, regardless of self-closing/open-tag form.
    /// </summary>
    private static XElement ParseNamespaced(string rawXml)
    {
        var tagName = System.Text.RegularExpressions.Regex.Match(rawXml, @"^<(\w+)").Groups[1].Value;
        var insertAt = 1 + tagName.Length;
        var withNamespace = rawXml.Insert(insertAt, $" xmlns=\"{MainNs.NamespaceName}\"");
        return XElement.Parse(withNamespace);
    }
}
