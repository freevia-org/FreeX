using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R41-io-worksheet-usedrange-dimension-3-1: a patch-save that removes
/// the last remaining &lt;c&gt; element(s) from a worksheet must recompute the &lt;dimension ref&gt;
/// down to the actual (now empty) used range instead of leaving the stale far-from-A1 reference
/// in place. <see cref="XlsxFileAdapter.SourcePackageSnapshot"/>'s UpdateDimension previously
/// bailed out with no write at all whenever the post-patch cell set was empty (minRow/minCol
/// sentinels never got updated), so the source file's original dimension survived untouched.
/// </summary>
public sealed class XlsxClearedCellUsedRangeDimensionPatchSaveTests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    [Fact]
    public void Save_PatchClearsOnlyCellInSheet_RewritesDimensionToA1()
    {
        // Source sheet's only content is a single far-from-A1 cell, exactly like the bug report:
        // <dimension ref="Z100:Z100"/> with nothing at/near A1.
        var sourceBytes = CreateSourcePackage(("Z100", "far value"));
        ReadDimensionRef(sourceBytes, "xl/worksheets/sheet1.xml").Should().Be("Z100");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(new CellAddress(sheet.Id, 100, 26)); // Z100

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // Must still be a cheap in-place patch save, not a full-save fallback, so this actually
        // exercises XlsxCellPatchBaseline.ApplyChanges -> UpdateDimension.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        ReadDimensionRef(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1", "a sheet with no cells left must have its used-range dimension reset to A1, matching real Excel, instead of keeping the stale far-from-A1 reference");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(0).GetCell(100, 26).Should().BeNull();
    }

    [Fact]
    public void Save_PatchClearsOneOfSeveralCells_RecomputesDimensionToRemainingUsedRange()
    {
        // Sibling/no-regression case: clearing a far cell while other cells remain must still
        // shrink the dimension down to the real remaining bounding box (the pre-existing,
        // already-correct code path for a non-empty post-patch cell set), not collapse to "A1"
        // unconditionally.
        var sourceBytes = CreateSourcePackage(("A1", "keep 1"), ("B2", "keep 2"), ("Z100", "clear me"));
        ReadDimensionRef(sourceBytes, "xl/worksheets/sheet1.xml").Should().Be("A1:Z100");

        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(new CellAddress(sheet.Id, 100, 26)); // Z100

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);

        ReadDimensionRef(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1:B2", "the dimension must shrink to the actual remaining used range once the far cell is cleared");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue("keep 1"));
        reloaded.GetSheetAt(0).GetCell(2, 2)!.Value.Should().Be(new TextValue("keep 2"));
        reloaded.GetSheetAt(0).GetCell(100, 26).Should().BeNull();
    }

    private static byte[] CreateSourcePackage(params (string Reference, string Value)[] cells)
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            foreach (var (reference, value) in cells)
                sheet.Cell(reference).Value = value;
            workbook.SaveAs(stream);
        }

        // ClosedXML always pads its written <dimension ref> down to A1 as the top-left corner,
        // regardless of where the actual data starts. The real-world bug this test guards
        // against only reproduces when the *source* file's dimension genuinely starts far from
        // A1 (exactly like a real Excel-authored file whose only content is at e.g. Z100), so
        // force the saved dimension to the true bounding box of the cells this fixture asked for.
        return ForceDimensionRef(
            RemoveEmptyWorkbookDefinedNames(stream.ToArray()),
            "xl/worksheets/sheet1.xml",
            ComputeDimensionRef(cells.Select(cell => cell.Reference)));
    }

    private static string ComputeDimensionRef(IEnumerable<string> references)
    {
        uint minRow = uint.MaxValue, minCol = uint.MaxValue, maxRow = 0, maxCol = 0;
        foreach (var reference in references)
        {
            var (row, col) = ParseReference(reference);
            minRow = Math.Min(minRow, row);
            minCol = Math.Min(minCol, col);
            maxRow = Math.Max(maxRow, row);
            maxCol = Math.Max(maxCol, col);
        }

        var start = ToReference(minRow, minCol);
        var end = ToReference(maxRow, maxCol);
        return start == end ? start : $"{start}:{end}";
    }

    private static (uint Row, uint Col) ParseReference(string reference)
    {
        var splitIndex = 0;
        while (splitIndex < reference.Length && char.IsLetter(reference[splitIndex]))
            splitIndex++;

        var colPart = reference[..splitIndex];
        var rowPart = reference[splitIndex..];

        uint col = 0;
        foreach (var ch in colPart)
            col = col * 26 + (uint)(char.ToUpperInvariant(ch) - 'A' + 1);

        return (uint.Parse(rowPart), col);
    }

    private static string ToReference(uint row, uint col)
    {
        var colLetters = "";
        var remaining = col;
        while (remaining > 0)
        {
            var rem = (remaining - 1) % 26;
            colLetters = (char)('A' + rem) + colLetters;
            remaining = (remaining - 1) / 26;
        }

        return $"{colLetters}{row}";
    }

    private static byte[] ForceDimensionRef(byte[] packageBytes, string worksheetPath, string dimensionRef)
    {
        using var stream = new MemoryStream();
        stream.Write(packageBytes, 0, packageBytes.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, worksheetPath);
            worksheetXml.Root!.Element(worksheetNs + "dimension")!.SetAttributeValue("ref", dimensionRef);

            archive.GetEntry(worksheetPath)?.Delete();
            var replacement = archive.CreateEntry(worksheetPath);
            using var replacementStream = replacement.Open();
            worksheetXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
    }

    private static byte[] RemoveEmptyWorkbookDefinedNames(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml");
            var changed = false;
            foreach (var definedNames in workbookXml.Root!.Elements(workbookNs + "definedNames").ToList())
            {
                if (definedNames.HasElements || definedNames.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
                    continue;

                definedNames.Remove();
                changed = true;
            }

            if (changed)
            {
                archive.GetEntry("xl/workbook.xml")?.Delete();
                var replacement = archive.CreateEntry("xl/workbook.xml");
                using var replacementStream = replacement.Open();
                workbookXml.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
            }
        }

        return stream.ToArray();
    }

    private static string? ReadDimensionRef(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(worksheetPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var doc = XDocument.Load(entryStream);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return (string?)doc.Root!.Element(worksheetNs + "dimension")?.Attribute("ref");
    }
}
