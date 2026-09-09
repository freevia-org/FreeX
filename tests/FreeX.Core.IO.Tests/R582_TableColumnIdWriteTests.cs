using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 582: <c>XlsxStructuredTableWriter</c> guards the TABLE id and not the COLUMN id, 68 lines
/// apart in the same file.
/// <para>
/// The table root is written as <c>table.Id &gt; 0 ? table.Id : ExtractTrailingNumber(tablePath)</c>,
/// and when a table has no columns at all the writer synthesizes them with
/// <c>Enumerable.Range(1, ...)</c>. Both say plainly that a table/column id is a positive number.
/// <c>ToColumnXml</c> then writes <c>new XAttribute("id", column.Id)</c> with no such rule.
/// </para>
/// <para>
/// A column id reaches the model as <c>ReadIntAttribute(column, "id") ?? 0</c> — and that reader
/// cannot distinguish an ABSENT attribute from a PRESENT but unreadable one, so a
/// <c>&lt;tableColumn&gt;</c> with no id, or an out-of-range one (int.TryParse returns FALSE on
/// overflow, unlike double.TryParse), both arrive as 0. FreeX then writes <c>id="0"</c> — and if
/// more than one column is affected, it writes the SAME id twice inside one table, which is
/// self-contradictory whatever the schema says about the minimum.
/// </para>
/// <para>
/// The table id has a reject rule for exactly this case one screen away
/// (<c>if (id &lt;= 0 || ...) return false;</c> in XlsxStructuredTableMetadataReader), which is why
/// this is decidable on the codebase's own terms rather than needing Excel to settle it.
/// </para>
/// </summary>
public sealed class R582_TableColumnIdWriteTests
{
    private static XDocument SaveAndReadTablePart(params StructuredTableColumnModel[] columns)
    {
        var workbook = new Workbook("TableColumnIdTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Header2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T",
            DisplayName = "T",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
        };
        foreach (var column in columns)
            table.Columns.Add(column);
        sheet.StructuredTables.Add(table);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.Entries.Single(e =>
            e.FullName.StartsWith("xl/tables/table", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static List<string> ColumnIds(XDocument tableXml)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return tableXml.Root!
            .Element(ns + "tableColumns")!
            .Elements(ns + "tableColumn")
            .Select(c => c.Attribute("id")!.Value)
            .ToList();
    }

    [Fact]
    public void ColumnWithNoUsableId_IsNotWrittenAsZero()
    {
        var ids = ColumnIds(SaveAndReadTablePart(
            new StructuredTableColumnModel(0, "Col1"),
            new StructuredTableColumnModel(2, "Col2")));

        ids.Should().NotContain("0", "a table column id is a positive number, as the writer's own " +
                                     "table-id rule and column synthesizer both say");
    }

    [Fact]
    public void SeveralColumnsWithNoUsableId_DoNotAllCollideOnOneId()
    {
        var ids = ColumnIds(SaveAndReadTablePart(
            new StructuredTableColumnModel(0, "Col1"),
            new StructuredTableColumnModel(0, "Col2")));

        ids.Should().OnlyHaveUniqueItems("column ids must be unique within a table");
        ids.Should().NotContain("0");
    }

    [Fact]
    public void AssignedIdMustNotCollideWithAColumnThatHasOne()
    {
        // The obvious positional fallback (index + 1) would give the first column id 1 and collide
        // with nothing here -- but give the SECOND column id 2, which the first column already owns.
        var ids = ColumnIds(SaveAndReadTablePart(
            new StructuredTableColumnModel(2, "Col1"),
            new StructuredTableColumnModel(0, "Col2")));

        ids.Should().OnlyHaveUniqueItems("an assigned id must not collide with an id already in use");
        ids.Should().Contain("2", "the column that HAS a valid id must keep it");
    }

    [Fact]
    public void ColumnsWithValidIds_AreUntouched()
    {
        // Non-vacuity: the fix must not renumber ids that were already fine, which would break every
        // reference to them (autoFilter, sortState, calculated-column formulas).
        ColumnIds(SaveAndReadTablePart(
            new StructuredTableColumnModel(7, "Col1"),
            new StructuredTableColumnModel(3, "Col2")))
            .Should().Equal("7", "3");
    }
}
