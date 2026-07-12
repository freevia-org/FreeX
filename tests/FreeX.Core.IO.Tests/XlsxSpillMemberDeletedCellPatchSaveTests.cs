using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R31-io-shared-array-formula-deep-1: patch-save must not silently
/// delete a legacy-CSE/dynamic-array spill-member cell whose <c> element carries no &lt;f&gt;
/// of its own (a bare `&lt;c&gt;&lt;v&gt;..&lt;/v&gt;&lt;/c&gt;`, exactly how Excel serialises
/// non-anchor spill cells) once the cached <see cref="XlsxFileAdapter"/> patch baseline goes
/// stale relative to a later spill reshape (<see cref="Sheet.SetSpillRange"/> moving the member
/// out of <c>Sheet._cells</c> into <c>Sheet._spillValues</c>).
/// </summary>
public sealed class XlsxSpillMemberDeletedCellPatchSaveTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    /// <summary>
    /// Bug case: B2 is a legacy CSE array-formula anchor (`&lt;f t="array" ref="B2:B3"&gt;`) and
    /// B3 is its bare-value non-anchor spill member, loaded exactly as
    /// <c>XlsxFileAdapter.Load</c>'s provisional-spill-cell path does. The first save (after an
    /// unrelated edit to A1) resolves and caches the patch baseline while B3 still matches the
    /// source (patches cleanly). Then a simulated recalc re-spills B2 to the *same* extent with a
    /// different value — mirroring <c>RecalcEngine.EvaluateSpilling</c> calling
    /// <c>Sheet.SetSpillRange</c> — which moves B3 out of <c>Sheet._cells</c> into
    /// <c>Sheet._spillValues</c>. The stale cached baseline still lists B3 as occupied, so the
    /// diff loop classifies it as <c>DeletedCell</c>; the fix must recognise that B3 is still a
    /// live spill member and bail to the full-save fallback instead of removing its &lt;c&gt;
    /// element outright.
    /// </summary>
    [Fact]
    public void Save_AfterUnrelatedEditThenSpillReshapeVacatesProvisionalMember_FallsBackToFullSaveInsteadOfDeletingCell()
    {
        var sourceBytes = CreateArraySpillSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2

        // Sanity: B3 loaded as a provisional spill-member value cell (no formula of its own).
        sheet.GetCell(3, 2)!.Value.Should().Be(new NumberValue(7));
        sheet.GetCell(3, 2)!.HasFormula.Should().BeFalse();

        // Step 1: edit an unrelated cell and save. This resolves + permanently caches the
        // CellPatchBaseline while B3 still matches the source (patch succeeds cleanly).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

        using (var firstSave = new MemoryStream())
        {
            adapter.Save(workbook, firstSave);
            adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
            adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        }

        // Step 2: simulate a recalc that re-spills B2 to the same B2:B3 extent with a new value.
        // Sheet.SetSpillRange removes the provisional B3 entry from _cells (via ClearSpillRange)
        // before writing the fresh result into _spillValues — B3 is now live only via spill
        // values, exactly as RecalcEngine.EvaluateSpilling would leave it.
        var cells = new ScalarValue[2, 1]
        {
            { new NumberValue(5) },
            { new NumberValue(9) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));
        sheet.GetCell(3, 2).Should().BeNull("the provisional member cell must vacate _cells once it becomes a live spill value");

        // Step 3: save again. The stale cached baseline still lists B3 as occupied, but it is no
        // longer in currentCells (GetOccupiedCellMap only returns _cells) — the diff loop would
        // classify it DeletedCell. The fix must recognise B3 is still a live spill member and
        // bail to full-save instead of silently removing its <c> element.
        using var secondSave = new MemoryStream();
        adapter.Save(workbook, secondSave);
        var savedBytes = secondSave.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_deleted_cell_still_spilled");

        // The cell must not have vanished from the saved package outright.
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "B3")
            .Should()
            .NotBeNull("a live spill-member cell must not be silently dropped from the saved package");

        // Reloading must still recognise B2 as a (re-spillable) array/dynamic-array formula —
        // the array identity must survive the fallback, not just the raw cell text.
        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedCell = reloaded.GetSheetAt(0).GetCell(2, 2)!;
        reloadedCell.HasFormula.Should().BeTrue();
        reloadedCell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
    }

    /// <summary>
    /// Sibling (already-working) case: a genuinely deleted plain literal cell — never any part of
    /// a spill range — must still take the normal DeletedCell patch path (not bail to full-save).
    /// This guards against the fix over-firing on ordinary cell clears.
    /// </summary>
    [Fact]
    public void Save_WithGenuinelyClearedNonSpillCell_StillPatchesAsDeletedCell()
    {
        var sourceBytes = CreateArraySpillSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);

        // A1 is a plain literal cell with no formula and no spill membership whatsoever.
        sheet.ClearCell(1, 1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "A1").Should().BeNull();

        // The unrelated array formula anchor and its provisional member are untouched.
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "B2").Should().NotBeNullOrEmpty();
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "B3").Should().NotBeNull();
    }

    /// <summary>
    /// Builds a source package with A1 = a plain literal (the "unrelated cell" for step 1), and
    /// B2:B3 = a legacy CSE array formula (`&lt;f t="array" ref="B2:B3"&gt;`) whose non-anchor
    /// member B3 is a bare `&lt;c&gt;&lt;v&gt;..&lt;/v&gt;&lt;/c&gt;` — exactly how
    /// <c>XlsxFileAdapter.Load</c>'s provisional-spill-cell comment (XlsxFileAdapter.cs:397-401)
    /// describes Excel 365's own on-disk encoding of non-anchor spill cells.
    /// </summary>
    private static byte[] CreateArraySpillSourcePackage()
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

    private static XElement? TryReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));
    }

    private static string? ReadCellFormula(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = TryReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell?.Name.Namespace;
        return ns is null ? null : cell!.Element(ns + "f")?.Value;
    }
}
