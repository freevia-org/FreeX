using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 28 finding R28-io-unknown-part-passthrough-deep-1: a
/// worksheet-level custom property's xl/customProperty/*.bin payload (the property's real
/// Excel/VBA-authored VALUE bytes) was permanently destroyed -- replaced with a fabricated
/// stub containing only the property's own name -- on every full-rebuild save, because
/// XlsxWorksheetCustomPropertyMapper never captured the original bytes on load.
/// </summary>
public sealed class R28_WorksheetCustomPropertyBinPayloadPreservationTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    private static readonly byte[] DistinctOriginalPayload = [0x01, 0x02, 0x03, 0xFF, 0x00, 0x42, 0x99, 0x7A];

    [Fact]
    public void LoadedWorkbookFullSave_PreservesOriginalCustomPropertyBinPayload()
    {
        // Bug case: the source package's .bin part holds real value bytes that are NOT
        // merely a re-encoding of the property's own name (e.g. Worksheet.CustomProperties
        // authored via Excel/VBA). A full-rebuild save must round-trip those exact bytes.
        using var source = new MemoryStream();
        BuildSourceWorkbookWithCustomPropertyPayload(source, DistinctOriginalPayload);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.CustomProperties.Should().ContainSingle().Which.Name.Should().Be("MyKey");

        // Force the full-rebuild save path with a plain cell edit (no
        // TryPrepareLoadedPackageSnapshotForEdit opt-in, so patch-save is never eligible),
        // leaving the custom property's own Id/name untouched -- exactly the common
        // "insert a row / change a cell's number format" scenario from the finding.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        saved.Position = 0;
        ReadCustomPropertyPartBytes(saved, "MyKey").Should().Equal(DistinctOriginalPayload);

        saved.Position = 0;
        var reloadedSheet = new XlsxFileAdapter().Load(saved).GetSheetAt(0);
        reloadedSheet.CustomProperties.Should().ContainSingle().Which.Name.Should().Be("MyKey");
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithoutCapturedPayload_StillWritesNonEmptyPlaceholder()
    {
        // Sibling already-working case: a brand-new custom property added directly to the
        // model (never loaded from a source package, so no original .bin bytes are known)
        // must still produce a valid, non-empty part on save -- the pre-existing
        // name-derived placeholder fallback -- so genuinely-new properties are unaffected.
        var workbook = new Workbook("FreshCustomProperty");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Property"));
        sheet.CustomProperties.Add(new WorksheetCustomProperty("FreshProperty", 3));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        var bytes = ReadCustomPropertyPartBytes(saved, "FreshProperty");
        bytes.Should().NotBeEmpty();
        Encoding.Unicode.GetString(bytes).Should().Be("FreshProperty");
    }

    private static void BuildSourceWorkbookWithCustomPropertyPayload(MemoryStream destination, byte[] payload)
    {
        var workbook = new Workbook("WorksheetCustomPropertyBinPayload");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Property"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));
        sheet.CustomProperties.Add(new WorksheetCustomProperty("MyKey", 1));

        new XlsxFileAdapter().Save(workbook, destination);

        // Overwrite the just-written placeholder .bin content with a distinct byte payload,
        // simulating a genuine Excel/VBA-authored custom property value that FreeX itself
        // never writes but must still preserve on round trip.
        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Update, leaveOpen: true);
        var partPath = ResolveCustomPropertyPartPath(archive, "MyKey");
        archive.GetEntry(partPath)!.Delete();
        var entry = archive.CreateEntry(partPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(payload, 0, payload.Length);
    }

    private static byte[] ReadCustomPropertyPartBytes(Stream stream, string propertyName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var partPath = ResolveCustomPropertyPartPath(archive, propertyName);
        using var partStream = archive.GetEntry(partPath)!.Open();
        using var bytes = new MemoryStream();
        partStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    // Resolves the .bin part actually referenced by the named property's <customPr r:id="...">
    // in the worksheet XML -- rather than merely the first customProperty-typed relationship
    // in the .rels file -- so the lookup stays correct even if a full-rebuild save leaves a
    // stale/orphaned relationship of the same type behind.
    private static string ResolveCustomPropertyPartPath(ZipArchive archive, string propertyName)
    {
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        using var worksheetStream = worksheetEntry.Open();
        var worksheetXml = XDocument.Load(worksheetStream);
        var customPr = worksheetXml.Root!
            .Element(WorksheetNs + "customProperties")!
            .Elements(WorksheetNs + "customPr")
            .Single(element => element.Attribute("name")?.Value == propertyName);
        var relationshipId = customPr.Attribute(RelNs + "id")!.Value;

        var relsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!;
        using var relsStream = relsEntry.Open();
        var relsXml = XDocument.Load(relsStream);
        var relationship = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Single(element => element.Attribute("Id")?.Value == relationshipId);
        return XlsxPackagePath.ResolveRelationshipTarget(WorksheetPath, relationship.Attribute("Target")!.Value);
    }
}
