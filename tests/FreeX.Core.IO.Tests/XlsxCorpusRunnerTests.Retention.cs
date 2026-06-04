using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    [Fact]
    public void GeneratedKnownGapRows_RetainCriticalPackagePartsAfterModelEdit()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-known-gap")
            .Where(row => XlsxCorpusFixtureFactory.CanCreateKnownGapRetentionPackage(row.Id))
            .ToArray();

        rows.Should().NotBeEmpty("known-gap retention packages catch XLSX package loss during ordinary model edits");

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(row.Id);
            var before = CapturePackageSummary(source);
            var fixtureParts = CaptureKnownGapFixtureParts(row.Id);
            before.CriticalParts.Should().Contain(fixtureParts, row.Id);
            var fixtureContentTypeOverrides = ContentTypeOverridesForParts(before, fixtureParts);
            fixtureContentTypeOverrides.Should().NotBeEmpty(row.Id);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("freex-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            var after = CapturePackageSummary(saved);

            after.CriticalParts.Should().Contain(before.CriticalParts, row.Id);
            after.CriticalRelationshipTargets.Should().Contain(before.CriticalRelationshipTargets, row.Id);
            after.CriticalRelationshipDetails.Should().Contain(before.CriticalRelationshipDetails, row.Id);
            after.CriticalContentTypeOverrides.Should().Contain(before.CriticalContentTypeOverrides, row.Id);
            after.CriticalContentTypeOverrides.Should().Contain(fixtureContentTypeOverrides, row.Id);
        }
    }

    [Fact]
    public void GeneratedPowerQueryRetentionPackage_LinksQueryTableFromWorksheet()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-power-query-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        var worksheetRelsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        worksheetEntry.Should().NotBeNull();
        worksheetRelsEntry.Should().NotBeNull();

        XDocument worksheetXml;
        using (var stream = worksheetEntry!.Open())
            worksheetXml = XDocument.Load(stream);
        XDocument worksheetRelsXml;
        using (var stream = worksheetRelsEntry!.Open())
            worksheetRelsXml = XDocument.Load(stream);

        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var queryTablePart = worksheetXml.Root!
            .Element(sheetNs + "queryTableParts")!
            .Elements(sheetNs + "queryTablePart")
            .Should().ContainSingle().Subject;
        var relationshipId = queryTablePart.Attribute(relNs + "id")!.Value;

        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == relationshipId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable" &&
                relationship.Attribute("Target")?.Value == "../queryTables/queryTable1.xml")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedLiveWebQueriesRetentionPackage_IncludesWebQueryConnectionMetadata()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-live-web-queries-001");
        var report = XlsxFeatureInspector.Inspect(package);
        report.Features.Select(feature => feature.Kind).Distinct().Should().BeEquivalentTo(
            new[] { XlsxUnsupportedFeatureKind.LiveWebQueries });

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var connectionsEntry = archive.GetEntry("xl/connections.xml");
        var webPublishItemsEntry = archive.GetEntry("xl/webPublishItems.xml");
        connectionsEntry.Should().NotBeNull();
        webPublishItemsEntry.Should().NotBeNull();

        XDocument connectionsXml;
        using (var stream = connectionsEntry!.Open())
            connectionsXml = XDocument.Load(stream);

        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var webPr = connectionsXml
            .Descendants(sheetNs + "webPr")
            .Should()
            .ContainSingle()
            .Subject;
        webPr.Attribute("url")?.Value.Should().Be("https://example.com/freex-web-query.html");
    }

    [Fact]
    public void GeneratedUnsupportedSheetTypesRetentionPackage_ListsWorkbookSheetReferences()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-unsupported-sheet-types-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        workbookEntry.Should().NotBeNull();
        workbookRelsEntry.Should().NotBeNull();

        XDocument workbookXml;
        using (var stream = workbookEntry!.Open())
            workbookXml = XDocument.Load(stream);
        XDocument workbookRelsXml;
        using (var stream = workbookRelsEntry!.Open())
            workbookRelsXml = XDocument.Load(stream);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sheets = workbookXml.Root!
            .Element(workbookNs + "sheets")!
            .Elements(workbookNs + "sheet")
            .ToArray();
        sheets.Where(sheet => sheet.Attribute("name")?.Value == "FreeX Chart Sheet").Should().ContainSingle();
        sheets.Where(sheet => sheet.Attribute("name")?.Value == "FreeX Dialog Sheet").Should().ContainSingle();
        sheets.Where(sheet => sheet.Attribute("name")?.Value == "FreeX Macro Sheet").Should().ContainSingle();

        var relationshipsById = workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .ToDictionary(relationship => relationship.Attribute("Id")!.Value, relationship => relationship);
        relationshipsById[sheets.Single(sheet => sheet.Attribute("name")?.Value == "FreeX Chart Sheet").Attribute(relNs + "id")!.Value]
            .Attribute("Target")!.Value.Should().Be("chartsheets/sheet1.xml");
        relationshipsById[sheets.Single(sheet => sheet.Attribute("name")?.Value == "FreeX Dialog Sheet").Attribute(relNs + "id")!.Value]
            .Attribute("Target")!.Value.Should().Be("dialogSheets/sheet2.xml");
        relationshipsById[sheets.Single(sheet => sheet.Attribute("name")?.Value == "FreeX Macro Sheet").Attribute(relNs + "id")!.Value]
            .Attribute("Target")!.Value.Should().Be("macroSheets/sheet3.xml");
    }

    [Fact]
    public void GeneratedVbaMacrosRetentionPackage_LinksWorkbookToVbaProject()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-vba-macros-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var vbaProjectEntry = archive.GetEntry("xl/vbaProject.bin");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        vbaProjectEntry.Should().NotBeNull();
        workbookRelsEntry.Should().NotBeNull();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument workbookRelsXml;
        using (var stream = workbookRelsEntry!.Open())
            workbookRelsXml = XDocument.Load(stream);

        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2006/relationships/vbaProject" &&
                relationship.Attribute("Target")?.Value == "vbaProject.bin")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedThreadedCommentsRetentionPackage_LinksWorksheetAndPersonsParts()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-threaded-comments-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var worksheetRelsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        worksheetRelsEntry.Should().NotBeNull();
        workbookRelsEntry.Should().NotBeNull();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument worksheetRelsXml;
        using (var stream = worksheetRelsEntry!.Open())
            worksheetRelsXml = XDocument.Load(stream);
        XDocument workbookRelsXml;
        using (var stream = workbookRelsEntry!.Open())
            workbookRelsXml = XDocument.Load(stream);

        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" &&
                relationship.Attribute("Target")?.Value == "../threadedComments/threadedComment1.xml")
            .Should().ContainSingle();
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2017/10/relationships/person" &&
                relationship.Attribute("Target")?.Value == "persons/person.xml")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedFormControlsRetentionPackage_LinksWorksheetControlAndActiveXParts()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-form-controls-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        var worksheetRelsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        var activeXRelsEntry = archive.GetEntry("xl/activeX/_rels/activeX1.xml.rels");
        worksheetEntry.Should().NotBeNull();
        worksheetRelsEntry.Should().NotBeNull();
        activeXRelsEntry.Should().NotBeNull();

        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        XDocument worksheetXml;
        using (var stream = worksheetEntry!.Open())
            worksheetXml = XDocument.Load(stream);
        XDocument worksheetRelsXml;
        using (var stream = worksheetRelsEntry!.Open())
            worksheetRelsXml = XDocument.Load(stream);
        XDocument activeXRelsXml;
        using (var stream = activeXRelsEntry!.Open())
            activeXRelsXml = XDocument.Load(stream);

        var control = worksheetXml.Root!
            .Element(sheetNs + "controls")!
            .Elements(sheetNs + "control")
            .Should().ContainSingle().Subject;
        var controlRelationshipId = control.Attribute(relNs + "id")!.Value;
        control.Attribute("name")!.Value.Should().Be("FreeX Button");

        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == controlRelationshipId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp" &&
                relationship.Attribute("Target")?.Value == "../ctrlProps/ctrlProp1.xml")
            .Should().ContainSingle();
        activeXRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2006/relationships/activeXControlBinary" &&
                relationship.Attribute("Target")?.Value == "activeX1.bin")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedCustomRibbonUiRetentionPackage_LinksPackageRootToCustomUiPart()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-custom-ribbon-ui-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var packageRelsEntry = archive.GetEntry("_rels/.rels");
        packageRelsEntry.Should().NotBeNull();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument packageRelsXml;
        using (var stream = packageRelsEntry!.Open())
            packageRelsXml = XDocument.Load(stream);

        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2006/relationships/ui/extensibility" &&
                relationship.Attribute("Target")?.Value == "customUI/customUI.xml")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedOfficeAddinsRetentionPackage_LinksTaskpanesAndWebextensionParts()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-office-addins-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var packageRelsEntry = archive.GetEntry("_rels/.rels");
        var taskpanesRelsEntry = archive.GetEntry("xl/webextensions/_rels/taskpanes.xml.rels");
        packageRelsEntry.Should().NotBeNull();
        taskpanesRelsEntry.Should().NotBeNull();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument packageRelsXml;
        using (var stream = packageRelsEntry!.Open())
            packageRelsXml = XDocument.Load(stream);
        XDocument taskpanesRelsXml;
        using (var stream = taskpanesRelsEntry!.Open())
            taskpanesRelsXml = XDocument.Load(stream);

        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes" &&
                relationship.Attribute("Target")?.Value == "xl/webextensions/taskpanes.xml")
            .Should().ContainSingle();
        taskpanesRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/webextension" &&
                relationship.Attribute("Target")?.Value == "webextension1.xml")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedExternalLinksRetentionPackage_AnchorsExternalReferenceWithFormula()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-external-links-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        var externalLinkEntry = archive.GetEntry("xl/externalLinks/externalLink1.xml");
        var externalLinkRelsEntry = archive.GetEntry("xl/externalLinks/_rels/externalLink1.xml.rels");
        workbookEntry.Should().NotBeNull();
        workbookRelsEntry.Should().NotBeNull();
        worksheetEntry.Should().NotBeNull();
        externalLinkEntry.Should().NotBeNull();
        externalLinkRelsEntry.Should().NotBeNull();

        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        XDocument workbookXml;
        using (var stream = workbookEntry!.Open())
            workbookXml = XDocument.Load(stream);
        XDocument workbookRelsXml;
        using (var stream = workbookRelsEntry!.Open())
            workbookRelsXml = XDocument.Load(stream);
        XDocument worksheetXml;
        using (var stream = worksheetEntry!.Open())
            worksheetXml = XDocument.Load(stream);
        XDocument externalLinkXml;
        using (var stream = externalLinkEntry!.Open())
            externalLinkXml = XDocument.Load(stream);
        XDocument externalLinkRelsXml;
        using (var stream = externalLinkRelsEntry!.Open())
            externalLinkRelsXml = XDocument.Load(stream);

        var externalReferenceId = workbookXml.Root!
            .Element(sheetNs + "externalReferences")!
            .Elements(sheetNs + "externalReference")
            .Single()
            .Attribute(relNs + "id")!
            .Value;
        externalReferenceId.Should().Be("rIdFreeXExternalLink1");
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == externalReferenceId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink" &&
                relationship.Attribute("Target")?.Value == "externalLinks/externalLink1.xml")
            .Should().ContainSingle();

        var formulaCell = worksheetXml.Descendants(sheetNs + "c")
            .Single(cell => string.Equals(cell.Attribute("r")?.Value, "C1", StringComparison.OrdinalIgnoreCase));
        formulaCell.Element(sheetNs + "f")!.Value.Should().Be("[1]ExternalSheet!$A$1");
        formulaCell.Element(sheetNs + "v")!.Value.Should().Be("42");

        var externalBook = externalLinkXml.Root!.Element(sheetNs + "externalBook")!;
        externalBook.Attribute(relNs + "id")!.Value.Should().Be("rIdExternalBook1");
        externalBook.Element(sheetNs + "sheetNames")!
            .Elements(sheetNs + "sheetName")
            .Single()
            .Attribute("val")!
            .Value
            .Should().Be("ExternalSheet");
        externalBook.Element(sheetNs + "sheetDataSet")!
            .Element(sheetNs + "sheetData")!
            .Element(sheetNs + "row")!
            .Element(sheetNs + "cell")!
            .Element(sheetNs + "v")!
            .Value
            .Should().Be("42");

        externalLinkRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdExternalBook1" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath" &&
                relationship.Attribute("Target")?.Value == "ExternalWorkbook.xlsx" &&
                relationship.Attribute("TargetMode")?.Value == "External")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedSmartArtDiagramsRetentionPackage_LinksWorksheetDrawingAndDiagramParts()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-smartart-diagrams-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        var worksheetRelsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        var drawingRelsEntry = archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels");
        worksheetEntry.Should().NotBeNull();
        worksheetRelsEntry.Should().NotBeNull();
        drawingRelsEntry.Should().NotBeNull();

        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        XDocument worksheetXml;
        using (var stream = worksheetEntry!.Open())
            worksheetXml = XDocument.Load(stream);
        XDocument worksheetRelsXml;
        using (var stream = worksheetRelsEntry!.Open())
            worksheetRelsXml = XDocument.Load(stream);
        XDocument drawingRelsXml;
        using (var stream = drawingRelsEntry!.Open())
            drawingRelsXml = XDocument.Load(stream);

        var drawingRelationshipId = worksheetXml.Root!
            .Element(sheetNs + "drawing")!
            .Attribute(relNs + "id")!
            .Value;
        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == drawingRelationshipId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" &&
                relationship.Attribute("Target")?.Value == "../drawings/drawing1.xml")
            .Should().ContainSingle();
        drawingRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData" &&
                relationship.Attribute("Target")?.Value == "../diagrams/data1.xml")
            .Should().ContainSingle();
        drawingRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout" &&
                relationship.Attribute("Target")?.Value == "../diagrams/layout1.xml")
            .Should().ContainSingle();
        drawingRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle" &&
                relationship.Attribute("Target")?.Value == "../diagrams/quickStyle1.xml")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedDataModelRetentionPackage_LinksWorkbookToModelPart()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-data-model-001");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        workbookRelsEntry.Should().NotBeNull();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        XDocument workbookRelsXml;
        using (var stream = workbookRelsEntry!.Open())
            workbookRelsXml = XDocument.Load(stream);

        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2011/relationships/model" &&
                relationship.Attribute("Target")?.Value == "model/item.data")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedLinkedDataTypesRetentionPackage_LinksRichValueDataToStructurePart()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-linked-data-types-001");
        var report = XlsxFeatureInspector.Inspect(package);
        report.Features.Select(feature => feature.Kind).Distinct().Should().BeEquivalentTo(
            new[] { XlsxUnsupportedFeatureKind.LinkedDataTypes });

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var richValueDataEntry = archive.GetEntry("xl/richData/rdrichvalue.xml");
        var richValueStructureEntry = archive.GetEntry("xl/richData/rdRichValueStructure.xml");
        var richValueDataRelsEntry = archive.GetEntry("xl/richData/_rels/rdrichvalue.xml.rels");
        richValueDataEntry.Should().NotBeNull();
        richValueStructureEntry.Should().NotBeNull();
        richValueDataRelsEntry.Should().NotBeNull();

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument richValueDataRelsXml;
        using (var stream = richValueDataRelsEntry!.Open())
            richValueDataRelsXml = XDocument.Load(stream);

        richValueDataRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueStructure" &&
                relationship.Attribute("Target")?.Value == "rdRichValueStructure.xml")
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedMetadataPassRows_RetainCriticalPackagePartsAfterModelEdit()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-metadata-pass")
            .ToArray();

        rows.Should().NotBeEmpty("metadata-pass rows cover supported native package features that should retain without warnings");
        rows.Should().HaveCount(52, "the generated metadata-pass manifest currently declares fifty-two deterministic package-retention rows");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreateKnownGapRetentionPackage(row.Id));

        var adapter = new XlsxFileAdapter();
        foreach (var row in rows)
        {
            using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(row.Id);
            var before = CapturePackageSummary(source);
            var fixtureParts = CaptureKnownGapFixtureParts(row.Id);
            before.CriticalParts.Should().Contain(fixtureParts, row.Id);
            var fixtureContentTypeOverrides = ContentTypeOverridesForParts(before, fixtureParts);

            source.Position = 0;
            XlsxFeatureInspector.Inspect(source).HasUnsupportedFeatures.Should().BeFalse(row.Id);

            source.Position = 0;
            var workbook = adapter.Load(source);
            var beforeMetadata = CaptureWorkbookMetadataSummary(workbook);
            var sheet = workbook.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 11, 1), new TextValue("freex-metadata-retention-edit"));

            using var saved = new MemoryStream();
            adapter.Save(workbook, saved);
            saved.Position = 0;
            AssertPackageHealth(saved, row.Id);
            var after = CapturePackageSummary(saved);

            after.CriticalParts.Should().Contain(before.CriticalParts, row.Id);
            after.CriticalRelationshipTargets.Should().Contain(before.CriticalRelationshipTargets, row.Id);
            after.CriticalRelationshipDetails.Should().Contain(before.CriticalRelationshipDetails, row.Id);
            after.CriticalContentTypeOverrides.Should().Contain(before.CriticalContentTypeOverrides, row.Id);
            if (fixtureContentTypeOverrides.Count > 0)
                after.CriticalContentTypeOverrides.Should().Contain(fixtureContentTypeOverrides, row.Id);

            saved.Position = 0;
            var roundTripped = adapter.Load(saved);
            CaptureWorkbookMetadataSummary(roundTripped).Should().BeEquivalentTo(
                beforeMetadata,
                options => options.WithStrictOrdering(),
                row.Id);
        }
    }

    [Fact]
    public void GeneratedCfRetentionPackage_RetainsSixteenCfRulesInXml()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-cf-retention-package-003");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        worksheetEntry.Should().NotBeNull("generated-cf-retention-package-003 must contain xl/worksheets/sheet1.xml");

        XDocument worksheetXml;
        using (var stream = worksheetEntry!.Open())
            worksheetXml = XDocument.Load(stream);

        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cfRules = worksheetXml.Descendants(sheetNs + "cfRule").ToArray();
        cfRules.Should().HaveCount(16, "generated-cf-retention-package-003 embeds sixteen cfRule elements across its conditionalFormatting blocks");
    }

    [Fact]
    public void GeneratedTableRefFormulaRow_MaterializesTableGraphAndFormulasAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-table-ref-formulas-package-003");
        AssertTableRefFormulaPackageGraph(source, "generated-table-ref-formulas-package-003 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-table-formula-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-table-ref-formulas-package-003");
        AssertTableRefFormulaPackageGraph(saved, "generated-table-ref-formulas-package-003 saved");
    }

    [Fact]
    public void GeneratedCrossSheetRangeRow_MaterializesCrossSheetFormulasAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-cross-sheet-range-package-003");
        AssertCrossSheetFormulaPackageGraph(source, "generated-cross-sheet-range-package-003 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-cross-sheet-formula-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-cross-sheet-range-package-003");
        AssertCrossSheetFormulaPackageGraph(saved, "generated-cross-sheet-range-package-003 saved");
    }

    [Fact]
    public void GeneratedNamedRangeCountRow_MaterializesTwelveDefinedNamesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-named-range-count-package-003");
        AssertNamedRangeCountPackageGraph(source, "generated-named-range-count-package-003 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-named-range-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-named-range-count-package-003");
        AssertNamedRangeCountPackageGraph(saved, "generated-named-range-count-package-003 saved");
    }


    [Theory]
    [InlineData("generated-slicers-001", "xl/drawings/drawing1.xml", "../slicers/slicer1.xml")]
    [InlineData("generated-timelines-001", "xl/drawings/drawing1.xml", "../timelines/timeline1.xml")]
    public void GeneratedSlicerTimelineRows_RetainFloatingDrawingAnchorsAfterModelEdit(
        string id,
        string drawingPart,
        string drawingRelationshipTarget)
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage(id);
        var before = CapturePackageSummary(source);
        before.CriticalParts.Should().Contain(drawingPart, id);
        before.CriticalRelationshipTargets.Should().Contain(target =>
            target.EndsWith($"=>{drawingRelationshipTarget}", StringComparison.OrdinalIgnoreCase), id);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-floating-anchor-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, id);
        var after = CapturePackageSummary(saved);

        after.CriticalParts.Should().Contain(drawingPart, id);
        after.CriticalRelationshipTargets.Should().Contain(target =>
            target.EndsWith($"=>{drawingRelationshipTarget}", StringComparison.OrdinalIgnoreCase), id);
    }

    [Fact]
    public void GeneratedPrinterSettingsRow_RetainsWorksheetPageSetupRelationshipAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-printer-settings-001");
        AssertPrinterSettingsReference(source, "generated-printer-settings-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-printer-settings-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-printer-settings-001");
        AssertPrinterSettingsReference(saved, "generated-printer-settings-001 saved");
    }

    [Fact]
    public void GeneratedCustomXmlRow_RetainsPackageRelationshipsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-custom-xml-001");
        AssertCustomXmlPackageGraph(source, "generated-custom-xml-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-custom-xml-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-custom-xml-001");
        AssertCustomXmlPackageGraph(saved, "generated-custom-xml-001 saved");
    }

    [Fact]
    public void GeneratedCustomDocPropsRow_RetainsCustomDocumentPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-custom-docprops-001");
        AssertCustomDocumentProperties(source, "generated-custom-docprops-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-custom-docprops-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-custom-docprops-001");
        AssertCustomDocumentProperties(saved, "generated-custom-docprops-001 saved");
    }

    [Fact]
    public void GeneratedCalcChainRow_RetainsCalcChainAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-calc-chain-001");
        AssertCalcChainReference(source, "generated-calc-chain-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-calc-chain-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-calc-chain-001");
        AssertCalcChainReference(saved, "generated-calc-chain-001 saved");
    }

    [Fact]
    public void GeneratedDocumentPropertiesRow_RetainsStableDocumentPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-document-properties-001");
        AssertStableDocumentProperties(source, "generated-document-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-document-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-document-properties-001");
        AssertStableDocumentProperties(saved, "generated-document-properties-001 saved");
    }

    [Fact]
    public void GeneratedHeaderFooterLegacyDrawingRow_RetainsPackageGraphAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-header-footer-legacy-drawing-001");
        AssertHeaderFooterLegacyDrawingPackageGraph(source, "generated-header-footer-legacy-drawing-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-header-footer-legacy-drawing-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-header-footer-legacy-drawing-001");
        AssertHeaderFooterLegacyDrawingPackageGraph(saved, "generated-header-footer-legacy-drawing-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetLegacyDrawingRow_RetainsPackageGraphAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-legacy-drawing-001");
        AssertWorksheetLegacyDrawingPackageGraph(source, "generated-worksheet-legacy-drawing-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-worksheet-legacy-drawing-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-legacy-drawing-001");
        AssertWorksheetLegacyDrawingPackageGraph(saved, "generated-worksheet-legacy-drawing-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookExtensionListRow_RetainsUnknownWorkbookExtensionsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-extension-list-001");
        AssertWorkbookExtensionList(source, "generated-workbook-extension-list-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-workbook-extension-list-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-extension-list-001");
        AssertWorkbookExtensionList(saved, "generated-workbook-extension-list-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookPropertiesRow_RetainsPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-properties-001");
        AssertWorkbookProperties(source, "generated-workbook-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-workbook-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-properties-001");
        AssertWorkbookProperties(saved, "generated-workbook-properties-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookCalculationRow_RetainsCalculationAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-calculation-001");
        AssertWorkbookCalculation(source, "generated-workbook-calculation-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-workbook-calculation-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-calculation-001");
        AssertWorkbookCalculation(saved, "generated-workbook-calculation-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookFileVersionRow_RetainsFileVersionAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-file-version-001");
        AssertWorkbookFileVersion(source, "generated-workbook-file-version-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-workbook-file-version-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-file-version-001");
        AssertWorkbookFileVersion(saved, "generated-workbook-file-version-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookFileRecoveryRow_RetainsFileRecoveryAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-file-recovery-001");
        AssertWorkbookFileRecovery(source, "generated-workbook-file-recovery-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-workbook-file-recovery-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-file-recovery-001");
        AssertWorkbookFileRecovery(saved, "generated-workbook-file-recovery-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookFileSharingRow_RetainsFileSharingAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-file-sharing-001");
        AssertWorkbookFileSharing(source, "generated-workbook-file-sharing-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 15, 1), new TextValue("freex-workbook-file-sharing-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-file-sharing-001");
        AssertWorkbookFileSharing(saved, "generated-workbook-file-sharing-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookProtectionNativeRow_RetainsProtectionAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-protection-native-001");
        AssertWorkbookProtectionNative(source, "generated-workbook-protection-native-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.IsStructureProtected.Should().BeTrue();
        workbook.StructureProtectionPassword.Should().Be("83AF");
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-workbook-protection-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-protection-native-001");
        AssertWorkbookProtectionNative(saved, "generated-workbook-protection-native-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookSmartTagsRow_RetainsSmartTagsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-smart-tags-001");
        AssertWorkbookSmartTags(source, "generated-workbook-smart-tags-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-workbook-smart-tags-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-smart-tags-001");
        AssertWorkbookSmartTags(saved, "generated-workbook-smart-tags-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookFunctionGroupsRow_RetainsFunctionGroupsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-function-groups-001");
        AssertWorkbookFunctionGroups(source, "generated-workbook-function-groups-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 13, 1), new TextValue("freex-workbook-function-groups-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-function-groups-001");
        AssertWorkbookFunctionGroups(saved, "generated-workbook-function-groups-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookViewsRow_RetainsViewsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-views-001");
        AssertWorkbookViews(source, "generated-workbook-views-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 14, 1), new TextValue("freex-workbook-views-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-views-001");
        AssertWorkbookViews(saved, "generated-workbook-views-001 saved", expectCustomViews: false);
    }

    [Fact]
    public void GeneratedWorkbookDefinedNamesNativeRow_RetainsDefinedNamesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-defined-names-native-001");
        AssertWorkbookDefinedNamesNative(source, "generated-workbook-defined-names-native-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-defined-name-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-defined-names-native-001");
        AssertWorkbookDefinedNamesNative(saved, "generated-workbook-defined-names-native-001 saved");
    }

    [Fact]
    public void GeneratedStylesheetNativeMetadataRow_RetainsStylesheetMetadataAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-stylesheet-native-metadata-001");
        AssertStylesheetNativeMetadata(source, "generated-stylesheet-native-metadata-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.IndexedColors.TryGetColor(1, out var color).Should().BeTrue();
        color.Should().Be(CellColor.FromArgb(1, 2, 3));
        workbook.PivotTableStyles.Should().ContainSingle(style =>
            style.Name == "FreeXNativePivotStyle" &&
            style.AppliesToPivotTables &&
            !style.AppliesToTables &&
            style.Elements.Any(element =>
                element.Type == "wholeTable" &&
                element.DifferentialFormatId == 0));
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-stylesheet-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-stylesheet-native-metadata-001");
        AssertStylesheetNativeMetadata(saved, "generated-stylesheet-native-metadata-001 saved");
    }

    [Fact]
    public void GeneratedWorkbookThemeNativeSchemesRow_RetainsThemeSchemeDetailsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-workbook-theme-native-schemes-001");
        AssertWorkbookThemeNativeSchemes(source, "generated-workbook-theme-native-schemes-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.Theme.Name.Should().Be("FreeX Native Scheme Theme");
        workbook.Theme.MajorFontName.Should().Be("Major Native");
        workbook.Theme.MinorFontName.Should().Be("Minor Native");
        workbook.Theme.NativeColorSchemeXml.Should().Contain("lumMod");
        workbook.Theme.NativeFontSchemeXml.Should().Contain("typeface=\"Major East Asia\"");
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-theme-scheme-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-workbook-theme-native-schemes-001");
        AssertWorkbookThemeNativeSchemes(saved, "generated-workbook-theme-native-schemes-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetIgnoredErrorsRow_RetainsIgnoredErrorsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-ignored-errors-001");
        AssertWorksheetIgnoredErrors(source, "generated-worksheet-ignored-errors-001 source");
        var before = CaptureWorksheetIgnoredErrorsPackageSummary(source);
        before.Errors.Should().BeEquivalentTo(
            [
                new WorksheetIgnoredErrorXmlSummary(
                    "A1",
                    true,
                    [new NativeAttributeSummary("twoDigitTextYear", "1")])
            ],
            options => options.WithStrictOrdering(),
            "generated-worksheet-ignored-errors-001 should exercise modeled ignored-error state plus a retained native flag");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-ignored-errors-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-ignored-errors-001");
        AssertWorksheetIgnoredErrors(saved, "generated-worksheet-ignored-errors-001 saved");
        var after = CaptureWorksheetIgnoredErrorsPackageSummary(saved);
        after.Should().BeEquivalentTo(
            before,
            options => options.WithStrictOrdering(),
            "worksheet ignored-error sqref, modeled ignore state, and retained native flags should survive ordinary model edits");
    }

    [Fact]
    public void GeneratedWorksheetCellWatchesRow_RetainsCellWatchesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-cell-watches-001");
        AssertWorksheetCellWatches(source, "generated-worksheet-cell-watches-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-cell-watches-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-cell-watches-001");
        AssertWorksheetCellWatches(saved, "generated-worksheet-cell-watches-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetSingleXmlCellsRow_RetainsSingleXmlCellsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-single-xml-cells-001");
        AssertWorksheetSingleXmlCells(source, "generated-worksheet-single-xml-cells-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-single-xml-cells-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-single-xml-cells-001");
        AssertWorksheetSingleXmlCells(saved, "generated-worksheet-single-xml-cells-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetCalculationPropertiesRow_RetainsCalculationPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-calculation-properties-001");
        AssertWorksheetCalculationProperties(source, "generated-worksheet-calculation-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).FullCalculationOnLoad.Should().BeTrue();
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-worksheet-calculation-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-calculation-properties-001");
        AssertWorksheetCalculationProperties(saved, "generated-worksheet-calculation-properties-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetSheetViewsRow_RetainsSheetViewsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-sheet-views-001");
        AssertWorksheetSheetViews(source, "generated-worksheet-sheet-views-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-sheet-views-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-sheet-views-001");
        AssertWorksheetSheetViews(saved, "generated-worksheet-sheet-views-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetSheetFormatRow_RetainsSheetFormatAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-sheet-format-001");
        AssertWorksheetSheetFormat(source, "generated-worksheet-sheet-format-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-sheet-format-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-sheet-format-001");
        AssertWorksheetSheetFormat(saved, "generated-worksheet-sheet-format-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetPageBreaksRow_RetainsPageBreaksAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-page-breaks-001");
        AssertWorksheetPageBreaks(source, "generated-worksheet-page-breaks-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-page-breaks-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-page-breaks-001");
        AssertWorksheetPageBreaks(saved, "generated-worksheet-page-breaks-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetPrintOptionsRow_RetainsPrintOptionsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-print-options-001");
        AssertWorksheetPrintOptions(source, "generated-worksheet-print-options-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-print-options-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-print-options-001");
        AssertWorksheetPrintOptions(saved, "generated-worksheet-print-options-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetPageSetupNativeRow_RetainsPageSetupAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-page-setup-native-001");
        AssertWorksheetPageSetupNative(source, "generated-worksheet-page-setup-native-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-page-setup-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-page-setup-native-001");
        AssertWorksheetPageSetupNative(saved, "generated-worksheet-page-setup-native-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetHeaderFooterNativeRow_RetainsHeaderFooterAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-header-footer-native-001");
        AssertWorksheetHeaderFooterNative(source, "generated-worksheet-header-footer-native-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-header-footer-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-header-footer-native-001");
        AssertWorksheetHeaderFooterNative(saved, "generated-worksheet-header-footer-native-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetDimensionNativeRow_RetainsDimensionAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-dimension-native-001");
        AssertWorksheetDimensionNative(source, "generated-worksheet-dimension-native-001 source", "A1");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-dimension-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-dimension-native-001");
        AssertWorksheetDimensionNative(saved, "generated-worksheet-dimension-native-001 saved", "A1:B12");
    }

    [Fact]
    public void GeneratedWorksheetSheetPropertiesRow_RetainsSheetPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-sheet-properties-001");
        AssertWorksheetSheetProperties(source, "generated-worksheet-sheet-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-sheet-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-sheet-properties-001");
        AssertWorksheetSheetProperties(saved, "generated-worksheet-sheet-properties-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetProtectionNativeRow_RetainsProtectionAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-protection-native-001");
        AssertWorksheetProtectionNative(source, "generated-worksheet-protection-native-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-protection-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-protection-native-001");
        AssertWorksheetProtectionNative(saved, "generated-worksheet-protection-native-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetProtectedRangesRow_RetainsProtectedRangesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-protected-ranges-001");
        AssertWorksheetProtectedRanges(source, "generated-worksheet-protected-ranges-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var allowEditRange = workbook.GetSheetAt(0).AllowEditRanges.Should().ContainSingle().Subject;
        allowEditRange.Start.ToA1().Should().Be("B2");
        allowEditRange.End.ToA1().Should().Be("C3");
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-protected-ranges-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-protected-ranges-001");
        AssertWorksheetProtectedRanges(saved, "generated-worksheet-protected-ranges-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetCellStructureNativeRow_RetainsNativeMetadataAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-cell-structure-native-001");
        AssertWorksheetCellStructureNative(source, "generated-worksheet-cell-structure-native-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-cell-structure-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-cell-structure-native-001");
        AssertWorksheetCellStructureNative(saved, "generated-worksheet-cell-structure-native-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetPhoneticPropertiesRow_RetainsPhoneticPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-phonetic-properties-001");
        AssertWorksheetPhoneticProperties(source, "generated-worksheet-phonetic-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-phonetic-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-phonetic-properties-001");
        AssertWorksheetPhoneticProperties(saved, "generated-worksheet-phonetic-properties-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetSortStateRow_RetainsSortStateAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-sort-state-001");
        AssertWorksheetSortState(source, "generated-worksheet-sort-state-001 source");
        var before = CaptureWorksheetSortFilterPackageSummary(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-sort-state-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-sort-state-001");
        AssertWorksheetSortState(saved, "generated-worksheet-sort-state-001 saved");
        var after = CaptureWorksheetSortFilterPackageSummary(saved);
        after.Should().BeEquivalentTo(
            before,
            options => options.WithStrictOrdering(),
            "worksheet AutoFilter and sortState XML semantics should survive ordinary model edits");
    }

    [Fact]
    public void GeneratedWorksheetDataConsolidationRow_RetainsDataConsolidationAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-data-consolidation-001");
        AssertWorksheetDataConsolidation(source, "generated-worksheet-data-consolidation-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-data-consolidation-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-data-consolidation-001");
        AssertWorksheetDataConsolidation(saved, "generated-worksheet-data-consolidation-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetAutoFilterMetadataRow_RetainsAutoFilterAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-auto-filter-metadata-001");
        AssertWorksheetAutoFilterMetadata(source, "generated-worksheet-auto-filter-metadata-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        var loadedAutoFilter = workbook.GetSheetAt(0).AutoFilter;
        loadedAutoFilter.Should().NotBeNull();
        loadedAutoFilter!.Reference.Should().Be("A1:B3");
        var loadedFilterColumn = loadedAutoFilter.FilterColumns.Should().ContainSingle().Subject;
        loadedFilterColumn.ColumnId.Should().Be(0);
        loadedFilterColumn.Values.Should().Equal("A");
        loadedFilterColumn.IncludeBlank.Should().BeTrue();
        workbook.GetSheetAt(0).FilterHiddenRows.Should().Contain(3u);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-auto-filter-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-auto-filter-metadata-001");
        AssertWorksheetAutoFilterMetadata(saved, "generated-worksheet-auto-filter-metadata-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetCustomPropertiesRow_RetainsCustomPropertiesAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-custom-properties-001");
        AssertWorksheetCustomProperties(source, "generated-worksheet-custom-properties-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-worksheet-custom-properties-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-custom-properties-001");
        AssertWorksheetCustomProperties(saved, "generated-worksheet-custom-properties-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetSmartTagsRow_RetainsSmartTagsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-smart-tags-001");
        AssertWorksheetSmartTags(source, "generated-worksheet-smart-tags-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-worksheet-smart-tags-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-smart-tags-001");
        AssertWorksheetSmartTags(saved, "generated-worksheet-smart-tags-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetScenariosRow_RetainsScenariosAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-scenarios-001");
        AssertWorksheetScenarios(source, "generated-worksheet-scenarios-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-worksheet-scenarios-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-scenarios-001");
        AssertWorksheetScenarios(saved, "generated-worksheet-scenarios-001 saved");
    }

    [Fact]
    public void GeneratedWorksheetCustomSheetViewsRow_RetainsCustomSheetViewsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-custom-sheet-views-001");
        AssertWorksheetCustomSheetViews(source, "generated-worksheet-custom-sheet-views-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-custom-sheet-views-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-custom-sheet-views-001");
        AssertWorksheetCustomSheetViews(saved, "generated-worksheet-custom-sheet-views-001 saved", expectCustomSheetViews: false);
    }

    [Fact]
    public void GeneratedWorksheetExtensionListRow_RetainsSparklineAndUnknownExtensionsAfterModelEdit()
    {
        using var source = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-worksheet-extension-list-001");
        AssertWorksheetExtensionList(source, "generated-worksheet-extension-list-001 source");

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.GetSheetAt(0).Sparklines.Should().ContainSingle();
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-worksheet-extlst-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-worksheet-extension-list-001");
        AssertWorksheetExtensionList(saved, "generated-worksheet-extension-list-001 saved");
    }

    private static void AssertWorksheetExtensionList(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var extensionList = worksheetXml.Root!.Element(worksheetNs + "extLst");
        extensionList.Should().NotBeNull(because);
        extensionList!.Elements(worksheetNs + "ext")
            .Where(extension => string.Equals(extension.Attribute("uri")?.Value, "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}", StringComparison.Ordinal))
            .Should()
            .ContainSingle(because);
        extensionList.Descendants(x14Ns + "sparklineGroups").Should().ContainSingle(because);

        var unknownExtension = extensionList.Elements(worksheetNs + "ext")
            .Where(extension => string.Equals(extension.Attribute("uri")?.Value, "{FFEEDDCC-BBAA-9988-7766-554433221100}", StringComparison.Ordinal))
            .Should()
            .ContainSingle(because)
            .Subject;
        unknownExtension.Descendants(x15Ns + "futureMetadata")
            .Where(metadata => string.Equals(metadata.Attribute("name")?.Value, "FreeXUnknownWorksheetExtension", StringComparison.Ordinal))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertWorksheetCustomSheetViews(Stream package, string because, bool expectCustomSheetViews = true)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var customSheetViews = worksheetXml.Root!.Element(worksheetNs + "customSheetViews");
        if (!expectCustomSheetViews)
        {
            customSheetViews.Should().BeNull(because);
            return;
        }

        var customSheetView = customSheetViews!
            .Elements(worksheetNs + "customSheetView")
            .Should()
            .ContainSingle(because)
            .Subject;
        customSheetView.Attribute("guid")!.Value.Should().Be("{11111111-1111-1111-1111-111111111111}", because);
        customSheetView.Attribute("scale")!.Value.Should().Be("120", because);
        customSheetView.Attribute("showGridLines")!.Value.Should().Be("0", because);
        customSheetView.Attribute("showRowCol")!.Value.Should().Be("0", because);
        customSheetView.Attribute("state")!.Value.Should().Be("visible", because);

        var pane = customSheetView.Element(worksheetNs + "pane");
        pane.Should().NotBeNull(because);
        pane!.Attribute("topLeftCell")!.Value.Should().Be("B2", because);
        pane.Attribute("activePane")!.Value.Should().Be("bottomRight", because);
    }

    private static void AssertWorksheetScenarios(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var scenarios = worksheetXml.Root!.Element(worksheetNs + "scenarios");
        scenarios.Should().NotBeNull(because);

        var scenario = scenarios!.Elements(worksheetNs + "scenario")
            .Should()
            .ContainSingle(because)
            .Subject;
        scenario.Attribute("name")!.Value.Should().Be("BestCase", because);
        scenario.Attribute("comment")!.Value.Should().Be("Scenario comment", because);
        scenario.Attribute("hidden")!.Value.Should().Be("1", because);
        scenario.Attribute("locked")!.Value.Should().Be("1", because);
        scenario.Attribute("user")!.Value.Should().Be("FreeXTest", because);

        var inputCells = scenario.Elements(worksheetNs + "inputCells")
            .Should()
            .ContainSingle(because)
            .Subject;
        inputCells.Attribute("r")!.Value.Should().Be("A1", because);
        inputCells.Attribute("val")!.Value.Should().Be("42", because);
    }

    private static void AssertWorksheetSmartTags(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var cellSmartTags = worksheetXml.Root!
            .Element(worksheetNs + "smartTags")!
            .Elements(worksheetNs + "cellSmartTags")
            .Should()
            .ContainSingle(because)
            .Subject;
        cellSmartTags.Attribute("r")!.Value.Should().Be("A1", because);

        var smartTag = cellSmartTags.Elements(worksheetNs + "cellSmartTag")
            .Should()
            .ContainSingle(because)
            .Subject;
        smartTag.Attribute("type")!.Value.Should().Be("0", because);
        smartTag.Attribute("deleted")!.Value.Should().Be("0", because);

        var property = smartTag.Elements(worksheetNs + "cellSmartTagPr")
            .Should()
            .ContainSingle(because)
            .Subject;
        property.Attribute("key")!.Value.Should().Be("place", because);
        property.Attribute("val")!.Value.Should().Be("Seattle", because);
        property.Attribute("customSmartTagPropertyFlag")!.Value.Should().Be("keep", because);
    }

    private static void AssertWorksheetCustomProperties(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var customProperty = worksheetXml.Root!
            .Element(worksheetNs + "customProperties")!
            .Elements(worksheetNs + "customPr")
            .Should()
            .ContainSingle(because)
            .Subject;

        customProperty.Attribute("name")!.Value.Should().Be("FreeXNativeProperty", because);
        if (customProperty.Attribute("id") is { } legacyId)
        {
            legacyId.Value.Should().Be("1", because);
        }
        else
        {
            var relationshipId = customProperty.Attribute(relNs + "id")!.Value;
            AssertWorksheetCustomPropertyRelationship(archive, relationshipId, "sheet1-1-FreeXNativeProperty.bin", because);
        }
        customProperty.Attribute("unsupportedAttr").Should().BeNull(because);
    }

    private static void AssertWorksheetCustomPropertyRelationship(
        ZipArchive archive,
        string relationshipId,
        string expectedTargetFileName,
        string because)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relsEntry = archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        relsEntry.Should().NotBeNull(because);

        var relsXml = LoadPackageXml(relsEntry!);
        var relationship = relsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .SingleOrDefault(element => element.Attribute("Id")?.Value == relationshipId);
        relationship.Should().NotBeNull(because);
        relationship!.Attribute("Type")!.Value.Should()
            .Be("http://schemas.openxmlformats.org/officeDocument/2006/relationships/customProperty", because);
        relationship.Attribute("Target")!.Value.Should().Be("../customProperty/" + expectedTargetFileName, because);
        archive.GetEntry("xl/customProperty/" + expectedTargetFileName).Should().NotBeNull(because);
    }

    private static void AssertWorksheetDataConsolidation(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var dataConsolidate = worksheetXml.Root!.Element(worksheetNs + "dataConsolidate");
        dataConsolidate.Should().NotBeNull(because);
        dataConsolidate!.Attribute("function")!.Value.Should().Be("sum", because);
        dataConsolidate.Attribute("leftLabels")!.Value.Should().Be("1", because);
        dataConsolidate.Attribute("topLabels")!.Value.Should().Be("1", because);
        dataConsolidate.Attribute("link")!.Value.Should().Be("1", because);
        dataConsolidate.Attribute("customDataConsolidationFlag").Should().BeNull(because);

        var dataRef = dataConsolidate
            .Element(worksheetNs + "dataRefs")!
            .Elements(worksheetNs + "dataRef")
            .Should()
            .ContainSingle(because)
            .Subject;
        dataRef.Attribute("ref")!.Value.Should().Be("A1:B2", because);
        dataRef.Attribute("sheet")!.Value.Should().Be("Data", because);
        dataRef.Attribute("customDataRefFlag").Should().BeNull(because);
    }

    private static void AssertWorksheetAutoFilterMetadata(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var autoFilter = worksheetXml.Root!.Element(worksheetNs + "autoFilter");
        autoFilter.Should().NotBeNull(because);
        autoFilter!.Attribute("ref")!.Value.Should().Be("A1:B3", because);
        var filterColumn = autoFilter.Elements(worksheetNs + "filterColumn")
            .Should()
            .ContainSingle(because)
            .Subject;
        filterColumn.Attribute("colId")!.Value.Should().Be("0", because);
        var filters = filterColumn.Element(worksheetNs + "filters");
        filters.Should().NotBeNull(because);
        filters!.Attribute("blank")!.Value.Should().Be("1", because);
        filters.Elements(worksheetNs + "filter")
            .Where(filter => string.Equals(filter.Attribute("val")?.Value, "A", StringComparison.Ordinal))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertWorksheetSortState(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var autoFilter = worksheetXml.Root!.Element(worksheetNs + "autoFilter");
        autoFilter.Should().NotBeNull(because);
        autoFilter!.Attribute("ref")!.Value.Should().Be("A1:B3", because);
        autoFilter.Descendants(worksheetNs + "filter")
            .Single(filter => string.Equals(filter.Attribute("val")?.Value, "A", StringComparison.Ordinal))
            .Should()
            .NotBeNull(because);

        var sortState = worksheetXml.Root.Element(worksheetNs + "sortState");
        sortState.Should().NotBeNull(because);
        sortState!.Attribute("ref")!.Value.Should().Be("A1:A3", because);
        sortState.Attribute("caseSensitive")!.Value.Should().Be("1", because);
        sortState.Attribute("sortMethod")!.Value.Should().Be("stroke", because);
        sortState.Attribute("customSortStateFlag").Should().BeNull(because);

        var sortCondition = sortState.Elements(worksheetNs + "sortCondition")
            .Should()
            .ContainSingle(because)
            .Subject;
        sortCondition.Attribute("ref")!.Value.Should().Be("A2:A3", because);
        sortCondition.Attribute("descending")!.Value.Should().Be("1", because);
        sortCondition.Attribute("sortBy")!.Value.Should().Be("cellColor", because);
        sortCondition.Attribute("customSortConditionFlag").Should().BeNull(because);
    }

    private static void AssertWorksheetPhoneticProperties(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var phoneticProperties = worksheetXml.Root!.Element(worksheetNs + "phoneticPr");
        phoneticProperties.Should().NotBeNull(because);
        phoneticProperties!.Attribute("fontId")!.Value.Should().Be("1", because);
        phoneticProperties.Attribute("type")!.Value.Should().Be("fullwidthKatakana", because);
        phoneticProperties.Attribute("alignment")!.Value.Should().Be("center", because);
        phoneticProperties.Attribute("nativeOnly").Should().BeNull(because);
    }

    private static void AssertWorksheetCellWatches(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var cellWatches = worksheetXml.Root!.Element(worksheetNs + "cellWatches");
        cellWatches.Should().NotBeNull(because);
        cellWatches!.Attribute("nativeContainer").Should().BeNull(because);

        var cellWatch = cellWatches.Elements(worksheetNs + "cellWatch")
            .Should()
            .ContainSingle(because)
            .Subject;
        cellWatch.Attribute("r")!.Value.Should().Be("A1", because);
        cellWatch.Attribute("nativeWatch").Should().BeNull(because);
    }

    private static void AssertWorksheetSingleXmlCells(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var singleXmlCells = worksheetXml.Root!.Element(worksheetNs + "singleXmlCells");
        singleXmlCells.Should().NotBeNull(because);
        singleXmlCells!.Attribute("nativeSingleXmlCellsAttr")!.Value.Should().Be("kept", because);

        var singleXmlCell = singleXmlCells.Elements(worksheetNs + "singleXmlCell")
            .Should()
            .ContainSingle(because)
            .Subject;
        singleXmlCell.Attribute("id")!.Value.Should().Be("1", because);
        singleXmlCell.Attribute("r")!.Value.Should().Be("A1", because);
        singleXmlCell.Attribute("xmlCellPrId")!.Value.Should().Be("1", because);
        singleXmlCell.Attribute("nativeSingleXmlCellAttr")!.Value.Should().Be("cell-kept", because);
    }

    private static void AssertWorksheetCalculationProperties(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var sheetCalcPr = worksheetXml.Root!.Element(worksheetNs + "sheetCalcPr");
        sheetCalcPr.Should().NotBeNull(because);
        sheetCalcPr!.Attribute("fullCalcOnLoad")!.Value.Should().Be("1", because);
        sheetCalcPr.Attribute("calcId").Should().BeNull(because);
    }

    private static void AssertWorksheetSheetViews(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var sheetViews = worksheetXml.Root!.Element(worksheetNs + "sheetViews");
        sheetViews.Should().NotBeNull(because);
        sheetViews!.Attribute("nativeSheetViewsAttr").Should().BeNull(because);
        var sheetView = sheetViews.Elements(worksheetNs + "sheetView")
            .Should()
            .ContainSingle(because)
            .Subject;
        sheetView.Attribute("workbookViewId")!.Value.Should().Be("0", because);
        sheetView.Attribute("showZeros")!.Value.Should().Be("0", because);
        sheetView.Attribute("rightToLeft")!.Value.Should().Be("1", because);
        sheetView.Element(worksheetNs + "pivotSelection").Should().BeNull(because);
    }

    private static void AssertWorksheetSheetFormat(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var sheetFormat = worksheetXml.Root!.Element(worksheetNs + "sheetFormatPr");
        sheetFormat.Should().NotBeNull(because);
        sheetFormat!.Attribute("baseColWidth")!.Value.Should().Be("12", because);
        sheetFormat.Attribute("zeroHeight")!.Value.Should().Be("1", because);
        sheetFormat.Attribute("thickTop")!.Value.Should().Be("1", because);
        sheetFormat.Attribute("outlineLevelRow")!.Value.Should().Be("3", because);
        sheetFormat.HasElements.Should().BeFalse(because);
    }

    private static void AssertWorksheetPageBreaks(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var rowBreaks = worksheetXml.Root!.Element(worksheetNs + "rowBreaks");
        rowBreaks.Should().NotBeNull(because);
        rowBreaks!.Attribute("count")!.Value.Should().Be("1", because);
        rowBreaks.Attribute("manualBreakCount")!.Value.Should().Be("1", because);
        var rowBreak = rowBreaks.Elements(worksheetNs + "brk")
            .Should()
            .ContainSingle(because)
            .Subject;
        rowBreak.Attribute("id")!.Value.Should().Be("20", because);
        rowBreak.Attribute("max")!.Value.Should().Be("16383", because);
        rowBreak.Attribute("man")!.Value.Should().Be("1", because);
        rowBreak.Attribute("pt")!.Value.Should().Be("1", because);
        rowBreak.Attribute("customAttr").Should().BeNull(because);

        var columnBreaks = worksheetXml.Root.Element(worksheetNs + "colBreaks");
        columnBreaks.Should().NotBeNull(because);
        columnBreaks!.Attribute("count")!.Value.Should().Be("1", because);
        columnBreaks.Attribute("manualBreakCount")!.Value.Should().Be("1", because);
        var columnBreak = columnBreaks.Elements(worksheetNs + "brk")
            .Should()
            .ContainSingle(because)
            .Subject;
        columnBreak.Attribute("id")!.Value.Should().Be("5", because);
        columnBreak.Attribute("max")!.Value.Should().Be("1048575", because);
        columnBreak.Attribute("man")!.Value.Should().Be("1", because);
        columnBreak.Attribute("pt")!.Value.Should().Be("1", because);
        columnBreak.Attribute("customAttr").Should().BeNull(because);
    }

    private static void AssertWorksheetPrintOptions(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var printOptions = worksheetXml.Root!.Element(worksheetNs + "printOptions");
        printOptions.Should().NotBeNull(because);
        printOptions!.Attribute("gridLinesSet")!.Value.Should().Be("1", because);
        printOptions.Attribute("customAttr").Should().BeNull(because);
        printOptions.Attribute("gridLines")?.Value.Should().NotBe("1", because);
        printOptions.Attribute("headings")?.Value.Should().NotBe("1", because);
        printOptions.Attribute("horizontalCentered")?.Value.Should().NotBe("1", because);
        printOptions.Attribute("verticalCentered")?.Value.Should().NotBe("1", because);
        printOptions.HasElements.Should().BeFalse(because);
    }

    private static void AssertWorksheetPageSetupNative(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var pageSetup = worksheetXml.Root!.Element(worksheetNs + "pageSetup");
        pageSetup.Should().NotBeNull(because);
        pageSetup!.Attribute("usePrinterDefaults")!.Value.Should().Be("1", because);
        pageSetup.Attribute("copies")!.Value.Should().Be("3", because);
        pageSetup.Attribute("customAttr").Should().BeNull(because);
        pageSetup.HasElements.Should().BeFalse(because);
    }

    private static void AssertWorksheetHeaderFooterNative(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var headerFooter = worksheetXml.Root!.Element(worksheetNs + "headerFooter");
        headerFooter.Should().NotBeNull(because);
        headerFooter!.Attribute("nativeHeaderFooterAttr").Should().BeNull(because);
        headerFooter.Element(worksheetNs + "oddHeader")!.Value.Should().Contain("Center", because);
        headerFooter.Element(worksheetNs + "nativeHeaderFooterChild").Should().BeNull(because);
    }

    private static void AssertWorksheetDimensionNative(Stream package, string because, string expectedRef)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var dimension = worksheetXml.Root!.Element(worksheetNs + "dimension");
        dimension.Should().NotBeNull(because);
        dimension!.Attribute("ref")!.Value.Should().Be(expectedRef, because);
        dimension.Attribute("nativeDimensionAttr").Should().BeNull(because);
    }

    private static void AssertWorksheetSheetProperties(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var sheetPr = worksheetXml.Root!.Element(worksheetNs + "sheetPr");
        sheetPr.Should().NotBeNull(because);
        sheetPr!.Attribute("filterMode")!.Value.Should().Be("1", because);
        var pageSetUpPr = sheetPr.Element(worksheetNs + "pageSetUpPr");
        pageSetUpPr.Should().NotBeNull(because);
        pageSetUpPr!.Attribute("fitToPage")!.Value.Should().Be("1", because);
        pageSetUpPr.Attribute("autoPageBreaks")!.Value.Should().Be("0", because);
        sheetPr.Elements().Where(element => element.Name.NamespaceName == "urn:freex:test")
            .Should()
            .BeEmpty(because);
    }

    private static void AssertWorksheetProtectionNative(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var protection = worksheetXml.Root!.Element(worksheetNs + "sheetProtection");
        protection.Should().NotBeNull(because);
        protection!.Attribute("sheet")!.Value.Should().Be("1", because);
        protection.Attribute("algorithmName")!.Value.Should().Be("SHA-512", because);
        protection.Attribute("hashValue")!.Value.Should().Be("AQIDBA==", because);
        protection.Attribute("saltValue")!.Value.Should().Be("BQYHCA==", because);
        protection.Attribute("spinCount")!.Value.Should().Be("100000", because);
        protection.Attribute("objects")!.Value.Should().Be("1", because);
        protection.Attribute("scenarios")!.Value.Should().Be("1", because);
        protection.HasElements.Should().BeFalse(because);
    }

    private static void AssertWorksheetProtectedRanges(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var protectedRanges = worksheetXml.Root!.Element(worksheetNs + "protectedRanges");
        protectedRanges.Should().NotBeNull(because);
        var ranges = protectedRanges!.Elements(worksheetNs + "protectedRange").ToArray();
        ranges.Should().HaveCount(2, because);

        var editableRange = ranges.Should()
            .ContainSingle(element => (string?)element.Attribute("name") == "NativeEditableRange", because)
            .Subject;
        editableRange.Attribute("sqref")!.Value.Should().Be("B2:C3", because);
        editableRange.Attribute("password")!.Value.Should().Be("ABCD", because);
        editableRange.Attribute("securityDescriptor")!.Value.Should().Be("D:PAI", because);
        editableRange.HasElements.Should().BeFalse(because);

        var nativeOnlyRange = ranges.Should()
            .ContainSingle(element => (string?)element.Attribute("name") == "NativeMultiAreaRange", because)
            .Subject;
        nativeOnlyRange.Attribute("sqref")!.Value.Should().Be("B2 C3", because);
        nativeOnlyRange.Attribute("password")!.Value.Should().Be("1234", because);
    }

    private static void AssertWorksheetCellStructureNative(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var cols = worksheetXml.Root!.Element(worksheetNs + "cols");
        cols.Should().NotBeNull(because);
        cols!.Attribute("nativeColsAttr").Should().BeNull(because);
        var column = cols.Elements(worksheetNs + "col")
            .Where(element => (string?)element.Attribute("min") == "2" && (string?)element.Attribute("max") == "2")
            .Should()
            .ContainSingle(because)
            .Subject;
        column.Attribute("bestFit")!.Value.Should().Be("1", because);
        column.Attribute("phonetic")!.Value.Should().Be("1", because);
        column.Attribute("customAttr").Should().BeNull(because);

        var sheetData = worksheetXml.Root.Element(worksheetNs + "sheetData");
        sheetData.Should().NotBeNull(because);
        sheetData!.Attribute("nativeSheetDataAttr").Should().BeNull(because);
        var row = sheetData.Elements(worksheetNs + "row")
            .Where(element => (string?)element.Attribute("r") == "2")
            .Should()
            .ContainSingle(because)
            .Subject;
        row.Attribute("thickTop")!.Value.Should().Be("1", because);
        row.Attribute("ph")!.Value.Should().Be("1", because);
        row.Attribute("customAttr").Should().BeNull(because);
        row.Element(freexNs + "rowNativeChild").Should().BeNull(because);
        row.Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")!
            .Element(freexNs + "rowExt")!
            .Attribute("value")!.Value.Should().Be("row-extension", because);

        var cell = row.Elements(worksheetNs + "c")
            .Where(element => (string?)element.Attribute("r") == "A2")
            .Should()
            .ContainSingle(because)
            .Subject;
        cell.Attribute("cm").Should().BeNull(because);
        cell.Attribute("vm").Should().BeNull(because);
        cell.Attribute("ph")!.Value.Should().Be("1", because);
        cell.Attribute("customAttr").Should().BeNull(because);
        cell.Element(freexNs + "cellNativeChild").Should().BeNull(because);
        cell.Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")!
            .Element(freexNs + "cellExt")!
            .Attribute("value")!.Value.Should().Be("cell-extension", because);
        var formula = cell.Element(worksheetNs + "f");
        formula.Should().NotBeNull(because);
        formula!.Attribute("t")!.Value.Should().Be("array", because);
        formula.Attribute("ref")!.Value.Should().Be("A2:A2", because);
        formula.Attribute("ca")!.Value.Should().Be("1", because);
        formula.Attribute("customAttr").Should().BeNull(because);

        var mergeCells = worksheetXml.Root.Element(worksheetNs + "mergeCells");
        mergeCells.Should().NotBeNull(because);
        mergeCells!.Attribute("nativeMergeContainerAttr").Should().BeNull(because);
        var mergeCell = mergeCells.Elements(worksheetNs + "mergeCell")
            .Where(element => (string?)element.Attribute("ref") == "A4:B5")
            .Should()
            .ContainSingle(because)
            .Subject;
        mergeCell.Attribute("nativeMergeCellAttr").Should().BeNull(because);
    }

    private static void AssertWorksheetIgnoredErrors(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var ignoredError = worksheetXml.Root!
            .Element(worksheetNs + "ignoredErrors")!
            .Elements(worksheetNs + "ignoredError")
            .Should()
            .ContainSingle(because)
            .Subject;

        ignoredError.Attribute("sqref")!.Value.Should().Be("A1", because);
        ignoredError.Attribute("numberStoredAsText")!.Value.Should().Be("1", because);
        ignoredError.Attribute("twoDigitTextYear")!.Value.Should().Be("1", because);
    }

    private static void AssertWorkbookExtensionList(Stream package, string because)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("{00112233-4455-6677-8899-AABBCCDDEEFF}", because)
            .And.Contain("FreeXUnknownWorkbookExtension", because);
    }

    private static void AssertWorkbookProperties(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var workbookProperties = workbookXml.Root!.Element(workbookNs + "workbookPr");
        workbookProperties.Should().NotBeNull(because);
        workbookProperties!.Attribute("date1904")!.Value.Should().Be("1", because);
        workbookProperties.Attribute("defaultThemeVersion")!.Value.Should().Be("166925", because);
        workbookProperties.HasElements.Should().BeFalse(because);
    }

    private static void AssertWorkbookCalculation(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var calculationProperties = workbookXml.Root!.Element(workbookNs + "calcPr");
        calculationProperties.Should().NotBeNull(because);
        calculationProperties!.Attribute("calcMode")!.Value.Should().Be("manual", because);
        calculationProperties.Attribute("iterate")!.Value.Should().Be("1", because);
        calculationProperties.Attribute("iterateCount")!.Value.Should().Be("50", because);
        calculationProperties.Attribute("calcId")!.Value.Should().Be("191029", because);
        calculationProperties.Attribute("refMode")!.Value.Should().Be("A1", because);
        calculationProperties.Attribute("fullPrecision")!.Value.Should().Be("0", because);
        calculationProperties.Attribute("concurrentCalc")!.Value.Should().Be("1", because);
    }

    private static void AssertWorkbookFileVersion(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var fileVersion = workbookXml.Root!.Element(workbookNs + "fileVersion");
        fileVersion.Should().NotBeNull(because);
        fileVersion!.Attribute("appName")!.Value.Should().Be("xl", because);
        fileVersion.Attribute("lastEdited")!.Value.Should().Be("7", because);
        fileVersion.Attribute("lowestEdited")!.Value.Should().Be("7", because);
        fileVersion.Attribute("rupBuild")!.Value.Should().Be("28129", because);
        fileVersion.Attribute("customVersionFlag").Should().BeNull(because);
    }

    private static void AssertWorkbookFileRecovery(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var recoveryBlocks = workbookXml.Root!.Elements(workbookNs + "fileRecoveryPr").ToArray();
        recoveryBlocks.Should().HaveCount(2, because);
        recoveryBlocks[0].Attribute("autoRecover")!.Value.Should().Be("1", because);
        recoveryBlocks[0].Attribute("crashSave")!.Value.Should().Be("1", because);
        recoveryBlocks[0].Attribute("customRecoveryFlag").Should().BeNull(because);
        recoveryBlocks[0].Attribute("repairLoad")!.Value.Should().Be("0", because);
        recoveryBlocks[1].Attribute("dataExtractLoad")!.Value.Should().Be("1", because);
        recoveryBlocks[1].Attribute("repairLoad")!.Value.Should().Be("1", because);
    }

    private static void AssertWorkbookFileSharing(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var fileSharing = workbookXml.Root!.Element(workbookNs + "fileSharing");
        fileSharing.Should().NotBeNull(because);
        fileSharing!.Attribute("readOnlyRecommended")!.Value.Should().Be("1", because);
        fileSharing.Attribute("userName")!.Value.Should().Be("FreeXTest", because);
        fileSharing.Attribute("revisionsPassword").Should().BeNull(because);
    }

    private static void AssertWorkbookProtectionNative(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var protection = workbookXml.Root!.Element(workbookNs + "workbookProtection");
        protection.Should().NotBeNull(because);
        protection!.Attribute("lockStructure")!.Value.Should().Be("1", because);
        protection.Attribute("lockWindows")!.Value.Should().Be("1", because);
        protection.Attribute("workbookPassword")!.Value.Should().Be("83AF", because);
        protection.Attribute("algorithmName").Should().BeNull(because);
        protection.Attribute("hashValue").Should().BeNull(because);
        protection.Attribute("saltValue").Should().BeNull(because);
        protection.Attribute("spinCount").Should().BeNull(because);
        protection.HasElements.Should().BeFalse(because);
    }

    private static void AssertWorkbookSmartTags(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var smartTagProperties = workbookXml.Root!.Element(workbookNs + "smartTagPr");
        smartTagProperties.Should().NotBeNull(because);
        smartTagProperties!.Attribute("embed")!.Value.Should().Be("1", because);
        smartTagProperties.Attribute("show")!.Value.Should().Be("all", because);
        smartTagProperties.Attribute("customSmartTagFlag").Should().BeNull(because);

        var smartTagTypes = workbookXml.Root.Element(workbookNs + "smartTagTypes");
        smartTagTypes.Should().NotBeNull(because);
        smartTagTypes!.Attribute("customSmartTagTypesFlag").Should().BeNull(because);
        var smartTagType = smartTagTypes.Elements(workbookNs + "smartTagType")
            .Should()
            .ContainSingle(because)
            .Subject;
        smartTagType.Attribute("namespaceUri")!.Value.Should().Be("urn:schemas-microsoft-com:office:smarttags", because);
        smartTagType.Attribute("name")!.Value.Should().Be("place", because);
        smartTagType.Attribute("customSmartTagTypeFlag").Should().BeNull(because);
    }

    private static void AssertWorkbookFunctionGroups(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var functionGroups = workbookXml.Root!.Element(workbookNs + "functionGroups");
        functionGroups.Should().NotBeNull(because);
        functionGroups!.Attribute("builtInGroupCount")!.Value.Should().Be("16", because);
        functionGroups.Attribute("customFunctionGroupFlag").Should().BeNull(because);
        var functionGroup = functionGroups.Elements(workbookNs + "functionGroup")
            .Should()
            .ContainSingle(because)
            .Subject;
        functionGroup.Attribute("name")!.Value.Should().Be("FreeXNativeFunctions", because);
        functionGroup.Attribute("customGroupFlag").Should().BeNull(because);
    }

    private static void AssertWorkbookViews(Stream package, string because, bool expectCustomViews = true)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var views = workbookXml.Root!
            .Element(workbookNs + "bookViews")!
            .Elements(workbookNs + "workbookView")
            .ToList();
        views.Should().HaveCount(2, because);
        var hasPrimaryView = views.Any(view =>
            string.Equals(view.Attribute("visibility")?.Value, "visible", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("showSheetTabs")?.Value, "0", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("tabRatio")?.Value, "700", StringComparison.Ordinal));
        hasPrimaryView.Should().BeTrue(because);
        var hasAdditionalView = views.Any(view =>
            string.Equals(view.Attribute("visibility")?.Value, "hidden", StringComparison.Ordinal) &&
            view.Attribute("customWorkbookViewFlag") is null &&
            string.Equals(view.Attribute("showHorizontalScroll")?.Value, "0", StringComparison.Ordinal));
        hasAdditionalView.Should().BeTrue(because);

        var customWorkbookViews = workbookXml.Root.Element(workbookNs + "customWorkbookViews");
        if (!expectCustomViews)
        {
            customWorkbookViews.Should().BeNull(because);
            return;
        }

        var customView = customWorkbookViews!
            .Elements(workbookNs + "customWorkbookView")
            .Should()
            .ContainSingle(because)
            .Subject;
        customView.Attribute("name")!.Value.Should().Be("FreeXView", because);
        customView.Attribute("guid")!.Value.Should().Be("{22222222-2222-2222-2222-222222222222}", because);
        customView.Attribute("includePrintSettings")!.Value.Should().Be("1", because);
        customView.Attribute("includeHiddenRowCol")!.Value.Should().Be("1", because);
    }

    private static void AssertWorkbookDefinedNamesNative(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var definedName = workbookXml.Root!
            .Element(workbookNs + "definedNames")!
            .Elements(workbookNs + "definedName")
            .Should()
            .ContainSingle(because)
            .Subject;
        definedName.Attribute("name")!.Value.Should().Be("DynamicSalesRange", because);
        definedName.Attribute("hidden")!.Value.Should().Be("1", because);
        definedName.Value.Should().Be("1+1", because);
    }

    private static void AssertStylesheetNativeMetadata(Stream package, string because)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!);
        var colors = stylesXml.Root!.Element(workbookNs + "colors");
        colors.Should().NotBeNull(because);
        colors!.ToString(SaveOptions.DisableFormatting).Should().Contain("rgb=\"FF010203\"", because);

        var tableStyles = stylesXml.Root.Element(workbookNs + "tableStyles");
        tableStyles.Should().NotBeNull(because);
        tableStyles!.Attribute("defaultPivotStyle")!.Value.Should().Be("PivotStyleMedium9", because);
        tableStyles.Elements(workbookNs + "tableStyle")
            .Where(element => string.Equals(element.Attribute("name")?.Value, "FreeXNativeTableStyle", StringComparison.Ordinal))
            .Should()
            .ContainSingle(because);
        tableStyles.Elements(workbookNs + "tableStyle")
            .Where(element =>
                string.Equals(element.Attribute("name")?.Value, "FreeXNativePivotStyle", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("pivot")?.Value, "1", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("table")?.Value, "0", StringComparison.Ordinal) &&
                string.Equals(element.Element(workbookNs + "tableStyleElement")?.Attribute("dxfId")?.Value, "0", StringComparison.Ordinal))
            .Should()
            .ContainSingle(because);

        var differentialStyle = stylesXml.Root.Element(workbookNs + "dxfs")!
            .Elements(workbookNs + "dxf")
            .Should()
            .ContainSingle(because)
            .Subject;
        differentialStyle.Attribute("nativePivotDxf").Should().BeNull(because);
        differentialStyle.Elements().Where(element => element.Name.LocalName == "pivotStyleDxfNativeChild")
            .Should()
            .BeEmpty(because);

        var extensionList = stylesXml.Root.Element(workbookNs + "extLst");
        extensionList.Should().NotBeNull(because);
        extensionList!.ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("{FFEEDDCC-7788-6655-4433-22110099AABB}", because)
            .And.Contain("FreeXNativeStylesExtension", because);
    }

    private static void AssertWorkbookThemeNativeSchemes(Stream package, string because)
    {
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var themeXml = LoadPackageXml(archive.GetEntry("xl/theme/theme1.xml")!);
        var themeElements = themeXml.Root!.Element(drawingNs + "themeElements")!;
        var colorScheme = themeElements.Element(drawingNs + "clrScheme")!;
        colorScheme.Element(drawingNs + "accent1")!
            .Element(drawingNs + "srgbClr")!
            .Element(drawingNs + "lumMod")!
            .Attribute("val")!
            .Value
            .Should()
            .Be("75000", because);
        var majorFont = themeElements.Element(drawingNs + "fontScheme")!.Element(drawingNs + "majorFont")!;
        majorFont.Element(drawingNs + "ea")!
            .Attribute("typeface")!
            .Value
            .Should()
            .Be("Major East Asia", because);
        majorFont.Element(drawingNs + "font")!
            .Attribute("script")!
            .Value
            .Should()
            .Be("Jpan", because);
    }

    private static void AssertTableRefFormulaPackageGraph(Stream package, string because)
    {
        if (package.CanSeek)
            package.Position = 0;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.Descendants(worksheetNs + "f")
            .Select(formula => formula.Value)
            .Where(formula =>
                formula.Contains("Price", StringComparison.OrdinalIgnoreCase) &&
                formula.Contains("Qty", StringComparison.OrdinalIgnoreCase))
            .Should()
            .HaveCount(2, because);

        var tableParts = worksheetXml.Root!.Element(worksheetNs + "tableParts");
        tableParts.Should().NotBeNull(because);
        tableParts!.Attribute("count")!.Value.Should().Be("1", because);
        var tablePart = tableParts.Elements(worksheetNs + "tablePart")
            .Should()
            .ContainSingle(because)
            .Which;
        var tableRelId = tablePart.Attribute(officeRelNs + "id")?.Value;
        tableRelId.Should().NotBeNullOrWhiteSpace(because);

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Should()
            .ContainSingle(rel =>
                string.Equals(AttributeValue(rel, "Id"), tableRelId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AttributeValue(rel, "Type"), "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AttributeValue(rel, "Target"), "../tables/table1.xml", StringComparison.OrdinalIgnoreCase),
                because);

        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!);
        tableXml.Root!.Name.Should().Be(worksheetNs + "table", because);
        tableXml.Root.Attribute("displayName")!.Value.Should().Be("SalesTable", because);
        tableXml.Root.Attribute("ref")!.Value.Should().Be("A1:D3", because);
        tableXml.Root.Element(worksheetNs + "autoFilter")!.Attribute("ref")!.Value.Should().Be("A1:D3", because);
        tableXml.Root.Element(worksheetNs + "tableColumns")!
            .Elements(worksheetNs + "tableColumn")
            .Select(column => column.Attribute("name")?.Value)
            .Should()
            .Equal("Item", "Price", "Qty", "Total");
        AssertContentTypeOverride(
            archive,
            "/xl/tables/table1.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml",
            because);
    }

    private static void AssertCrossSheetFormulaPackageGraph(Stream package, string because)
    {
        if (package.CanSeek)
            package.Position = 0;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull(because);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.Descendants(workbookNs + "f")
            .Select(formula => formula.Value)
            .Where(formula =>
                formula.Contains("SUMIF", StringComparison.OrdinalIgnoreCase) &&
                formula.Contains("Lookup", StringComparison.OrdinalIgnoreCase))
            .Should()
            .HaveCount(2, because);

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var lookupSheet = workbookXml.Root!
            .Element(workbookNs + "sheets")!
            .Elements(workbookNs + "sheet")
            .Should()
            .ContainSingle(sheet => string.Equals(AttributeValue(sheet, "name"), "Lookup", StringComparison.OrdinalIgnoreCase), because)
            .Which;
        var lookupRelId = lookupSheet.Attribute(officeRelNs + "id")?.Value;
        lookupRelId.Should().NotBeNullOrWhiteSpace(because);

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Should()
            .ContainSingle(rel =>
                string.Equals(AttributeValue(rel, "Id"), lookupRelId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AttributeValue(rel, "Type"), "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", StringComparison.OrdinalIgnoreCase) &&
                AttributeValue(rel, "Target") != null &&
                AttributeValue(rel, "Target")!.EndsWith("worksheets/sheet2.xml", StringComparison.OrdinalIgnoreCase),
                because);
        AssertContentTypeOverride(
            archive,
            "/xl/worksheets/sheet2.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
            because);
    }

    private static void AssertNamedRangeCountPackageGraph(Stream package, string because)
    {
        if (package.CanSeek)
            package.Position = 0;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var definedNames = workbookXml.Root!
            .Element(workbookNs + "definedNames")!
            .Elements(workbookNs + "definedName")
            .ToArray();

        definedNames.Should().HaveCount(12, because);
        definedNames.Select(name => name.Attribute("name")?.Value)
            .Should()
            .Equal(Enumerable.Range(1, 12).Select(index => $"Range{index:00}"), because);
        NormalizeDefinedNameReference(definedNames[0].Value).Should().Contain("Sheet1!A1:A3", because);
        NormalizeDefinedNameReference(definedNames[11].Value).Should().Contain("Sheet1!L1:L3", because);
    }

    private static string NormalizeDefinedNameReference(string reference) =>
        reference.Replace("'", "", StringComparison.Ordinal).Replace("$", "", StringComparison.Ordinal);

    private static void AssertContentTypeOverride(
        ZipArchive archive,
        string partName,
        string contentType,
        string because)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                string.Equals(AttributeValue(element, "PartName"), partName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(AttributeValue(element, "ContentType"), contentType, StringComparison.OrdinalIgnoreCase),
                because);
    }

    private static string? AttributeValue(XElement element, XName name) => element.Attribute(name)?.Value;

    private static void AssertHeaderFooterLegacyDrawingPackageGraph(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace officeNs = "urn:schemas-microsoft-com:office:office";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull(because);
        archive.GetEntry("xl/drawings/_rels/vmlDrawing1.vml.rels").Should().NotBeNull(because);
        archive.GetEntry("xl/media/headerFooterImage1.png").Should().NotBeNull(because);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var legacyDrawing = worksheetXml.Root!.Element(worksheetNs + "legacyDrawingHF");
        legacyDrawing.Should().NotBeNull(because);
        var relId = legacyDrawing!.Attribute(officeRelNs + "id")?.Value;
        relId.Should().NotBeNullOrWhiteSpace(because);

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Id")?.Value, relId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../drawings/vmlDrawing1.vml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);

        var vmlDrawing = LoadPackageXml(archive.GetEntry("xl/drawings/vmlDrawing1.vml")!);
        vmlDrawing.Descendants()
            .Where(element => element.Attribute(officeNs + "relid")?.Value == "rIdImage1")
            .Should()
            .ContainSingle(because);

        var vmlRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/vmlDrawing1.vml.rels")!);
        vmlRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Id")?.Value, "rIdImage1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../media/headerFooterImage1.png", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertWorksheetLegacyDrawingPackageGraph(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull(because);
        archive.GetEntry("xl/drawings/_rels/vmlDrawing1.vml.rels").Should().NotBeNull(because);
        archive.GetEntry("xl/media/vmlImage1.png").Should().NotBeNull(because);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var legacyDrawing = worksheetXml.Root!.Element(worksheetNs + "legacyDrawing");
        legacyDrawing.Should().NotBeNull(because);
        legacyDrawing!.Attribute(officeRelNs + "id")!.Value.Should().Be("rIdFreeXLegacyDrawing", because);

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        var hasLegacyDrawingRelationship = worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Count(rel =>
                string.Equals(rel.Attribute("Id")?.Value, "rIdFreeXLegacyDrawing", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../drawings/vmlDrawing1.vml", StringComparison.OrdinalIgnoreCase)) == 1;
        hasLegacyDrawingRelationship.Should().BeTrue(because);

        var vmlDrawing = LoadPackageXml(archive.GetEntry("xl/drawings/vmlDrawing1.vml")!);
        vmlDrawing.Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "shape", StringComparison.OrdinalIgnoreCase))
            .Attribute("id")!.Value.Should().Be("FreeXLegacyDrawingShape", because);

        var vmlRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/vmlDrawing1.vml.rels")!);
        var hasImageRelationship = vmlRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Count(rel =>
                string.Equals(rel.Attribute("Id")?.Value, "rIdFreeXVmlImage", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../media/vmlImage1.png", StringComparison.OrdinalIgnoreCase)) == 1;
        hasImageRelationship.Should().BeTrue(because);
    }

    private static void AssertStableDocumentProperties(Stream package, string because)
    {
        XNamespace corePropertiesNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
        XNamespace extendedPropertiesNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var coreProperties = LoadPackageXml(archive.GetEntry("docProps/core.xml")!);
        coreProperties.Root!.Name.Should().Be(corePropertiesNs + "coreProperties", because);
        coreProperties.Root.Element(dcNs + "title")!.Value.Should().Be("FreeX document property corpus", because);
        coreProperties.Root.Element(dcNs + "subject")!.Value.Should().Be("Stable document properties retained", because);
        coreProperties.Root.Element(corePropertiesNs + "keywords")!.Value.Should().Be("xlsx parity", because);
        coreProperties.Root.Element(corePropertiesNs + "lastModifiedBy")!.Value.Should().Be("FreeX Fixture", because);

        var appProperties = LoadPackageXml(archive.GetEntry("docProps/app.xml")!);
        appProperties.Root!.Name.Should().Be(extendedPropertiesNs + "Properties", because);
        appProperties.Root.Element(extendedPropertiesNs + "Application")!.Value.Should().Be("Microsoft Excel", because);
        appProperties.Root.Element(extendedPropertiesNs + "Company")!.Value.Should().Be("FreeX Test Lab", because);
        appProperties.Root.Element(extendedPropertiesNs + "Manager")!.Value.Should().Be("Workbook Fidelity", because);

        var packageRelsXml = LoadPackageXml(archive.GetEntry("_rels/.rels")!);
        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value.TrimStart('/'), "docProps/app.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertCalcChainReference(Stream package, string because)
    {
        XNamespace calcNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var calcChain = LoadPackageXml(archive.GetEntry("xl/calcChain.xml")!);
        calcChain.Root!.Name.Should().Be(calcNs + "calcChain", because);
        calcChain.Root.Elements(calcNs + "c").Should().ContainSingle(because)
            .Which.Attribute("r")!.Value.Should().Be("A1", because);

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "/xl/calcChain.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertCustomDocumentProperties(Stream package, string because)
    {
        XNamespace customPropertiesNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var customProperties = LoadPackageXml(archive.GetEntry("docProps/custom.xml")!);
        var propertiesByName = customProperties.Root!
            .Elements(customPropertiesNs + "property")
            .ToDictionary(property => property.Attribute("name")?.Value ?? "", StringComparer.OrdinalIgnoreCase);
        propertiesByName.Should().ContainKey("Department", because);
        propertiesByName["Department"].Value.Should().Be("Compliance", because);
        propertiesByName.Should().ContainKey("MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled", because);
        propertiesByName["MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled"].Value.Should().Be("true", because);

        var packageRelsXml = LoadPackageXml(archive.GetEntry("_rels/.rels")!);
        packageRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "docProps/custom.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertCustomXmlPackageGraph(Stream package, string because)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("customXml/item1.xml").Should().NotBeNull(because);
        archive.GetEntry("customXml/itemProps1.xml").Should().NotBeNull(because);
        archive.GetEntry("customXml/_rels/item1.xml.rels").Should().NotBeNull(because);

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../customXml/item1.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeEmpty(because);

        var itemRelsXml = LoadPackageXml(archive.GetEntry("customXml/_rels/item1.xml.rels")!);
        itemRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "itemProps1.xml", StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle(because);
    }

    private static void AssertPrinterSettingsReference(Stream package, string because)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/printerSettings/printerSettings1.bin").Should().NotBeNull(because);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var relId = worksheetXml.Root?
            .Element(worksheetNs + "pageSetup")?
            .Attribute(officeRelNs + "id")?
            .Value;
        relId.Should().Be("rIdPrinterSettings1", because);

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        var printerRelationships = worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(rel =>
                string.Equals(rel.Attribute("Id")?.Value, "rIdPrinterSettings1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Type")?.Value, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("Target")?.Value, "../printerSettings/printerSettings1.bin", StringComparison.OrdinalIgnoreCase))
            .ToList();
        printerRelationships.Should().ContainSingle(because);
    }

    [Fact]
    public void PackageSummary_TreatsDocumentPropertiesAsFidelityCriticalParts()
    {
        var workbook = new Workbook("DocumentPropertiesCriticalParts");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("document properties"));

        using var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            ReplacePackageXml(
                archive,
                "docProps/core.xml",
                new XDocument(new XElement(
                    XName.Get("coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                    new XElement(XName.Get("subject", "http://purl.org/dc/elements/1.1/"), "FreeX parity subject"))));
            ReplacePackageXml(
                archive,
                "docProps/app.xml",
                new XDocument(new XElement(
                    XName.Get("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                    new XElement(XName.Get("Company", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"), "FreeX Test Lab"))));
        }

        package.Position = 0;
        var summary = CapturePackageSummary(package);

        summary.CriticalParts.Should().Contain("docProps/core.xml");
        summary.CriticalParts.Should().Contain("docProps/app.xml");
    }

    [Fact]
    public void PackageHealth_AllowsPercentEncodedInternalRelationshipTargets()
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"/>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/image%201.png"/>
                </Relationships>
                """);
            archive.CreateEntry("xl/media/image 1.png");
        }

        package.Position = 0;
        var act = () => AssertPackageHealth(package, "percent-encoded relationship target");

        act.Should().NotThrow();
    }

    private static string[] CaptureKnownGapFixtureParts(string id)
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapPackage(id);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: false);
        return archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(IsFidelityCriticalPart)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ContentTypeOverridesForParts(
        PackagePartSummary package,
        IReadOnlyList<string> partNames)
    {
        var overridePrefixes = partNames
            .Select(part => "/" + part.TrimStart('/').Replace('\\', '/') + "=>")
            .ToArray();

        return package.CriticalContentTypeOverrides
            .Where(entry => overridePrefixes.Any(prefix => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IReadOnlyList<string> RelationshipDetailsForParts(
        PackagePartSummary package,
        IReadOnlyList<string> partNames)
    {
        var partSet = partNames
            .Select(part => part.TrimStart('/').Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipPrefixes = partNames
            .Select(GetRelationshipPartPathForPart)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path + "=>")
            .ToArray();

        return package.CriticalRelationshipDetails
            .Where(entry =>
                relationshipPrefixes.Any(prefix => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                IsWorkbookRelationshipToCriticalPart(entry, partSet))
            .ToArray();
    }

    private static string GetRelationshipPartPathForPart(string partName)
    {
        var path = partName.TrimStart('/').Replace('\\', '/');
        if (string.Equals(path, "_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/_rels/", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return path;

        var slashIndex = path.LastIndexOf('/');
        return slashIndex < 0
            ? $"_rels/{path}.rels"
            : $"{path[..slashIndex]}/_rels/{path[(slashIndex + 1)..]}.rels";
    }

    private static bool IsWorkbookRelationshipToCriticalPart(string relationshipDetail, ISet<string> partNames)
    {
        const string workbookRelsPrefix = "xl/_rels/workbook.xml.rels=>";
        if (!relationshipDetail.StartsWith(workbookRelsPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var targetEnd = relationshipDetail.IndexOf("|type=", StringComparison.Ordinal);
        if (targetEnd < 0)
            return false;

        var target = relationshipDetail[workbookRelsPrefix.Length..targetEnd];
        if (string.Equals(target, "worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, "/xl/worksheets/sheet1.xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = target.StartsWith("/", StringComparison.Ordinal)
            ? target.TrimStart('/')
            : target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                ? target
                : "xl/" + target.TrimStart('/');
        return partNames.Contains(normalized);
    }

}
