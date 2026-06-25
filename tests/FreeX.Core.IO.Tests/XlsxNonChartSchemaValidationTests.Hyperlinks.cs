using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void WorksheetHyperlinks_ProducesSchemaValidWorkbook()
    {
        SchemaErrors(CreateWorksheetHyperlinksSourceWorkbook()).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetHyperlinks_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetHyperlinksSourceWorkbook());
        var sourceHyperlinks = ReadWorksheetChildElement(source, "hyperlinks");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        AssertWorksheetHyperlinksModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "hyperlinks")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceHyperlinks.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        AssertWorksheetHyperlinksModel(adapter.Load(saved).GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetHyperlinksForSchemaValidity()
    {
        using var source = Save(CreateWorksheetHyperlinksSourceWorkbook());
        SetWorksheetHyperlinksInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetHyperlinksSanitized(saved);

        saved.Position = 0;
        AssertWorksheetHyperlinksModel(adapter.Load(saved).GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetHyperlinksForSchemaValidity()
    {
        using var source = Save(CreateWorksheetHyperlinksSourceWorkbook());
        SetWorksheetHyperlinksInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetHyperlinksSanitized(saved);

        saved.Position = 0;
        AssertWorksheetHyperlinksModel(adapter.Load(saved).GetSheetAt(0));
    }

    private static void SetWorksheetHyperlinksInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var hyperlinks = worksheetXml.Root!.Element(worksheetNs + "hyperlinks")!;
        hyperlinks.SetAttributeValue("nativeHyperlinksAttr", "kept");
        hyperlinks.Add(new XElement(freexNs + "nativeHyperlinksChild"));

        var hyperlink = hyperlinks
            .Elements(worksheetNs + "hyperlink")
            .Single(element => element.Attribute("ref")?.Value == "A1");
        hyperlink.SetAttributeValue("display", "FreeX docs");
        hyperlink.SetAttributeValue("customAttr", "hyperlink-native");
        hyperlink.Add(new XElement(
            freexNs + "nativeHyperlinkChild",
            new XAttribute("id", "hyperlink")));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertWorksheetHyperlinksSanitized(MemoryStream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace freexNs = "urn:freex:test";
        var hyperlinks = ReadWorksheetChildElement(stream, "hyperlinks");
        hyperlinks.Attribute("nativeHyperlinksAttr").Should().BeNull();
        hyperlinks.Element(freexNs + "nativeHyperlinksChild").Should().BeNull();

        var external = hyperlinks
            .Elements(worksheetNs + "hyperlink")
            .Single(element => element.Attribute("ref")?.Value == "A1");
        external.Attribute(relationshipNs + "id").Should().NotBeNull();
        external.Attribute("tooltip")!.Value.Should().Be("Open documentation");
        external.Attribute("display")!.Value.Should().Be("FreeX docs");
        external.Attribute("customAttr").Should().BeNull();
        external.Element(freexNs + "nativeHyperlinkChild").Should().BeNull();

        var internalLink = hyperlinks
            .Elements(worksheetNs + "hyperlink")
            .Single(element => element.Attribute("ref")?.Value == "A2");
        internalLink.Attribute("location")!.Value.Should().Be("Data!B2");
        internalLink.Attribute("tooltip")!.Value.Should().Be("Jump to details");
    }

    private static void AssertWorksheetHyperlinksModel(Sheet sheet)
    {
        var externalAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks.Should().ContainKey(externalAddress);
        sheet.Hyperlinks[externalAddress].Should().Be("https://example.com/docs");
        sheet.HyperlinkMetadata.Should().ContainKey(externalAddress);
        sheet.HyperlinkMetadata[externalAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open documentation",
            ""));

        var internalAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.Hyperlinks.Should().ContainKey(internalAddress);
        sheet.Hyperlinks[internalAddress].Should().Be("Data!B2");
        sheet.HyperlinkMetadata.Should().ContainKey(internalAddress);
        sheet.HyperlinkMetadata[internalAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to details",
            "Data!B2"));
    }

    private static Workbook CreateWorksheetHyperlinksSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetHyperlinksPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        var externalAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(externalAddress, new TextValue("Open docs"));
        sheet.Hyperlinks[externalAddress] = "https://example.com/docs";
        sheet.HyperlinkMetadata[externalAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open documentation",
            "");

        var internalAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(internalAddress, new TextValue("Jump inside"));
        sheet.Hyperlinks[internalAddress] = "Data!B2";
        sheet.HyperlinkMetadata[internalAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to details",
            "Data!B2");

        return workbook;
    }

    /// <summary>
    /// Regression test for the P6 hyperlink-normalizer bug: a hyperlink whose <c>ref</c>
    /// attribute is a whole-column reference (e.g. <c>A:A</c>) or whole-row reference
    /// (e.g. <c>3:3</c>) must survive a round-trip without being silently removed.
    /// </summary>
    [Fact]
    public void HyperlinkNormalizer_PreservesWholeColumnAndRowRefs()
    {
        // Build a workbook and inject raw hyperlink elements with column/row-wide refs.
        var workbook = new Workbook("WholeColumnRowHyperlinks");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        using var stream = Save(workbook);
        stream.Position = 0;

        // Inject hyperlinks with column-only (A:A) and row-only (3:3) refs.
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        // Add an external relationship for the hyperlinks.
        const string sheet1RelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var relsXml = archive.GetEntry(sheet1RelsPath) is { } relsEntry
            ? LoadPackageXml(relsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        relsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdColHyperlink"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
            new XAttribute("Target", "https://example.com/col"),
            new XAttribute("TargetMode", "External")));
        relsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdRowHyperlink"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
            new XAttribute("Target", "https://example.com/row"),
            new XAttribute("TargetMode", "External")));
        ReplacePackageXml(archive, sheet1RelsPath, relsXml);

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var hyperlinkContainer = worksheetXml.Root!.Element(worksheetNs + "hyperlinks")
            ?? new XElement(worksheetNs + "hyperlinks");
        hyperlinkContainer.Add(new XElement(
            worksheetNs + "hyperlink",
            new XAttribute("ref", "A:A"),
            new XAttribute(relNs + "id", "rIdColHyperlink")));
        hyperlinkContainer.Add(new XElement(
            worksheetNs + "hyperlink",
            new XAttribute("ref", "3:3"),
            new XAttribute(relNs + "id", "rIdRowHyperlink")));
        if (worksheetXml.Root.Element(worksheetNs + "hyperlinks") is null)
            worksheetXml.Root.Add(hyperlinkContainer);
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        archive.Dispose();

        // Save again through the adapter (triggers normalization on the full-save path).
        stream.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(stream);

        loaded.GetSheetAt(0).SetCell(
            new CellAddress(loaded.GetSheetAt(0).Id, 9, 9),
            new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        // The normalizer must not drop the whole-column/row hyperlink elements.
        saved.Position = 0;
        using var resultArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var savedWorksheetXml = LoadPackageXml(resultArchive, "xl/worksheets/sheet1.xml");
        var savedHyperlinks = savedWorksheetXml.Root!
            .Element(worksheetNs + "hyperlinks")?
            .Elements(worksheetNs + "hyperlink")
            .Select(e => e.Attribute("ref")?.Value)
            .ToList()
            ?? [];

        savedHyperlinks.Should().Contain("A:A",
            "a whole-column hyperlink ref must survive normalization");
        savedHyperlinks.Should().Contain("3:3",
            "a whole-row hyperlink ref must survive normalization");
    }
}
