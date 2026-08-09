using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R128 (io-writer defense-in-depth): XlsxStructuredTableWriter.Save's preserved-path branch used to
/// call <c>claimedTablePaths.Add(tablePath)</c> unconditionally, discarding the return value -- unlike
/// the sibling "no preserved path" branch's collision-avoiding <c>while (!claimedTablePaths.Add(...))</c>
/// loop just above it. If two distinct <see cref="StructuredTableModel"/> instances (anywhere in the
/// workbook, e.g. a Duplicate-Sheet clone that inherited its source table's PackagePart -- see
/// R128_DuplicateSheetTablePackagePartIdentityTests in FreeX.Core.Model.Tests, which fixes that root
/// cause) ever resolved to the SAME PackagePart, the writer would silently write both tables' XML to the
/// identical zip entry: whichever table is processed second clobbers the first's saved &lt;table&gt;
/// definition, even though both sheets keep their own worksheet relationship pointing at that one now-
/// wrong physical part.
///
/// This test exercises that scenario directly at the writer's own level (two tables sharing an aliased
/// PackagePart), independent of how the alias came to exist, through the real production entry point
/// (<see cref="XlsxFileAdapter.Save"/>) -- proving the writer itself no longer trusts an aliased
/// preserved path even if some other bug (present or future) ever produces one again.
/// </summary>
public sealed class R128_StructuredTableWriterCollisionGuardTests
{
    [Fact]
    public void TwoTablesSharingAliasedPackagePart_BothSurviveUnderDistinctPaths()
    {
        var workbook = new Workbook("AliasedTablePackagePartTest");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        AddSimpleTableData(sheet1);
        AddSimpleTableData(sheet2);

        // Both tables claim the SAME PackagePart -- the exact aliasing shape a Duplicate Sheet clone
        // produced before the Sheet.Clone.cs fix (and could in principle recur from any other bug).
        var firstTable = new StructuredTableModel
        {
            Id = 1,
            Name = "FirstTable",
            DisplayName = "FirstTable",
            Range = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 2, 2)),
            PackagePart = "xl/tables/table1.xml",
        };
        firstTable.Columns.Add(new StructuredTableColumnModel(1, "FirstCol1"));
        firstTable.Columns.Add(new StructuredTableColumnModel(2, "FirstCol2"));
        sheet1.StructuredTables.Add(firstTable);

        var secondTable = new StructuredTableModel
        {
            Id = 2,
            Name = "SecondTable",
            DisplayName = "SecondTable",
            Range = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 2, 2)),
            PackagePart = "xl/tables/table1.xml", // aliased with firstTable
        };
        secondTable.Columns.Add(new StructuredTableColumnModel(1, "SecondCol1"));
        secondTable.Columns.Add(new StructuredTableColumnModel(2, "SecondCol2"));
        sheet2.StructuredTables.Add(secondTable);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var tableEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/tables/table", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The core defect: both tables must land on DISTINCT physical parts -- not have the second
        // one silently overwrite the first's zip entry.
        tableEntries.Should().HaveCount(2,
            "both tables must be written to distinct package parts, not overwrite each other under " +
            "the one aliased PackagePart");

        var names = tableEntries
            .Select(path =>
            {
                var entry = archive.GetEntry(path)!;
                using var s = entry.Open();
                return XDocument.Load(s).Root!.Attribute("name")!.Value;
            })
            .ToList();

        names.Should().Contain("FirstTable",
            "the first table's own content must survive under some distinct package part");
        names.Should().Contain("SecondTable",
            "the second table's content must land on its OWN part instead of clobbering the first " +
            "table's saved XML");
    }

    [Fact]
    public void PreservedTablePackagePart_StillReusedWhenNotAliased_NoRegression()
    {
        // No-regression sibling: a table with a genuinely unique, first-time-seen preserved
        // PackagePart must still keep it verbatim (must not be needlessly reassigned just because
        // claimedTablePaths was pre-seeded with every table's own path up front).
        var workbook = new Workbook("UniquePreservedTablePathTest");
        var sheet = workbook.AddSheet("Sheet1");
        AddSimpleTableData(sheet);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "PreservedTable",
            DisplayName = "PreservedTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            PackagePart = "xl/tables/table7.xml",
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Col1"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Col2"));
        sheet.StructuredTables.Add(table);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/tables/table7.xml");
        entry.Should().NotBeNull("a genuinely unique preserved PackagePart must still be honored verbatim");

        using var s = entry!.Open();
        var xml = XDocument.Load(s);
        xml.Root!.Attribute("name")!.Value.Should().Be("PreservedTable");
    }

    private static void AddSimpleTableData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Header2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
    }
}
