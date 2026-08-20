using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R155 (legacy-CSE-array-identity class, IO half): patch-save validates a candidate patch by
/// speculatively reverting the live workbook model back to its baseline, fingerprinting it, and then
/// unwinding that revert in a <c>finally</c>. Both directions reassign <see cref="Cell.FormulaText"/>
/// — whose setter unconditionally resets ArrayMode/LegacyArrayRows/LegacyArrayCols to the "freshly
/// authored modern formula" defaults — and while ArrayMode was captured and restored on both sides,
/// LegacyArrayRows/LegacyArrayCols were never captured at all. A legacy CSE array anchor
/// (<c>&lt;f t="array" ref="B2:B3"&gt;</c>) therefore lost its fixed extent every time the workbook
/// was patch-saved, silently downgrading it to a free-spilling modern dynamic array and dropping the
/// "you cannot change part of an array" protection — with no user edit involved at all.
/// </summary>
public sealed class R155_PatchSaveLegacyArrayIdentityTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    /// <summary>
    /// B2 is a legacy CSE array anchor over B2:B3. Editing its own cached value is an ordinary
    /// patchable change (FormulaCachedValue). Adding a brand-new cell alongside it takes the save
    /// off the ChangesOnlyExistingCells fast path and into <c>ModelMatchesWithOriginalValues</c> —
    /// the speculative revert/unwind that reassigns B2's FormulaText, which is exactly where the
    /// legacy extent used to be destroyed.
    /// </summary>
    [Fact]
    public void Save_PatchingLegacyArrayAnchorCachedValue_KeepsAnchorFixedExtentInModel()
    {
        var sourceBytes = R155LegacyArrayPackage.Create();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2
        var anchorCell = sheet.GetCell(anchor)!;
        anchorCell.LegacyArrayRows.Should().Be(2u, "sanity: the loader recognises <f t=\"array\" ref=\"B2:B3\"/>");
        anchorCell.LegacyArrayCols.Should().Be(1u);

        // A recalc refreshed the anchor's cached value; nothing about the array's shape changed.
        anchorCell.Value = new NumberValue(6);

        // A brand-new (previously empty) cell elsewhere on the sheet, inside the declared A1:B3
        // dimension so no dimension patch is needed. Its InsertedLiteralValue change is what routes
        // this save through the speculative model revert instead of the existing-cells fast path.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

        var afterSave = sheet.GetCell(anchor)!;
        afterSave.LegacyArrayRows.Should().Be(2u,
            "saving must leave the model exactly as it found it — the patch-validation revert/unwind " +
            "must put the legacy fixed extent back after reassigning FormulaText");
        afterSave.LegacyArrayCols.Should().Be(1u);
        afterSave.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        afterSave.FormulaText.Should().Be("A1:A2");

        sheet.TryGetArrayExtent(new CellAddress(sheet.Id, 3, 2), out var extentAnchor, out var rows, out var cols)
            .Should().BeTrue("B3 must still be a declared member of B2's array after the save");
        extentAnchor.Should().Be(anchor);
        rows.Should().Be(2u);
        cols.Should().Be(1u);
    }

    /// <summary>
    /// Sibling: the anchor's extent is genuinely gone (the user re-authored over the whole array
    /// range, which clears LegacyArrayRows/Cols while ArrayMode stays Dynamic). A patch cannot
    /// express that — it reuses the source XML's own <c>&lt;f t="array" ref="B2:B3"&gt;</c> verbatim,
    /// so the saved package would keep declaring an array over cells that are no longer part of one.
    /// The save must fall back to a full save instead.
    /// </summary>
    [Fact]
    public void Save_LegacyArrayExtentCleared_FallsBackToFullSave()
    {
        var sourceBytes = R155LegacyArrayPackage.Create();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var anchorCell = sheet.GetCell(2, 2)!;
        anchorCell.LegacyArrayRows.Should().Be(2u);

        // Re-authoring the formula is what clears the legacy extent (Cell.FormulaText's setter).
        anchorCell.FormulaText = "A1+A2";
        anchorCell.Value = new NumberValue(3);
        anchorCell.LegacyArrayRows.Should().Be(0u, "sanity: re-authoring drops the fixed extent");
        anchorCell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic, "sanity: the array-mode guard is not what fires here");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_formula_array_extent");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();
    }

    /// <summary>
    /// No-regression sibling: an ordinary (non-array) cell edit in the same workbook still patches
    /// cleanly — the new extent check must not over-fire on cells that were never array formulas.
    /// </summary>
    [Fact]
    public void Save_OrdinaryLiteralEdit_StillPatchesSourcePackage()
    {
        var sourceBytes = R155LegacyArrayPackage.Create();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        sheet.GetCell(2, 2)!.LegacyArrayRows.Should().Be(2u,
            "an unrelated cell's patch must not disturb the array anchor either");
    }
}

/// <summary>
/// A minimal source package whose B2:B3 is a legacy CSE array formula (<c>&lt;f t="array"
/// ref="B2:B3"&gt;</c>) with a bare-value non-anchor member at B3 — the same shape
/// <c>XlsxSpillMemberDeletedCellPatchSaveTests</c> uses, plus a plain literal at A1 to edit.
/// </summary>
internal static class R155LegacyArrayPackage
{
    internal static byte[] Create()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191029"/>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:B3"/>
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c></row>
                    <row r="2"><c r="B2"><f t="array" ref="B2:B3">A1:A2</f><v>5</v></c></row>
                    <row r="3"><c r="B3"><v>7</v></c></row>
                  </sheetData>
                </worksheet>
                """));

        return package.ToArray();
    }
}
