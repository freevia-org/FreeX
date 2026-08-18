using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R140-io-dynamic-array-spill-growth-1: patch-save must not silently
/// drop a dynamic-array's new spill member cells when the array grows past its saved extent
/// without the anchor cell's own formula text, cached top-left value, style, or rich runs
/// changing. Growth is invisible to the plain occupied-cell diff because non-anchor spill
/// members live only in <see cref="Sheet"/>'s private spill-value overlay (populated via
/// <see cref="Sheet.SetSpillRange"/>), never in the ordinary cell dictionary that
/// <see cref="Sheet.GetOccupiedCellMap"/> returns.
/// </summary>
public sealed class XlsxSpillPatchSaveGrowthTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    /// <summary>
    /// Bug case, matching the reported failure scenario exactly: B2 holds a dynamic-array
    /// formula that round-trips on disk as a single-cell array formula
    /// (`&lt;f t="array" ref="B2:B2"&gt;`, exactly how <see cref="XlsxFileAdapter"/>'s own full
    /// save writes a currently-1x1 dynamic array). The user edits the unrelated driver cell A1
    /// and, in the same edit/recalc cycle, B2's formula grows to spill into B2:B4 with new
    /// values 1,2,3 (simulated the same way the sibling XlsxSpillMemberDeletedCellPatchSaveTests
    /// suite does: a direct <see cref="Sheet.SetSpillRange"/> call, mirroring what
    /// RecalcEngine.EvaluateSpilling would do). B2's own formula text, cached top-left value
    /// (1), style, and rich runs are all unchanged by the growth, so the patch-save diff must
    /// notice the extent change through some other signal or it will treat B2 as "nothing to
    /// patch" and never write B3/B4 at all.
    /// </summary>
    [Fact]
    public void Save_AfterUnrelatedInputChangeGrowsSpillExtent_FallsBackToFullSaveAndPreservesGrownMembers()
    {
        var sourceBytes = CreateSingleCellSpillSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var anchor = new CellAddress(sheet.Id, 2, 2); // B2

        // Sanity: B2 loaded as a 1x1 dynamic-array formula anchor with no spill members at all.
        var anchorCell = sheet.GetCell(2, 2)!;
        anchorCell.HasFormula.Should().BeTrue();
        anchorCell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);
        sheet.GetCell(3, 2).Should().BeNull("B3 must not exist yet -- the spill is still 1x1");
        sheet.TryGetArrayExtent(anchor, out _, out _, out _)
            .Should()
            .BeFalse("a genuinely 1x1 array-formula ref registers no spill extent until it actually spills");

        // Edit the driver cell A1 -- this alone is what proves to the top-level "did anything
        // change" gate that a real save is needed (a pure spill-only change with zero _cells
        // edits is a separate, deeper gap not covered by this fix -- see notes).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3));

        // Simulate the recalc that A1's new value triggers: B2's SEQUENCE-style formula now
        // spills 1x3 into B2:B4 with values 1,2,3. Sheet.SetSpillRange is the real production
        // API RecalcEngine.EvaluateSpilling calls; it writes the non-anchor members (B3, B4)
        // purely into Sheet._spillValues, never into Sheet._cells.
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) }
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));
        sheet.GetCell(3, 2).Should().BeNull("a spill member never gets its own Sheet._cells entry");
        sheet.GetCell(4, 2).Should().BeNull("a spill member never gets its own Sheet._cells entry");
        sheet.TryGetArrayExtent(anchor, out var liveAnchor, out var liveRows, out var liveCols).Should().BeTrue();
        liveAnchor.Should().Be(anchor);
        (liveRows * liveCols).Should().Be(3u);

        // B2's own formula text/cached top-left value/style/runs are completely unchanged by the
        // growth -- this is exactly the case the plain occupied-cell diff cannot see on its own.
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.FullSave,
            "growing a spill's extent without touching the anchor cell must be recognised, not silently skipped");
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_spill_extent");

        // The grown members must actually be present in the saved package (full-save's own
        // live-spill-extent-aware cell writer is responsible for this, exercised here end-to-end).
        ReadCellCachedValue(savedBytes, "xl/worksheets/sheet1.xml", "B3").Should().Be("2");
        ReadCellCachedValue(savedBytes, "xl/worksheets/sheet1.xml", "B4").Should().Be("3");

        // Reloading the saved file must show the full grown spill, not just the original 1x1.
        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(1));
        reloadedSheet.GetValue(3, 2).Should().Be(new NumberValue(2), "B3 must survive the save+reload round trip");
        reloadedSheet.GetValue(4, 2).Should().Be(new NumberValue(3), "B4 must survive the save+reload round trip");
    }

    /// <summary>
    /// Sibling (must-not-regress) case: a dynamic-array anchor whose spill extent stays exactly
    /// the same (still 1x1, matching what the baseline captured at load) while only its cached
    /// value changes -- exactly what RecalcEngine does to a formula cell after evaluating it --
    /// must still take the ordinary FormulaCachedValue patch path via SourcePatch. This guards
    /// against the new extent check over-firing and forcing an unnecessary full-save fallback
    /// whenever a spilling formula's value simply changes without growing or shrinking.
    /// </summary>
    [Fact]
    public void Save_WithSameExtentSpillValueChange_StillPatchesViaSourcePatch()
    {
        var sourceBytes = CreateSingleCellSpillSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);

        // Recalculate B2 in place -- same production field RecalcEngine writes
        // (cell.Value = result) -- to a new value while the spill stays 1x1 throughout.
        var anchorCell = sheet.GetCell(2, 2)!;
        anchorCell.Value = new NumberValue(99);
        sheet.TryGetArrayExtent(new CellAddress(sheet.Id, 2, 2), out _, out _, out _)
            .Should()
            .BeFalse("the spill never grew past its original 1x1 extent");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            XlsxSavePath.SourcePatch,
            "an extent-preserving spill value change must still take the ordinary patch path");
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadCellCachedValue(saved.ToArray(), "xl/worksheets/sheet1.xml", "B2").Should().Be("99");
    }

    /// <summary>
    /// Builds a source package with A1 = a plain literal driver cell, and B2 = a modern
    /// dynamic-array formula that currently spills into exactly one cell -- on disk this
    /// round-trips as a single-cell `&lt;f t="array" ref="B2:B2"&gt;`, exactly as
    /// <see cref="XlsxFileAdapter"/>'s own full save (XlsxFileAdapter.Save.cs) writes a
    /// currently-1x1 dynamic array, per the comment at XlsxFileAdapter.cs:404-411.
    /// </summary>
    private static byte[] CreateSingleCellSpillSourcePackage()
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
                  <dimension ref="A1:B2"/>
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c></row>
                    <row r="2"><c r="B2"><f t="array" ref="B2:B2">SEQUENCE(A1)</f><v>1</v></c></row>
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

    private static string? ReadCellCachedValue(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = TryReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell?.Name.Namespace;
        return ns is null ? null : cell!.Element(ns + "v")?.Value;
    }
}
