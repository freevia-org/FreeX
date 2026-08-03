using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-120 finding: r119 added 'change_inserted_cell' (a brand-new formula
/// cell not present in the loaded source) to PatchBlockReasonInvalidatesCalcChain, but a new cell
/// landing inside a Structured Table's range is diverted to a DIFFERENT reason,
/// 'change_table_inserted_cell', reached via `baseline.Tables.HasTables &amp;&amp;
/// !AllowsInsertedScalarValueCellPatch(row, col)` -- which fires BEFORE the HasFormula check and was
/// absent from the allowlist. So a new calculated-column formula cell (e.g. Excel auto-extending a
/// table's calculated-column formula into a newly added row -- FreeX.Core.Commands.InsertRowsCommand
/// performs exactly this auto-fill, see R26_InsertRowsTableCalculatedColumnFillTests in
/// FreeX.Integration.Tests for the model-layer half of this same feature) used to leave
/// invalidatesCalcChain = false on the full-save fallback it triggers, shipping the stale source
/// calcChain.xml (which predates the new formula cell and omits it) unmodified -- the identical defect
/// class r119 fixed for the non-table 'change_inserted_cell' path.
///
/// FreeX.Core.Commands is a separately-owned project outside this fix's scope, so the fixture below
/// reproduces the scenario directly through the Sheet model API (Sheet.SetCell/SetFormula, with the
/// table's declared Range already including the not-yet-populated row) instead of invoking
/// InsertRowsCommand -- this still drives the REAL entry point under test (XlsxFileAdapter.Save's
/// patch-vs-full-save decision), exactly as R119_CalcChainReorderAndInsertedFormulaTests' own 'new
/// formula in previously-blank cell' fixture uses Sheet.SetFormula directly rather than a command.
///
/// Sibling fix in the same file: 'change_table_cell' (an EXISTING table cell gaining/changing a
/// formula -- formulaChanged or cell.HasFormula are each independently sufficient to divert there,
/// before the general formula-text-change handling that would otherwise route a literal-to-formula
/// conversion to change_inserted_cell/change_formula_text) has the identical missing-calcChain-entry
/// risk and was also absent from the allowlist -- fixed alongside change_table_inserted_cell.
/// </summary>
public sealed class R120_TableInsertedFormulaCellCalcChainTests
{
    // ---- unit-level: the allowlist itself ---------------------------------------------------------

    [Theory]
    [InlineData("change_table_inserted_cell")]
    [InlineData("change_table_cell")]
    public void PatchBlockReasonInvalidatesCalcChain_TableFormulaReasons_InvalidateCalcChain(string reason)
    {
        InvokePatchBlockReasonInvalidatesCalcChain(reason).Should().BeTrue(
            $"a full-save fallback triggered by '{reason}' can introduce a formula cell the source " +
            "calcChain.xml does not know about (a new table row's calculated column, or an existing " +
            "table cell newly given a formula), so any stale source calcChain.xml must not survive the save");
    }

    private static bool InvokePatchBlockReasonInvalidatesCalcChain(string reason)
    {
        var method = FindPrivateStaticMethod("PatchBlockReasonInvalidatesCalcChain");
        return (bool)method.Invoke(null, [reason])!;
    }

    private static System.Reflection.MethodInfo FindPrivateStaticMethod(string name)
    {
        var stack = new Stack<Type>();
        stack.Push(typeof(XlsxFileAdapter));
        var seen = new HashSet<Type>();
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            if (!seen.Add(type))
                continue;
            var method = type.GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method is not null)
                return method;
            foreach (var nested in type.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                stack.Push(nested);
        }

        throw new MissingMethodException(
            $"Private static method '{name}' not found on XlsxFileAdapter or its nested types.");
    }

    // ---- end-to-end: real Save() entry point, Excel auto-extending a calculated column ------------

    [Fact]
    public void Save_NewCalculatedColumnFormulaFilledIntoDeclaredTableRow_FullSaveStripsStaleSourceCalcChain()
    {
        var sourceBytes = CreateCalculatedColumnTableSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        PackageHasEntry(sourceBytes, "xl/calcChain.xml").Should().BeTrue("test fixture must seed a source calcChain.xml");

        var sheet = workbook.Sheets[0];

        // The fixture's table already DECLARES row 5 as part of its Range (A1:B5) but row 5 itself is
        // entirely unoccupied in the loaded source -- e.g. a table with a trailing blank row, or (like
        // InsertRowsCommand -- see R26_InsertRowsTableCalculatedColumnFillTests) a row-insert that grows
        // the table's Range in one command step before this per-cell scan ever runs. Filling that
        // pre-declared row now (auto-filling the calculated column's formula exactly like Excel does)
        // does NOT change the table's own metadata (Range/Columns are unchanged), so this reaches the
        // per-cell "brand-new cell inside an unchanged table range" path (change_table_inserted_cell)
        // rather than change_table_metadata -- a brand-new formula cell inside the table's range that
        // the loaded source (and its calcChain.xml) never knew about.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(4));
        sheet.SetFormula(new CellAddress(sheet.Id, 5, 2), "A5*2");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_table_inserted_cell");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();

        // The core regression: the pre-edit calcChain.xml (which simply does not mention the newly
        // inserted formula cell) must not survive into the saved package.
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse(
            "a table auto-extending a calculated column's formula into a new row invalidates the source " +
            "calcChain (it omits the new cell) and must strip it on full-save fallback");
    }

    private static byte[] CreateCalculatedColumnTableSourcePackageWithCalcChain()
    {
        // Table1 spans A1:B5 (A1:B1 header; column B is a calculated column holding "A2*2" anchored to
        // the table's first data row, row 2). Rows 2-4 are populated (A2=1/B2=2, A3=2/B3=4, A4=3/B4=6);
        // row 5 is entirely blank in the loaded source even though the table's OWN Range metadata
        // already declares it as part of the table (e.g. a table with a trailing blank row, or the
        // one-command-later state of an InsertRowsCommand-grown table -- see
        // R26_InsertRowsTableCalculatedColumnFillTests for the model-layer half of this same feature).
        var workbook = new Workbook("CalcChainTableInsertedCellRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Double"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A2*2");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "A3*2");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 2), "A4*2");

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Double", CalculatedColumnFormula: "A2*2")
            }
        };
        sheet.StructuredTables.Add(table);

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        AddMinimalCalcChainPackage(source);
        source.Position = 0;
        return source.ToArray();
    }

    // ---- end-to-end: real Save() entry point, formula newly typed into an existing table cell -----

    [Fact]
    public void Save_ExistingTableCellGivenNewFormula_FullSaveStripsStaleSourceCalcChain()
    {
        var sourceBytes = CreateScalarTableSourcePackageWithCalcChain();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        PackageHasEntry(sourceBytes, "xl/calcChain.xml").Should().BeTrue("test fixture must seed a source calcChain.xml");

        var sheet = workbook.Sheets[0];
        // B2 is a plain literal value in the loaded source (not a calculated column). Typing a
        // brand-new formula into it mirrors 'change_inserted_cell' for the non-table case, but here
        // the cell is an EXISTING baseline cell inside a table, so it is diverted to change_table_cell.
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A2*10");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_table_cell");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();

        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse(
            "an existing table cell newly given a formula invalidates the source calcChain (it omits " +
            "the newly-formula'd cell) and must strip it on full-save fallback");
    }

    private static byte[] CreateScalarTableSourcePackageWithCalcChain()
    {
        var workbook = new Workbook("CalcChainTableCellRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Columns =
            {
                new StructuredTableColumnModel(1, "Category"),
                new StructuredTableColumnModel(2, "Amount")
            }
        };
        sheet.StructuredTables.Add(table);

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;

        AddMinimalCalcChainPackage(source);
        source.Position = 0;
        return source.ToArray();
    }

    // ---- no-regression sibling: metadata-only table full-save fallback does not need to change -----
    // (already covered for non-table reasons by R119_CalcChainReorderAndInsertedFormulaTests'
    // "MetadataOnlyReasons" theory; change_table_metadata below is the table-specific analogue and was
    // never part of this finding -- it cannot introduce a new formula cell, so it must keep behaving
    // exactly as it did before this fix.)

    [Fact]
    public void PatchBlockReasonInvalidatesCalcChain_TableMetadataOnlyReason_DoesNotInvalidateCalcChain()
    {
        InvokePatchBlockReasonInvalidatesCalcChain("change_table_metadata").Should().BeFalse(
            "a table-metadata-only full-save fallback (e.g. rename/style options) cannot introduce a " +
            "formula cell absent from the source calcChain, so a stale calcChain risk does not apply " +
            "and this reason must NOT be swept into the fix for change_table_inserted_cell/change_table_cell");
    }

    // ---- shared helpers (mirrors R119_CalcChainReorderAndInsertedFormulaTests) ---------------------

    private static void AddMinimalCalcChainPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace calcNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/calcChain.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml")));
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            ReplacePackageXml(archive, "xl/calcChain.xml", new XDocument(
                new XElement(
                    calcNs + "calcChain",
                    new XElement(calcNs + "c", new XAttribute("r", "B2"), new XAttribute("i", "1")))));

            const string workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive, workbookRelsPath);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXCalcChain"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain"),
                new XAttribute("Target", "calcChain.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
        }

        packageStream.Position = 0;
    }

    private static bool PackageHasEntry(byte[] packageBytes, string path)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        return archive.GetEntry(path) is not null;
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path)
    {
        using var entryStream = archive.GetEntry(path)!.Open();
        return XDocument.Load(entryStream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var entryStream = entry.Open();
        document.Save(entryStream);
    }
}
