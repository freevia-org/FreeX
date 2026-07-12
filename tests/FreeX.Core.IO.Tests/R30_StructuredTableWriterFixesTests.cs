using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-30 structured-table writer findings:
///   R30-io-table-structured-deep-1: the global tablePartIndex counter used to collide with a
///     preserved table's PackagePart, silently overwriting one table's XML with another's.
///   R30-io-table-structured-deep-3: &lt;autoFilter ref&gt; used to always span the full table
///     range (including the totals row) instead of being clamped to header+data rows.
/// </summary>
public sealed class R30_StructuredTableWriterFixesTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // -------------------------------------------------------------------------
    // R30-io-table-structured-deep-1 — tablePartIndex vs preserved PackagePart collision
    // -------------------------------------------------------------------------

    [Fact]
    public void PreservedTablePackagePart_DoesNotGetOverwrittenByGeneratedPathForNewTable()
    {
        // Sheet1's table keeps a preserved, historical PackagePart of "table2.xml" (as if
        // table1.xml had been deleted upstream and this table survived under table2.xml).
        // Sheet2's table is brand new (no PackagePart yet). With the old unconditional
        // tablePartIndex counter (starts at 1, incremented once per table regardless of whether
        // a path was generated), processing Sheet1 first would bump the counter to 2 before
        // Sheet2's blank-PackagePart table generated "table{2}.xml" -- colliding with Sheet1's
        // preserved part and silently overwriting it.
        var workbook = new Workbook("TablePathCollisionTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        AddSimpleTableData(sheet1);
        AddSimpleTableData(sheet2);

        var preservedTable = new StructuredTableModel
        {
            Id = 1,
            Name = "PreservedTable",
            DisplayName = "PreservedTable",
            Range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 2)),
            PackagePart = "xl/tables/table2.xml",
        };
        preservedTable.Columns.Add(new StructuredTableColumnModel(1, "PreservedCol1"));
        preservedTable.Columns.Add(new StructuredTableColumnModel(2, "PreservedCol2"));
        sheet1.StructuredTables.Add(preservedTable);

        var newTable = new StructuredTableModel
        {
            Id = 2,
            Name = "NewTable",
            DisplayName = "NewTable",
            Range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 2, 2)),
            PackagePart = "",
        };
        newTable.Columns.Add(new StructuredTableColumnModel(1, "NewCol1"));
        newTable.Columns.Add(new StructuredTableColumnModel(2, "NewCol2"));
        sheet2.StructuredTables.Add(newTable);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var table2Entry = archive.GetEntry("xl/tables/table2.xml");
        table2Entry.Should().NotBeNull("the preserved table's own package part must still exist");
        XDocument table2Xml;
        using (var s = table2Entry!.Open())
            table2Xml = XDocument.Load(s);
        table2Xml.Root!.Attribute("name")!.Value.Should().Be("PreservedTable",
            "table2.xml must retain the preserved table's own content, not be overwritten by the new table");

        // The new table must have landed on a distinct, non-colliding generated path.
        var otherTableEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/tables/table", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(e.FullName, "xl/tables/table2.xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        otherTableEntries.Should().HaveCount(1, "the new table must be written to its own distinct package part");

        XDocument otherTableXml;
        using (var s = otherTableEntries[0].Open())
            otherTableXml = XDocument.Load(s);
        otherTableXml.Root!.Attribute("name")!.Value.Should().Be("NewTable",
            "the new table's content must survive under its own generated path");
    }

    [Fact]
    public void MultipleNewTablesWithoutPreservedParts_StillGetSequentialDistinctPaths()
    {
        // Sibling already-working case: when no table has a preserved PackagePart, freshly
        // generated table paths must remain sequential and distinct (no regression from the fix).
        var workbook = new Workbook("SequentialTablePathsTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        AddSimpleTableData(sheet1);
        AddSimpleTableData(sheet2);

        var tableA = new StructuredTableModel
        {
            Id = 1,
            Name = "TableA",
            DisplayName = "TableA",
            Range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 2)),
            PackagePart = "",
        };
        tableA.Columns.Add(new StructuredTableColumnModel(1, "A1"));
        tableA.Columns.Add(new StructuredTableColumnModel(2, "A2"));
        sheet1.StructuredTables.Add(tableA);

        var tableB = new StructuredTableModel
        {
            Id = 2,
            Name = "TableB",
            DisplayName = "TableB",
            Range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 2, 2)),
            PackagePart = "",
        };
        tableB.Columns.Add(new StructuredTableColumnModel(1, "B1"));
        tableB.Columns.Add(new StructuredTableColumnModel(2, "B2"));
        sheet2.StructuredTables.Add(tableB);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var tableEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/tables/table", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .ToList();

        tableEntries.Should().HaveCount(2, "both new tables must be written to distinct paths");
        tableEntries.Should().Contain("xl/tables/table1.xml");
        tableEntries.Should().Contain("xl/tables/table2.xml");
    }

    // -------------------------------------------------------------------------
    // R30-io-table-structured-deep-3 — autoFilter ref must exclude the totals row
    // -------------------------------------------------------------------------

    [Fact]
    public void AutoFilterRef_ExcludesTotalsRow_WhenTotalsRowShown()
    {
        var workbook = new Workbook("AutoFilterTotalsRowTest");
        var sheet = workbook.AddSheet("Data");

        // Header (row 1) + data (rows 2-6) + totals (row 7).
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "TotalsTable",
            DisplayName = "TotalsTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 4)),
            HasAutoFilter = true,
            TotalsRowShown = true,
            TotalsRowCount = 1,
            PackagePart = "xl/tables/table1.xml",
        };
        for (var col = 1; col <= 4; col++)
            table.Columns.Add(new StructuredTableColumnModel(col, $"Col{col}"));
        sheet.StructuredTables.Add(table);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var autoFilterRef = ReadAutoFilterRef(stream);
        autoFilterRef.Should().Be("A1:D6",
            "autoFilter ref must exclude the totals row (row 7) so filtering never touches it");
    }

    [Fact]
    public void AutoFilterRef_SpansFullRange_WhenNoTotalsRow()
    {
        // Sibling already-working case: without a totals row, the autoFilter ref must still
        // span the full table range (no over-clamping regression).
        var workbook = new Workbook("AutoFilterNoTotalsRowTest");
        var sheet = workbook.AddSheet("Data");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "PlainTable",
            DisplayName = "PlainTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
            HasAutoFilter = true,
            TotalsRowShown = false,
            PackagePart = "xl/tables/table1.xml",
        };
        for (var col = 1; col <= 4; col++)
            table.Columns.Add(new StructuredTableColumnModel(col, $"Col{col}"));
        sheet.StructuredTables.Add(table);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var autoFilterRef = ReadAutoFilterRef(stream);
        autoFilterRef.Should().Be("A1:D6",
            "without a totals row, autoFilter ref must still span the full table range");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AddSimpleTableData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Header2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
    }

    private static string? ReadAutoFilterRef(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var tableEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        tableEntry.Should().NotBeNull("there must be a table XML part in the package");

        XDocument tableXml;
        using (var s = tableEntry!.Open())
            tableXml = XDocument.Load(s);

        return tableXml.Root!.Element(MainNs + "autoFilter")?.Attribute("ref")?.Value;
    }
}
