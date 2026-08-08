using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R128 (fresh-lens finding on Sheet.Clone.cs:397): every sibling clone path hardened against the
/// "clone copies PackagePart verbatim from the source object" bug class -- <see cref="PivotTableModel"/>
/// (Sheet.Clone.cs ClonePivotTable, see <see cref="R127B_DuplicateSheetPivotPackagePartIdentityTests"/>),
/// <c>PivotCacheModel</c> (DuplicateSheetCommand.cs), and <c>SlicerModel</c>/<c>TimelineModel</c>
/// (DuplicateSheetDrawingCloner.cs) -- deliberately blank PackagePart on the clone. <see cref="Sheet"/>'s
/// CloneStructuredTable was never given the same fix: it copied <see cref="StructuredTableModel.PackagePart"/>
/// verbatim, both via <see cref="Sheet.Clone"/> (every table on a duplicated sheet) and via
/// <see cref="Sheet.ReidentifyStructuredTable"/> (which <c>DuplicateSheetCommand.UniquifyClonedTables</c>
/// uses to give the copy a fresh Id/Name -- PackagePart was never included in that re-identification).
///
/// PackagePart is the exact archive path (e.g. "xl/tables/table1.xml") the SOURCE table was loaded
/// from/last saved to. Two distinct StructuredTableModel instances sharing that path means
/// XlsxStructuredTableWriter.Save (Core.IO) writes both the source's and the duplicate's table XML to
/// the SAME zip entry on the very next full save -- whichever is processed second silently clobbers the
/// other's freshly-written &lt;table&gt; definition, even though both sheets keep their own worksheet
/// relationship (and so both still resolve, but now to the wrong content). The fix mirrors R127B: a
/// cloned structured table must start with an EMPTY PackagePart, exactly like a brand-new table that has
/// never been saved -- XlsxStructuredTableWriter's "no preserved path" branch already mints a fresh
/// "xl/tables/tableN.xml" for that case. As defense in depth, XlsxStructuredTableWriter.Save's
/// previously-unconditional preserved-path branch now also detects an already-written path this save and
/// falls back to minting a fresh one instead of silently overwriting (see
/// R128_StructuredTableWriterCollisionGuardTests in FreeX.Core.IO.Tests).
/// </summary>
public sealed class R128_DuplicateSheetTablePackagePartIdentityTests
{
    [Fact]
    public void DuplicateSheet_ClonedTable_DoesNotShareSourcePackagePart()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var tableRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));

        var originalTable = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            // Mirrors what XlsxStructuredTableModelMapper actually assigns when loading a real workbook.
            PackagePart = "xl/tables/table1.xml",
        };
        sheet.StructuredTables.Add(originalTable);

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;

        // Already-landed behavior (R17-table-listobject-3): the copy gets a fresh Id/Name.
        copiedTable.Id.Should().NotBe(originalTable.Id);
        copiedTable.Name.Should().NotBe(originalTable.Name);

        // The core defect: the clone must not carry the source's exact package-part path forward.
        copiedTable.PackagePart.Should().NotBe(originalTable.PackagePart,
            because: "two StructuredTableModel entries sharing one package-part path collide when " +
                     "XlsxStructuredTableWriter.Save writes both tables to the same zip entry, and " +
                     "whichever is written second silently overwrites the other's saved <table> XML");
        copiedTable.PackagePart.Should().BeEmpty(
            because: "a freshly-cloned table has no saved package identity yet, matching a brand-new table");

        // The original must be completely untouched.
        originalTable.PackagePart.Should().Be("xl/tables/table1.xml");

        // The actual collision-preventing property: no two tables in the workbook share a non-blank
        // PackagePart.
        var nonBlankParts = workbook.Sheets
            .SelectMany(s => s.StructuredTables)
            .Select(t => t.PackagePart)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        nonBlankParts.Should().OnlyHaveUniqueItems(
            because: "a duplicate non-blank PackagePart across the workbook means a full save silently " +
                     "overwrites one table's on-disk definition with the other's");
    }

    [Fact]
    public void SheetClone_ClonedTable_DoesNotShareSourcePackagePart()
    {
        // Exercises Sheet.Clone's CloneStructuredTable directly (the only production caller of
        // Sheet.Clone(SheetId, string) is DuplicateSheetCommand, so this goes through the real,
        // single production entry point for a cloned Sheet), independent of the
        // DuplicateSheetCommand-level UniquifyClonedTables re-identification step.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var tableRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            PackagePart = "xl/tables/table1.xml",
        };
        sheet.StructuredTables.Add(table);

        var copy = sheet.Clone(SheetId.New(), "Sheet1 Copy");

        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        copiedTable.PackagePart.Should().NotBe(table.PackagePart,
            because: "two StructuredTableModel entries sharing one package-part path collide in " +
                     "XlsxStructuredTableWriter.Save");
        copiedTable.PackagePart.Should().BeEmpty(
            because: "a freshly-cloned table has no saved package identity yet, matching a brand-new table");

        // The source table must be completely untouched.
        table.PackagePart.Should().Be("xl/tables/table1.xml");
    }

    [Fact]
    public void ReidentifyStructuredTable_DoesNotShareSourcePackagePart()
    {
        // ReidentifyStructuredTable is the OTHER production caller of CloneStructuredTable --
        // DuplicateSheetCommand.UniquifyClonedTables calls it in place, on the already-Clone()d
        // sheet, to give the copy's table a workbook-unique Id/Name. Exercised directly to prove the
        // fix holds for both callers of CloneStructuredTable, not just the Sheet.Clone one.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var tableRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
            PackagePart = "xl/tables/table1.xml",
        });

        sheet.ReidentifyStructuredTable(0, 2, "Table2");

        var reidentified = sheet.StructuredTables.Should().ContainSingle().Subject;
        reidentified.Id.Should().Be(2);
        reidentified.Name.Should().Be("Table2");
        reidentified.PackagePart.Should().BeEmpty(
            because: "re-identifying a cloned table for workbook-unique identity must not leave it " +
                     "aliasing the source table's package-part path");
    }

    [Fact]
    public void DuplicateSheet_MultipleTablesOnSameSheet_NoTwoClonedObjectsShareNonEmptyPackagePart()
    {
        // No-regression sibling: a sheet with TWO independent structured tables (the ordinary "two
        // separate tables on one sheet" shape) must still duplicate cleanly -- every cloned table gets
        // its own blank PackagePart, and the already-landed UniquifyClonedTables behavior (distinct
        // Id/Name per clone) must still hold alongside this fix.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var range1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var range2 = new GridRange(new CellAddress(sheet.Id, 1, 10), new CellAddress(sheet.Id, 5, 11));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "Table1", DisplayName = "Table1", Range = range1,
            PackagePart = "xl/tables/table1.xml",
        });
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2, Name = "Table2", DisplayName = "Table2", Range = range2,
            PackagePart = "xl/tables/table2.xml",
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        copy.StructuredTables.Should().HaveCount(2);

        // Already-landed behavior: each clone gets its own Id/Name, distinct from both the source's
        // and each other's.
        copy.StructuredTables.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        copy.StructuredTables.Select(t => t.Name).Should().OnlyHaveUniqueItems();

        // This fix's property: across every table in the workbook (both sheets), no two distinct
        // objects share a non-blank PackagePart.
        var allParts = workbook.Sheets
            .SelectMany(s => s.StructuredTables)
            .Select(t => t.PackagePart)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        allParts.Should().OnlyHaveUniqueItems();

        // Both clones came from real (previously-saved) sources, so both must have been reset to blank.
        copy.StructuredTables.Should().OnlyContain(t => t.PackagePart == string.Empty);
    }
}
