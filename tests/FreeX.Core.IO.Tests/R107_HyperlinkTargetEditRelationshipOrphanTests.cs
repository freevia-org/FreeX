using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class R107_HyperlinkTargetEditRelationshipOrphanTests
{
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void Save_AfterEditingExternalHyperlinkTargetTwice_DoesNotAccumulateOrphanedRelationships()
    {
        var workbook = new Workbook("HyperlinkEditTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Link"));
        sheet.Hyperlinks[address] = "https://example.com/original";

        var adapter = new XlsxFileAdapter();
        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);
        var firstBytes = firstSave.ToArray();

        // Load back so the adapter tracks a source-package snapshot (the pre-edit package) for the
        // next save -- exactly the real user flow of opening a file, editing it, and saving again.
        using var loadStream1 = new MemoryStream(firstBytes, writable: false);
        var reloaded1 = adapter.Load(loadStream1);
        var reloadedSheet1 = reloaded1.GetSheetAt(0);
        var reloadedAddress1 = new CellAddress(reloadedSheet1.Id, 1, 1);
        reloadedSheet1.Hyperlinks[reloadedAddress1].Should().Be("https://example.com/original");

        // Act: edit (not remove) the hyperlink's target URL on the SAME cell and save again.
        reloadedSheet1.Hyperlinks[reloadedAddress1] = "https://example.com/edited-once";

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded1, secondSave);
        var secondBytes = secondSave.ToArray();

        // Premise: editing an external hyperlink's target always bails the fast cell-patch path onto
        // a full (ClosedXML) save (XlsxWorksheetHyperlinkPatch.TryCreate bails whenever
        // source.HasRelationshipId is true), exactly the path that exercises
        // XlsxPackageMetadataMerger.MergeRelationshipParts / ShouldPreserveRelationship.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        var externalTargetsAfterFirstEdit = ReadExternalHyperlinkTargets(secondBytes);
        externalTargetsAfterFirstEdit.Should().BeEquivalentTo(
            new[] { "https://example.com/edited-once" },
            "editing a hyperlink's target must rewrite the relationship in place, leaving no trace " +
            "of the replaced URL behind in the worksheet's .rels part (Excel's own edit-in-place behavior)");

        // Repeat the edit-and-save cycle a second time to prove the orphan does not merely appear
        // once but keeps accumulating across repeated edits of the same hyperlink.
        using var loadStream2 = new MemoryStream(secondBytes, writable: false);
        var reloaded2 = adapter.Load(loadStream2);
        var reloadedSheet2 = reloaded2.GetSheetAt(0);
        var reloadedAddress2 = new CellAddress(reloadedSheet2.Id, 1, 1);
        reloadedSheet2.Hyperlinks[reloadedAddress2] = "https://example.com/edited-twice";

        using var thirdSave = new MemoryStream();
        adapter.Save(reloaded2, thirdSave);
        var thirdBytes = thirdSave.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        var externalTargetsAfterSecondEdit = ReadExternalHyperlinkTargets(thirdBytes);
        externalTargetsAfterSecondEdit.Should().BeEquivalentTo(
            new[] { "https://example.com/edited-twice" },
            "repeated edits to the same hyperlink must not leave every prior URL behind as an " +
            "ever-growing pile of orphaned, unreferenced relationships in the .rels part");
    }

    [Fact]
    public void Save_AfterEditingOneOfTwoExternalHyperlinkTargets_LeavesTheOtherHyperlinkIntact()
    {
        // Sibling no-regression case: fixing the edited hyperlink's orphan must not disturb a
        // second, untouched external hyperlink relationship on the same worksheet.
        var workbook = new Workbook("HyperlinkEditSiblingTest");
        var sheet = workbook.AddSheet("S1");
        var editedAddress = new CellAddress(sheet.Id, 1, 1);
        var untouchedAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(editedAddress, new TextValue("Edited"));
        sheet.SetCell(untouchedAddress, new TextValue("Untouched"));
        sheet.Hyperlinks[editedAddress] = "https://example.com/edited-original";
        sheet.Hyperlinks[untouchedAddress] = "https://example.com/untouched";

        var adapter = new XlsxFileAdapter();
        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);
        var firstBytes = firstSave.ToArray();

        using var loadStream = new MemoryStream(firstBytes, writable: false);
        var reloaded = adapter.Load(loadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedEditedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        var reloadedUntouchedAddress = new CellAddress(reloadedSheet.Id, 2, 1);

        reloadedSheet.Hyperlinks[reloadedEditedAddress] = "https://example.com/edited-new";

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);
        var secondBytes = secondSave.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        var externalTargets = ReadExternalHyperlinkTargets(secondBytes);
        externalTargets.Should().BeEquivalentTo(
            new[] { "https://example.com/edited-new", "https://example.com/untouched" },
            "the untouched hyperlink's relationship must survive unchanged while the edited one's old " +
            "target must not linger as an orphan");

        using var reloadStream = new MemoryStream(secondBytes, writable: false);
        var reloadedAgain = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedAgain.Hyperlinks[new CellAddress(reloadedAgain.Id, 1, 1)].Should().Be("https://example.com/edited-new");
        reloadedAgain.Hyperlinks[new CellAddress(reloadedAgain.Id, 2, 1)].Should().Be("https://example.com/untouched");
    }

    private static List<string> ReadExternalHyperlinkTargets(byte[] savedBytes)
    {
        using var package = new MemoryStream(savedBytes, writable: false);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        if (entry is null)
            return [];

        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root;
        if (root is null)
            return [];

        return root.Elements(RelationshipNs + "Relationship")
            .Where(element => (string?)element.Attribute("TargetMode") == "External")
            .Select(element => (string?)element.Attribute("Target"))
            .Where(target => !string.IsNullOrEmpty(target))
            .Select(target => target!)
            .ToList();
    }
}
