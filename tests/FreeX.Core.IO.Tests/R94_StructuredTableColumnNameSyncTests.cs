using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-94 finding R94-io-table-column-name-sync:
///   XlsxStructuredTableWriter.ToColumnXml used to write &lt;tableColumn name="..."&gt; from the
///   STORED StructuredTableColumnModel.Name rather than the LIVE header-row cell text. Renaming a
///   table column in FreeX is done by editing its header cell only -- nothing syncs the stored
///   Name back -- so a saved workbook could carry a table1.xml @name that disagrees with the
///   header row actually written into the sheet. Excel treats tableColumn/@name as authoritative
///   (ECMA-376 18.3.1.4/18.3.1.24) and requires it to match the header cell; a mismatch produces a
///   repair prompt. The fix mirrors the pre-existing live-header-first lookup already used by
///   StructuredReferenceResolver.ColumnHeaderText and StructuredTableTotalsCommand's own copy of
///   it: use the header cell's TextValue when present, falling back to the stored Name only for a
///   headerless table (HeaderRowCount == 0) or a blank/non-text header cell.
/// </summary>
public sealed class R94_StructuredTableColumnNameSyncTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void TableColumnName_ReflectsLiveHeaderCell_AfterHeaderRenamedWithoutSyncingModel()
    {
        // Header (row 1) + one data row (row 2). Column 2's header cell is edited directly to
        // "Revenue" -- the only rename path FreeX exposes -- without touching the stored
        // StructuredTableColumnModel.Name, which is exactly what an ordinary EditCellsCommand
        // header edit does today.
        var workbook = new Workbook("ColumnNameSyncTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Id"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            HeaderRowCount = 1,
            PackagePart = "xl/tables/table1.xml",
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Id"));
        // Stored Name is stale: the header cell reads "Revenue" but the model still says "Sales".
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables.Add(table);

        var columnNames = SaveAndReadColumnNames(workbook);

        columnNames.Should().Equal(["Id", "Revenue"],
            "tableColumn/@name must match the live header-row cell text, not the stale stored Name");
    }

    [Fact]
    public void TableColumnName_MatchesStoredName_WhenHeaderUntouched()
    {
        // No-regression sibling: an ordinary table whose header cells were never independently
        // edited must still round-trip its column names unchanged.
        var workbook = new Workbook("ColumnNameUnchangedTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Id"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "PlainTable",
            DisplayName = "PlainTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            HeaderRowCount = 1,
            PackagePart = "xl/tables/table1.xml",
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Id"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales"));
        sheet.StructuredTables.Add(table);

        var columnNames = SaveAndReadColumnNames(workbook);

        columnNames.Should().Equal(["Id", "Sales"],
            "an untouched header must round-trip the same column names as before the fix");
    }

    [Fact]
    public void TableColumnName_FallsBackToStoredName_ForHeaderlessTable()
    {
        // HeaderRowCount == 0: there is no header row to read from, so the stored Name remains
        // authoritative (mirrors StructuredReferenceResolver.ColumnHeaderText's own fallback).
        var workbook = new Workbook("HeaderlessTableTest");
        var sheet = workbook.AddSheet("Data");

        // Row 1 holds plain data (no header row at all for this table).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(100));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "HeaderlessTable",
            DisplayName = "HeaderlessTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2)),
            HeaderRowCount = 0,
            PackagePart = "xl/tables/table1.xml",
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Column1"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Column2"));
        sheet.StructuredTables.Add(table);

        var columnNames = SaveAndReadColumnNames(workbook);

        columnNames.Should().Equal(["Column1", "Column2"],
            "a headerless table has no header row to read, so the stored Name must be used as-is");
    }

    [Fact]
    public void TableColumnName_FallsBackToStoredName_WhenHeaderCellIsBlankOrNonText()
    {
        // A blank header cell and a numeric header cell both fall back to the stored Name, same
        // as the resolver/totals-refresh siblings, which also only special-case plain TextValue
        // header cells.
        var workbook = new Workbook("NonTextHeaderTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Id"));
        // Column 2's header cell is left blank.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2024));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(3));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "MixedHeaderTable",
            DisplayName = "MixedHeaderTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            HeaderRowCount = 1,
            PackagePart = "xl/tables/table1.xml",
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Id"));
        table.Columns.Add(new StructuredTableColumnModel(2, "BlankHeaderStoredName"));
        table.Columns.Add(new StructuredTableColumnModel(3, "YearStoredName"));
        sheet.StructuredTables.Add(table);

        var columnNames = SaveAndReadColumnNames(workbook);

        columnNames.Should().Equal(["Id", "BlankHeaderStoredName", "YearStoredName"],
            "a blank or non-text header cell must fall back to the stored column Name");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<string> SaveAndReadColumnNames(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var tableEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        tableEntry.Should().NotBeNull("there must be a table XML part in the package");

        XDocument tableXml;
        using (var s = tableEntry!.Open())
            tableXml = XDocument.Load(s);

        return tableXml.Root!
            .Element(MainNs + "tableColumns")!
            .Elements(MainNs + "tableColumn")
            .Select(e => e.Attribute("name")!.Value)
            .ToList();
    }
}
