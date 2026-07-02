using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageRetentionTests
{
    private static readonly string[] ExpectedCorpusDeckNames =
    [
        "01-title-slide.pptx",
        "02-autoshapes.pptx",
        "03-mixed-text.pptx",
        "04-picture.pptx",
        "05-table.pptx",
        "06-charts.pptx",
        "07-customgeom.pptx",
        "08-effects.pptx",
        "09-smartart.pptx",
        "10-motionpath.pptx",
        "11-bevel3d.pptx",
        "12-fills.pptx",
        "13-wordart.pptx",
        "14-smartart-live.pptx",
        "15-picture-crop.pptx",
        "16-bg-tabs-vtext.pptx",
        "17-bullets-autofit.pptx",
        "18-chart-types.pptx",
        "19-chart-labels.pptx",
        "20-columns-gradoutline.pptx",
        "21-comments-notes.pptx",
    ];

    private static readonly string[] WriterOwnedPackagePartPaths =
    [
        "[Content_Types].xml",
        "_rels/.rels",
        OpcPackageProperties.CorePropertiesZipEntry,
        "ppt/presentation.xml",
        "ppt/_rels/presentation.xml.rels",
        "ppt/presProps.xml",
        "ppt/viewProps.xml",
        "ppt/tableStyles.xml",
        "ppt/commentAuthors.xml",
    ];

    private static readonly string[] WriterOwnedPackagePartPrefixes =
    [
        "ppt/slides/",
        "ppt/slideLayouts/",
        "ppt/slideMasters/",
        "ppt/theme/",
        "ppt/charts/",
        "ppt/media/",
        "ppt/comments/",
        "ppt/notesSlides/",
        "ppt/notesMasters/",
        "ppt/embeddings/",
        "ppt/diagrams/",
    ];

    private static readonly HashSet<string> WriterOwnedRelationshipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
        OpcPackageProperties.CorePropertiesRelationshipType,
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/viewProps",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors",
        "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing",
    };

    private const string CustomXmlRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    private const string ExternalReviewRelType =
        "http://example.com/freep/relationships/reviewLink";
    private const string UnknownViewRelType =
        "http://example.com/freep/relationships/viewState";
    private const string PrinterSettingsRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings";
    private const string UnknownSlideMirrorRelType =
        "http://example.com/freep/relationships/slideMirror";
    private const string PackageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";
    private const string SpreadsheetWorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEnumerable<object[]> CorpusDecks() =>
        ExpectedCorpusDeckNames.Select(name => new object[] { name });

    public static IEnumerable<object[]> SemanticEditCorpusDecks()
    {
        yield return ["04-picture.pptx", new[] { "ppt/media/" }];
        yield return ["06-charts.pptx", new[] { "ppt/charts/", "ppt/embeddings/" }];
        yield return ["14-smartart-live.pptx", new[] { "ppt/diagrams/" }];
        yield return ["15-picture-crop.pptx", new[] { "ppt/media/" }];
        yield return ["18-chart-types.pptx", new[] { "ppt/charts/", "ppt/embeddings/" }];
        yield return ["19-chart-labels.pptx", new[] { "ppt/charts/", "ppt/embeddings/" }];
        yield return ["21-comments-notes.pptx", new[] { "ppt/comments/", "ppt/notesSlides/", "ppt/notesMasters/" }];
    }

    [Fact]
    public void RenderCompareCorpus_TracksExpectedDecks()
    {
        var corpusDirectory = FindCorpusDirectory();
        Directory.GetFiles(corpusDirectory, "*.pptx")
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Should()
            .Equal(ExpectedCorpusDeckNames.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(CorpusDecks))]
    public void RenderCompareCorpusDeck_OpenSaveReopen_RetainsSharedPackageContract(string deckName)
    {
        var sourcePath = Path.Combine(FindCorpusDirectory(), deckName);
        var loaded = PptxPackageReader.Read(sourcePath);
        loaded.PackageSnapshot.Should().NotBeNull($"{deckName} must be captured by the shared preserve-bag layer");
        loaded.Slides.Should().NotBeEmpty($"{deckName} should load through shared Core.IO before save");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();
        savedBytes.Should().NotBeEmpty($"{deckName} should save through shared Core.IO");

        using var reopenedStream = new MemoryStream(savedBytes);
        var reopened = PptxPackageReader.Read(reopenedStream);
        reopened.Slides.Should().HaveCount(loaded.Slides.Count, $"{deckName} should reopen after Core.IO save");
        reopened.PackageSnapshot.Should().NotBeNull($"{deckName} should capture a package snapshot after reopen");

        using var sourceArchive = ZipFile.OpenRead(sourcePath);
        using var savedArchive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);

        AssertPreservedPackageEntries(sourceArchive, savedArchive, deckName);
        AssertPreservedContentTypes(sourceArchive, savedArchive, deckName);
        AssertPreservedRelationships(sourceArchive, savedArchive, deckName);
    }

    [Theory]
    [MemberData(nameof(SemanticEditCorpusDecks))]
    public void RenderCompareHighRiskCorpusDeck_SemanticEdit_RetainsPackageContract(
        string deckName,
        string[] featurePartPrefixes)
    {
        var sourcePath = Path.Combine(FindCorpusDirectory(), deckName);
        var loaded = PptxPackageReader.Read(sourcePath);
        loaded.PackageSnapshot.Should().NotBeNull($"{deckName} must be captured before semantic edits");
        loaded.Slides.Should().NotBeEmpty($"{deckName} should load through shared Core.IO before edit");

        var editShapeName = AddModeledShapeEdit(loaded, deckName);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();
        savedBytes.Should().NotBeEmpty($"{deckName} should save after a modeled edit");

        using var reopenedStream = new MemoryStream(savedBytes);
        var reopened = PptxPackageReader.Read(reopenedStream);
        reopened.Slides.Should().HaveCount(loaded.Slides.Count, $"{deckName} should reopen after a modeled edit");
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == editShapeName,
            $"{deckName} should retain the writer-owned semantic edit after reopen");

        using var sourceArchive = ZipFile.OpenRead(sourcePath);
        using var savedArchive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);

        AssertPreservedPackageEntries(sourceArchive, savedArchive, deckName);
        AssertPreservedContentTypes(sourceArchive, savedArchive, deckName);
        AssertPreservedRelationships(sourceArchive, savedArchive, deckName);
        AssertFeaturePackageEntriesStillPresent(sourceArchive, savedArchive, deckName, featurePartPrefixes);
        AssertFeatureContentTypesStillPresent(sourceArchive, savedArchive, deckName, featurePartPrefixes);
        AssertFeatureRelationshipsStillPresent(sourceArchive, savedArchive, deckName, featurePartPrefixes);
    }

    [Fact]
    public void RenderCompareCommentsNotesCorpusDeck_SemanticEdit_RetainsModeledNotesCommentsAndPackageParts()
    {
        const string deckName = "21-comments-notes.pptx";
        var sourcePath = Path.Combine(FindCorpusDirectory(), deckName);
        var loaded = PptxPackageReader.Read(sourcePath);
        loaded.PackageSnapshot.Should().NotBeNull($"{deckName} must be captured before semantic edits");
        loaded.Slides.Should().HaveCount(2);
        loaded.Slides[0].Comments.Should().ContainSingle(comment =>
            comment.Author == "Alice Reviewer" &&
            comment.Initials == "AR" &&
            comment.Text == "Confirm the title before publishing.");
        loaded.Slides[1].Comments.Should().HaveCount(2);
        TextBodyText(loaded.Slides[0].Notes).Should().Contain("review workflow");
        TextBodyText(loaded.Slides[1].Notes).Should().Contain("comment decisions");

        var editShapeName = AddModeledShapeEdit(loaded, deckName);

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using var reopenedStream = new MemoryStream(savedBytes);
        var reopened = PptxPackageReader.Read(reopenedStream);
        reopened.Slides.Should().HaveCount(2);
        reopened.Slides[0].Shapes.Should().Contain(shape => shape.Name == editShapeName);
        reopened.Slides[0].Comments.Should().ContainSingle(comment =>
            comment.Author == "Alice Reviewer" &&
            comment.Initials == "AR" &&
            comment.Text == "Confirm the title before publishing.");
        reopened.Slides[1].Comments.Select(comment => comment.Author)
            .Should().Equal("Bob Reviewer", "Alice Reviewer");
        TextBodyText(reopened.Slides[0].Notes).Should().Contain("package save");
        TextBodyText(reopened.Slides[1].Notes).Should().Contain("comment decisions");

        using var sourceArchive = ZipFile.OpenRead(sourcePath);
        using var savedArchive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);
        AssertPreservedPackageEntries(sourceArchive, savedArchive, deckName);
        AssertPreservedContentTypes(sourceArchive, savedArchive, deckName);
        AssertPreservedRelationships(sourceArchive, savedArchive, deckName);
        AssertCommentsNotesPackageParts(savedArchive);
    }

    [Fact]
    public void CoreProperties_RoundTripThroughPptxPackage()
    {
        var presentation = Presentation.CreateEmpty();
        var created = new DateTimeOffset(2026, 6, 29, 9, 30, 0, TimeSpan.Zero);
        var modified = created.AddMinutes(45);
        typeof(Presentation)
            .GetProperty(nameof(Presentation.Properties))!
            .PropertyType
            .Should()
            .Be(typeof(DocumentProperties));

        presentation.Properties.Title = "FreeP title";
        presentation.Properties.Author = "FreeP author";
        presentation.Properties.Subject = "FreeP subject";
        presentation.Properties.Keywords = "freep,pptx,opc";
        presentation.Properties.Comments = "FreeP comments";
        presentation.Properties.LastModifiedBy = "FreeP editor";
        presentation.Properties.Created = created;
        presentation.Properties.Modified = modified;
        presentation.Properties.Category = "FreeP category";
        presentation.Properties.ContentStatus = "Draft";
        presentation.Properties.Language = "en-US";
        presentation.Properties.Version = "2026.06";

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var coreProperties = OpcDocumentProperties.ReadCoreProperties(
                LoadXml(archive, OpcPackageProperties.CorePropertiesZipEntry));
            coreProperties.Should().Be(new CoreDocumentProperties(
                Title: "FreeP title",
                Author: "FreeP author",
                Subject: "FreeP subject",
                Keywords: "freep,pptx,opc",
                Comments: "FreeP comments",
                LastModifiedBy: "FreeP editor",
                Created: created,
                Modified: modified,
                Category: "FreeP category",
                ContentStatus: "Draft",
                Language: "en-US",
                Version: "2026.06"));
        }

        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);
        reloaded.Properties.Title.Should().Be("FreeP title");
        reloaded.Properties.Author.Should().Be("FreeP author");
        reloaded.Properties.Subject.Should().Be("FreeP subject");
        reloaded.Properties.Keywords.Should().Be("freep,pptx,opc");
        reloaded.Properties.Comments.Should().Be("FreeP comments");
        reloaded.Properties.LastModifiedBy.Should().Be("FreeP editor");
        reloaded.Properties.Created.Should().Be(created);
        reloaded.Properties.Modified.Should().Be(modified);
        reloaded.Properties.Category.Should().Be("FreeP category");
        reloaded.Properties.ContentStatus.Should().Be("Draft");
        reloaded.Properties.Language.Should().Be("en-US");
        reloaded.Properties.Version.Should().Be("2026.06");
    }

    [Fact]
    public void ReadWriteRead_RetainsUnmodeledPackagePartsRelationshipsAndContentTypes()
    {
        using var source = BuildPptxWithUnmodeledPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.Slides.Should().HaveCount(1);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 77,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var extendedProperties = OpcDocumentProperties.ReadExtendedProperties(
                LoadXml(archive, OpcPackageProperties.ExtendedPropertiesZipEntry));
            extendedProperties.Application.Should().Be("FreeP retention harness");
            var customProperties = OpcCustomDocumentProperties.FromDocument(
                LoadXml(archive, OpcPackageProperties.CustomPropertiesZipEntry));
            customProperties.GetString("RetentionMarker").Should().Be("retain-me");
            ReadText(archive, "customXml/item1.xml").Should().Contain("retain-me");
            ReadText(archive, "customXml/itemProps1.xml").Should().Contain("itemID");
            ReadText(archive, "customXml/payload.freex").Should().Contain("freex-payload");
            ReadBytes(archive, "ppt/customData/viewState.bin").Should().Equal(new byte[] { 0x46, 0x50, 0x52, 0x01 });

            var rootRels = LoadXml(archive, "_rels/.rels");
            Relationship(
                rootRels,
                OpcPackageProperties.ExtendedPropertiesRelationshipType,
                OpcPackageProperties.ExtendedPropertiesZipEntry).Should().NotBeNull();
            Relationship(
                rootRels,
                OpcPackageProperties.CustomPropertiesRelationshipType,
                OpcPackageProperties.CustomPropertiesZipEntry).Should().NotBeNull();
            Relationship(rootRels, CustomXmlRelType, "customXml/item1.xml").Should().NotBeNull();
            var externalReviewRel = Relationship(rootRels, ExternalReviewRelType, "https://example.com/freep-review");
            externalReviewRel.Should().NotBeNull();
            externalReviewRel!.Attribute("TargetMode")?.Value.Should().Be("External");
            Relationship(rootRels, UnknownSlideMirrorRelType, "ppt/slides/slide1.xml").Should().BeNull();

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            Relationship(presRels, UnknownViewRelType, "customData/viewState.bin").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(
                contentTypes,
                OpcPackageProperties.ExtendedPropertiesPartName,
                OpcPackageProperties.ExtendedPropertiesContentType).Should().NotBeNull();
            Override(
                contentTypes,
                OpcPackageProperties.CustomPropertiesPartName,
                OpcPackageProperties.CustomPropertiesContentType).Should().NotBeNull();
            Override(contentTypes, "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml").Should().NotBeNull();
            Override(contentTypes, "/ppt/customData/viewState.bin",
                "application/vnd.example.freep.viewstate").Should().NotBeNull();
            Default(contentTypes, "freex", "application/vnd.example.freep.payload").Should().NotBeNull();
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Name == "Modeled edit");
    }

    [Fact]
    public void ReadWriteRead_SemanticEditRetainsPresentationScopedCustomXmlPackageParts()
    {
        using var source = BuildPptxWithPresentationScopedCustomXml();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.Slides.Should().HaveCount(1);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 79,
            Name = "Modeled presentation custom XML edit",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 685800,
            OffsetYEmu = 685800,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/item2.xml").Should().Contain("presentation-scoped-retain-me");
            ReadText(archive, "customXml/itemProps2.xml").Should().Contain("{22222222-2222-2222-2222-222222222222}");
            ReadText(archive, "customXml/item2.freexmeta").Should().Be("presentation custom xml payload");

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            Relationship(presRels, CustomXmlRelType, "../customXml/item2.xml").Should().NotBeNull();

            var itemRels = LoadXml(archive, "customXml/_rels/item2.xml.rels");
            Relationship(
                itemRels,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps",
                "itemProps2.xml").Should().NotBeNull();
            Relationship(
                itemRels,
                "http://example.com/freep/relationships/customXmlPayload",
                "item2.freexmeta").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(contentTypes, "/customXml/itemProps2.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml").Should().NotBeNull();
            Default(contentTypes, "freexmeta", "application/vnd.example.freep.customxml-payload").Should().NotBeNull();
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Name == "Modeled presentation custom XML edit");
    }

    [Fact]
    public void ReadWriteRead_RetainsViewAndPrintSettingsPackageSemantics()
    {
        using var source = BuildPptxWithViewAndPrintSettings();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.Slides.Should().HaveCount(1);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 78,
            Name = "Modeled view print edit",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var presProps = ReadText(archive, "ppt/presProps.xml");
            presProps.Should().Contain("freep-print-retain");
            presProps.Should().Contain("prnWhat=\"handouts3\"");
            presProps.Should().Contain("frameSlides=\"1\"");

            var viewProps = ReadText(archive, "ppt/viewProps.xml");
            viewProps.Should().Contain("freep-view-retain");
            viewProps.Should().Contain("lastView=\"sldSorterView\"");
            viewProps.Should().Contain("cx=\"12700\"");

            ReadBytes(archive, "ppt/printerSettings/printerSettings1.bin")
                .Should()
                .Equal(new byte[] { 0x46, 0x50, 0x50, 0x01 });

            var presPropsRels = LoadXml(archive, "ppt/_rels/presProps.xml.rels");
            Relationship(
                presPropsRels,
                PrinterSettingsRelType,
                "printerSettings/printerSettings1.bin").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(
                contentTypes,
                "/ppt/printerSettings/printerSettings1.bin",
                "application/vnd.openxmlformats-officedocument.presentationml.printerSettings").Should().NotBeNull();
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Name == "Modeled view print edit");
    }

    [Fact]
    public void ReadWriteRead_ChartDataSemanticEdit_RegeneratesChartWorkbookAndKeepsUnrelatedPackageParts()
    {
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        var chartShape = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart);
        chartShape.Chart.Should().NotBeNull();
        chartShape.Chart!.RegenerateWorkbookOnSave.Should().BeFalse();

        new ReplaceChartDataCommand(
            slideIndex: 0,
            shapeId: chartShape.Id,
            categories: ["East", "West"],
            seriesNames: ["Actual"],
            values: [new double?[] { 42, 51 }]).Apply(loaded);

        chartShape.Chart.RegenerateWorkbookOnSave.Should().BeTrue();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/chartWorkbookPayload.xml")
                .Should()
                .Contain("unrelated-retain-me");
            archive.GetEntry("ppt/embeddings/sourceWorkbook.xlsx").Should().BeNull(
                "a chart data edit must not carry forward the stale source workbook sidecar");

            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            chartXml.ToString(SaveOptions.DisableFormatting).Should().Contain("East");
            chartXml.ToString(SaveOptions.DisableFormatting).Should().Contain("42");
            var externalData = chartXml.Root!.Element(ChartNs + "externalData");
            externalData.Should().NotBeNull("the edited chart should point at a regenerated workbook sidecar");
            externalData!.Attribute(RelsDocNs + "id")!.Value.Should().Be("rIdWorkbook1");

            var chartRels = LoadXml(archive, "ppt/charts/_rels/chart1.xml.rels");
            Relationship(chartRels, PackageRelType, "../embeddings/chartWorkbook1.xlsx").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(
                contentTypes,
                "/ppt/embeddings/chartWorkbook1.xlsx",
                SpreadsheetWorkbookContentType).Should().NotBeNull();
            Override(
                contentTypes,
                "/customXml/chartWorkbookPayload.xml",
                "application/vnd.example.freep.chart-workbook-payload+xml").Should().NotBeNull();

            using var workbookArchive = new ZipArchive(
                new MemoryStream(ReadBytes(archive, "ppt/embeddings/chartWorkbook1.xlsx")),
                ZipArchiveMode.Read);
            var sheetXml = LoadXml(workbookArchive, "xl/worksheets/sheet1.xml")
                .ToString(SaveOptions.DisableFormatting);
            sheetXml.Should().Contain("Actual");
            sheetXml.Should().Contain("East");
            sheetXml.Should().Contain("42");
            sheetXml.Should().Contain("51");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedChart = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        reloadedChart.Categories.Should().Equal("East", "West");
        reloadedChart.Series.Should().ContainSingle();
        reloadedChart.Series[0].Name.Should().Be("Actual");
        reloadedChart.Series[0].Values.Should().Equal(42, 51);
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableSettings_RetainsModeledChartPackageSemantics()
    {
        using var source = BuildPptxWithChartWorkbookAndUnrelatedPackageData();
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull("PowerPoint-authored c:dTable settings should import into the shared model");
        chart.DataTable!.ShowHorizontalBorder.Should().BeTrue();
        chart.DataTable.ShowVerticalBorder.Should().BeFalse();
        chart.DataTable.ShowOutlineBorder.Should().BeTrue();
        chart.DataTable.ShowLegendKeys.Should().BeTrue();
        chart.DataTable.BorderOutline.Should().BeOfType<ShapeOutline.Visible>()
            .Which.Color.Resolved.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
        ((ShapeOutline.Visible)chart.DataTable.BorderOutline!).WidthPt.Should().BeApproximately(1.25, 0.001);
        chart.DataTable.TextStyle.Should().NotBeNull();
        chart.DataTable.TextStyle!.FontSizePt.Should().Be(8.75);
        chart.DataTable.TextStyle.Bold.Should().BeTrue();
        chart.DataTable.TextStyle.Italic.Should().BeTrue();
        chart.DataTable.TextStyle.Color.Should().NotBeNull();
        chart.DataTable.TextStyle.Color!.Resolved.Should().Be(new SrgbColor(0x22, 0x44, 0x66));

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "customXml/chartWorkbookPayload.xml")
                .Should()
                .Contain("unrelated-retain-me");

            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedPlotArea = savedChartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!;
            var savedDataTable = savedPlotArea.Element(ChartNs + "dTable");
            savedDataTable.Should().NotBeNull("saving should write c:dTable back into the chart package part");
            savedDataTable!.Element(ChartNs + "showHorzBorder")!.Attribute("val")!.Value.Should().Be("1");
            savedDataTable.Element(ChartNs + "showVertBorder")!.Attribute("val")!.Value.Should().Be("0");
            savedDataTable.Element(ChartNs + "showOutline")!.Attribute("val")!.Value.Should().Be("1");
            savedDataTable.Element(ChartNs + "showKeys")!.Attribute("val")!.Value.Should().Be("1");
            var savedLine = savedDataTable.Element(ChartNs + "spPr")!.Element(DrawingNs + "ln")!;
            savedLine.Attribute("w")!.Value.Should().Be(DrawingMlUnits.PointsToEmu(1.25).ToString());
            savedLine.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!
                .Attribute("val")!
                .Value.Should().Be("123456");
            var savedDefRPr = savedDataTable.Element(ChartNs + "txPr")!
                .Element(DrawingNs + "p")!
                .Element(DrawingNs + "pPr")!
                .Element(DrawingNs + "defRPr")!;
            savedDefRPr.Attribute("sz")!.Value.Should().Be("875");
            savedDefRPr.Attribute("b")!.Value.Should().Be("1");
            savedDefRPr.Attribute("i")!.Value.Should().Be("1");
            savedDefRPr.Element(DrawingNs + "solidFill")!
                .Element(DrawingNs + "srgbClr")!
                .Attribute("val")!
                .Value.Should().Be("224466");
            savedPlotArea.Elements().Last(element => element.Name == ChartNs + "valAx" || element.Name == ChartNs + "dTable")
                .Name.Should().Be(ChartNs + "dTable", "c:dTable should remain after chart axes in the package chart part");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        reloadedDataTable!.ShowHorizontalBorder.Should().BeTrue();
        reloadedDataTable.ShowVerticalBorder.Should().BeFalse();
        reloadedDataTable.ShowOutlineBorder.Should().BeTrue();
        reloadedDataTable.ShowLegendKeys.Should().BeTrue();
        reloadedDataTable.BorderOutline.Should().BeOfType<ShapeOutline.Visible>()
            .Which.Color.Resolved.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
        ((ShapeOutline.Visible)reloadedDataTable.BorderOutline!).WidthPt.Should().BeApproximately(1.25, 0.001);
        reloadedDataTable.TextStyle.Should().NotBeNull();
        reloadedDataTable.TextStyle!.FontSizePt.Should().Be(8.75);
        reloadedDataTable.TextStyle.Bold.Should().BeTrue();
        reloadedDataTable.TextStyle.Italic.Should().BeTrue();
        reloadedDataTable.TextStyle.Color.Should().NotBeNull();
        reloadedDataTable.TextStyle.Color!.Resolved.Should().Be(new SrgbColor(0x22, 0x44, 0x66));
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableTextStyleFontFamily_RoundTripsAndIsNotDroppedToCalibri()
    {
        // KA1: c:dTable/c:txPr/a:defRPr/a:latin typeface="Georgia" must be captured into
        // ChartTextStyle.FontFamily and re-emitted on save, instead of being silently
        // dropped (which previously caused the data table to always render/save in the
        // renderer's hardcoded "Calibri" default).
        using var source = BuildPptxWithChartDataTableFontFamily("Georgia");
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.TextStyle.Should().NotBeNull();
        chart.DataTable.TextStyle!.FontFamily.Should().Be("Georgia");

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedDefRPr = savedChartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "dTable")!
                .Element(ChartNs + "txPr")!
                .Element(DrawingNs + "p")!
                .Element(DrawingNs + "pPr")!
                .Element(DrawingNs + "defRPr")!;
            savedDefRPr.Element(DrawingNs + "latin").Should().NotBeNull(
                "the data-table font family must round-trip as a:latin, not be dropped on save");
            savedDefRPr.Element(DrawingNs + "latin")!.Attribute("typeface")!.Value.Should().Be("Georgia");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        reloadedDataTable!.TextStyle.Should().NotBeNull();
        reloadedDataTable.TextStyle!.FontFamily.Should().Be("Georgia");
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableTextStyleWithoutLatin_FontFamilyIsNullNotDefaulted()
    {
        // No a:latin present on the source defRPr → FontFamily should stay null (unset),
        // not be defaulted to anything — the renderer default only applies at draw time.
        using var source = BuildPptxWithChartDataTableFontFamily(fontFamily: null);
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.TextStyle.Should().NotBeNull();
        chart.DataTable.TextStyle!.FontFamily.Should().BeNull();

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);

        using var savedRead = new MemoryStream(saved.ToArray());
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        reloadedDataTable!.TextStyle.Should().NotBeNull();
        reloadedDataTable.TextStyle!.FontFamily.Should().BeNull();
    }

    private static MemoryStream BuildPptxWithChartDataTableFontFamily(string? fontFamily)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["East", "West"]);
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Font family chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var defRPr = new XElement(DrawingNs + "defRPr",
                new XAttribute("sz", "875"));
            if (fontFamily is not null)
                defRPr.Add(new XElement(DrawingNs + "latin", new XAttribute("typeface", fontFamily)));

            chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Add(new XElement(ChartNs + "dTable",
                    new XElement(ChartNs + "showHorzBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showVertBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showOutline", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showKeys", new XAttribute("val", "0")),
                    new XElement(ChartNs + "txPr",
                        new XElement(DrawingNs + "bodyPr"),
                        new XElement(DrawingNs + "lstStyle"),
                        new XElement(DrawingNs + "p",
                            new XElement(DrawingNs + "pPr", defRPr),
                            new XElement(DrawingNs + "endParaRPr")))));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);
        }

        package.Position = 0;
        return package;
    }

    [Fact]
    public void ReadWriteRead_ChartDataTableGradientBorderOutline_IsPreservedNotReplacedByDefaultGray()
    {
        // JA1: a c:dTable/c:spPr/a:ln with an a:gradFill child (gradient border) must survive
        // read -> write -> read as ShapeOutline.GradientVisible, not be discarded and replaced
        // by the renderer/writer default gray solid outline.
        using var source = BuildPptxWithGradientChartDataTableBorder();
        var loaded = PptxPackageReader.Read(source);

        var chart = loaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!;
        chart.DataTable.Should().NotBeNull();
        var gradientOutline = chart.DataTable!.BorderOutline.Should().BeOfType<ShapeOutline.GradientVisible>().Subject;
        gradientOutline.WidthPt.Should().BeApproximately(1.0, 0.001);
        gradientOutline.Gradient.Stops.Select(s => s.Color.Resolved).Should()
            .Equal(new SrgbColor(0xFF, 0x00, 0x00), new SrgbColor(0x00, 0x00, 0xFF));

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            var savedChartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            var savedDataTable = savedChartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Element(ChartNs + "dTable");
            savedDataTable.Should().NotBeNull();
            var savedLine = savedDataTable!.Element(ChartNs + "spPr")!.Element(DrawingNs + "ln")!;
            savedLine.Element(DrawingNs + "gradFill").Should().NotBeNull(
                "the gradient border must round-trip as a:gradFill, not be collapsed to a:solidFill");
            savedLine.Element(DrawingNs + "solidFill").Should().BeNull(
                "a gradient border must not be silently replaced by a solid default gray fill");
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        var reloadedDataTable = reloaded.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Chart).Chart!.DataTable;
        reloadedDataTable.Should().NotBeNull();
        var reloadedGradient = reloadedDataTable!.BorderOutline.Should().BeOfType<ShapeOutline.GradientVisible>().Subject;
        reloadedGradient.WidthPt.Should().BeApproximately(1.0, 0.001);
        reloadedGradient.Gradient.Stops.Select(s => s.Color.Resolved).Should()
            .Equal(new SrgbColor(0xFF, 0x00, 0x00), new SrgbColor(0x00, 0x00, 0xFF));
    }

    private static MemoryStream BuildPptxWithGradientChartDataTableBorder()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["East", "West"]);
        var series = new ChartSeries { Name = "Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Gradient border chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Add(new XElement(ChartNs + "dTable",
                    new XElement(ChartNs + "showHorzBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showVertBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showOutline", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showKeys", new XAttribute("val", "0")),
                    new XElement(ChartNs + "spPr",
                        new XElement(DrawingNs + "ln",
                            new XAttribute("w", DrawingMlUnits.PointsToEmu(1.0)),
                            new XElement(DrawingNs + "gradFill",
                                new XElement(DrawingNs + "gsLst",
                                    new XElement(DrawingNs + "gs",
                                        new XAttribute("pos", "0"),
                                        new XElement(DrawingNs + "srgbClr", new XAttribute("val", "FF0000"))),
                                    new XElement(DrawingNs + "gs",
                                        new XAttribute("pos", "100000"),
                                        new XElement(DrawingNs + "srgbClr", new XAttribute("val", "0000FF")))),
                                new XElement(DrawingNs + "lin",
                                    new XAttribute("ang", "5400000"),
                                    new XAttribute("scaled", "0")))))));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithUnmodeledPackageData()
    {
        var presentation = Presentation.CreateEmpty();
        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteXml(
                archive,
                OpcPackageProperties.ExtendedPropertiesZipEntry,
                OpcDocumentProperties.BuildExtendedPropertiesDocument(
                    new ExtendedDocumentProperties(Application: "FreeP retention harness"),
                    includeXmlDeclaration: true));

            var customProperties = OpcCustomDocumentProperties.Create();
            customProperties.SetString("RetentionMarker", "retain-me");
            WriteXml(
                archive,
                OpcPackageProperties.CustomPropertiesZipEntry,
                customProperties.ToXDocument(includeXmlDeclaration: true));
            WriteText(archive, "customXml/item1.xml", """<bag xmlns="urn:freep:test">retain-me</bag>""");
            WriteText(archive, "customXml/itemProps1.xml",
                """<ds:datastoreItem ds:itemID="{11111111-1111-1111-1111-111111111111}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>""");
            WriteText(archive, "customXml/payload.freex", "freex-payload");
            WriteBytes(archive, "ppt/customData/viewState.bin", new byte[] { 0x46, 0x50, 0x52, 0x01 });

            var rootRels = LoadXml(archive, "_rels/.rels");
            AddRelationship(
                rootRels,
                "rIdAppProps",
                OpcPackageProperties.ExtendedPropertiesRelationshipType,
                OpcPackageProperties.ExtendedPropertiesZipEntry);
            AddRelationship(
                rootRels,
                "rIdCustomProps",
                OpcPackageProperties.CustomPropertiesRelationshipType,
                OpcPackageProperties.CustomPropertiesZipEntry);
            AddRelationship(rootRels, "rIdCustomXml", CustomXmlRelType, "customXml/item1.xml");
            AddRelationship(rootRels, "rIdExternalReview", ExternalReviewRelType, "https://example.com/freep-review", external: true);
            AddRelationship(rootRels, "rIdSlideMirror", UnknownSlideMirrorRelType, "ppt/slides/slide1.xml");
            WriteXml(archive, "_rels/.rels", rootRels);

            var itemRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships",
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                        new XAttribute("Target", "itemProps1.xml"))));
            WriteXml(archive, "customXml/_rels/item1.xml.rels", itemRels);

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(presRels, "rIdUnknownView", UnknownViewRelType, "customData/viewState.bin");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(
                contentTypes,
                OpcPackageProperties.ExtendedPropertiesPartName,
                OpcPackageProperties.ExtendedPropertiesContentType);
            AddOverride(
                contentTypes,
                OpcPackageProperties.CustomPropertiesPartName,
                OpcPackageProperties.CustomPropertiesContentType);
            AddOverride(contentTypes, "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            AddOverride(contentTypes, "/ppt/customData/viewState.bin",
                "application/vnd.example.freep.viewstate");
            AddDefault(contentTypes, "freex", "application/vnd.example.freep.payload");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithChartWorkbookAndUnrelatedPackageData()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Old East", "Old West"]);
        var series = new ChartSeries { Name = "Old Actual" };
        series.Values.AddRange([10, 20]);
        chart.Series.Add(series);
        slide.Shapes.Add(new SlideShape
        {
            Id = 101,
            Name = "Workbook chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 3657600,
            ExtentCyEmu = 2743200,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var chartXml = LoadXml(archive, "ppt/charts/chart1.xml");
            chartXml.Root!.Element(ChartNs + "externalData")?.Remove();
            chartXml.Root.Add(new XElement(ChartNs + "externalData",
                new XAttribute(RelsDocNs + "id", "rIdSourceWorkbook"),
                new XElement(ChartNs + "autoUpdate", new XAttribute("val", "0"))));
            chartXml.Root!
                .Element(ChartNs + "chart")!
                .Element(ChartNs + "plotArea")!
                .Add(new XElement(ChartNs + "dTable",
                    new XElement(ChartNs + "showHorzBorder", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showVertBorder", new XAttribute("val", "0")),
                    new XElement(ChartNs + "showOutline", new XAttribute("val", "1")),
                    new XElement(ChartNs + "showKeys", new XAttribute("val", "1")),
                    new XElement(ChartNs + "spPr",
                        new XElement(DrawingNs + "ln",
                            new XAttribute("w", DrawingMlUnits.PointsToEmu(1.25)),
                            new XElement(DrawingNs + "solidFill",
                                new XElement(DrawingNs + "srgbClr", new XAttribute("val", "123456"))))),
                    new XElement(ChartNs + "txPr",
                        new XElement(DrawingNs + "bodyPr"),
                        new XElement(DrawingNs + "lstStyle"),
                        new XElement(DrawingNs + "p",
                            new XElement(DrawingNs + "pPr",
                                new XElement(DrawingNs + "defRPr",
                                    new XAttribute("sz", "875"),
                                    new XAttribute("b", "1"),
                                    new XAttribute("i", "1"),
                                    new XElement(DrawingNs + "solidFill",
                                        new XElement(DrawingNs + "srgbClr", new XAttribute("val", "224466"))))),
                            new XElement(DrawingNs + "endParaRPr")))));
            WriteXml(archive, "ppt/charts/chart1.xml", chartXml);

            var chartRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships"));
            AddRelationship(
                chartRels,
                "rIdSourceWorkbook",
                PackageRelType,
                "../embeddings/sourceWorkbook.xlsx");
            WriteXml(archive, "ppt/charts/_rels/chart1.xml.rels", chartRels);
            WriteBytes(archive, "ppt/embeddings/sourceWorkbook.xlsx", Encoding.UTF8.GetBytes("stale workbook bytes"));

            WriteText(
                archive,
                "customXml/chartWorkbookPayload.xml",
                """<payload xmlns="urn:freep:test">unrelated-retain-me</payload>""");
            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(
                presRels,
                "rIdChartWorkbookPayload",
                CustomXmlRelType,
                "../customXml/chartWorkbookPayload.xml");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(
                contentTypes,
                "/ppt/embeddings/sourceWorkbook.xlsx",
                SpreadsheetWorkbookContentType);
            AddOverride(
                contentTypes,
                "/customXml/chartWorkbookPayload.xml",
                "application/vnd.example.freep.chart-workbook-payload+xml");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithPresentationScopedCustomXml()
    {
        var presentation = Presentation.CreateEmpty();
        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteText(archive, "customXml/item2.xml",
                """<bag xmlns="urn:freep:test">presentation-scoped-retain-me</bag>""");
            WriteText(archive, "customXml/itemProps2.xml",
                """<ds:datastoreItem ds:itemID="{22222222-2222-2222-2222-222222222222}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>""");
            WriteText(archive, "customXml/item2.freexmeta", "presentation custom xml payload");

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(presRels, "rIdPresentationCustomXml", CustomXmlRelType, "../customXml/item2.xml");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var itemRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships",
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rIdProps"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                        new XAttribute("Target", "itemProps2.xml")),
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rIdPayload"),
                        new XAttribute("Type", "http://example.com/freep/relationships/customXmlPayload"),
                        new XAttribute("Target", "item2.freexmeta"))));
            WriteXml(archive, "customXml/_rels/item2.xml.rels", itemRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/customXml/itemProps2.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            AddDefault(contentTypes, "freexmeta", "application/vnd.example.freep.customxml-payload");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream BuildPptxWithViewAndPrintSettings()
    {
        var presentation = Presentation.CreateEmpty();
        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var presProps = XDocument.Parse("""
                <p:presentationPr xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:prnPr prnWhat="handouts3" clrMode="clr" hiddenSlides="0" frameSlides="1"/>
                  <p:extLst>
                    <p:ext uri="{11111111-2222-3333-4444-555555555555}">
                      <freep:retention xmlns:freep="urn:freep:test">freep-print-retain</freep:retention>
                    </p:ext>
                  </p:extLst>
                </p:presentationPr>
                """);
            WriteXml(archive, "ppt/presProps.xml", presProps);

            var viewProps = XDocument.Parse("""
                <p:viewPr xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" lastView="sldSorterView">
                  <p:normalViewPr showOutlineIcons="1"/>
                  <p:gridSpacing cx="12700" cy="12700"/>
                  <p:extLst>
                    <p:ext uri="{66666666-7777-8888-9999-AAAAAAAAAAAA}">
                      <freep:retention xmlns:freep="urn:freep:test">freep-view-retain</freep:retention>
                    </p:ext>
                  </p:extLst>
                </p:viewPr>
                """);
            WriteXml(archive, "ppt/viewProps.xml", viewProps);

            WriteBytes(archive, "ppt/printerSettings/printerSettings1.bin", new byte[] { 0x46, 0x50, 0x50, 0x01 });

            var presPropsRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships"));
            AddRelationship(
                presPropsRels,
                "rIdPrinterSettings",
                PrinterSettingsRelType,
                "printerSettings/printerSettings1.bin");
            WriteXml(archive, "ppt/_rels/presProps.xml.rels", presPropsRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(
                contentTypes,
                "/ppt/printerSettings/printerSettings1.bin",
                "application/vnd.openxmlformats-officedocument.presentationml.printerSettings");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static void AssertCommentsNotesPackageParts(ZipArchive archive)
    {
        archive.GetEntry("ppt/notesSlides/notesSlide1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/notesSlides/notesSlide2.xml").Should().NotBeNull();
        archive.GetEntry("ppt/notesMasters/notesMaster1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/comments/comment1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/comments/comment2.xml").Should().NotBeNull();
        archive.GetEntry("ppt/commentAuthors.xml").Should().NotBeNull();

        ReadText(archive, "ppt/notesSlides/notesSlide1.xml")
            .Should().Contain("Speaker note: introduce the review workflow.")
            .And.Contain("package save");
        ReadText(archive, "ppt/notesSlides/notesSlide2.xml")
            .Should().Contain("Speaker note: summarize the comment decisions.");
        ReadText(archive, "ppt/comments/comment1.xml")
            .Should().Contain("Confirm the title before publishing.");
        ReadText(archive, "ppt/comments/comment2.xml")
            .Should().Contain("Add a data source footnote.")
            .And.Contain("Keep this callout for presenter notes.");
        ReadText(archive, "ppt/commentAuthors.xml")
            .Should().Contain("Alice Reviewer")
            .And.Contain("Bob Reviewer");

        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        Override(contentTypes, "/ppt/notesSlides/notesSlide1.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/notesSlides/notesSlide2.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/notesMasters/notesMaster1.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/comments/comment1.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.comments+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/comments/comment2.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.comments+xml").Should().NotBeNull();
        Override(contentTypes, "/ppt/commentAuthors.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml").Should().NotBeNull();

        var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
        Relationship(presRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster",
            "notesMasters/notesMaster1.xml").Should().NotBeNull();
        Relationship(presRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors",
            "commentAuthors.xml").Should().NotBeNull();

        AssertSlideCommentsNotesRelationships(archive, slideIndex: 1);
        AssertSlideCommentsNotesRelationships(archive, slideIndex: 2);
    }

    private static void AssertSlideCommentsNotesRelationships(ZipArchive archive, int slideIndex)
    {
        var slideRels = LoadXml(archive, $"ppt/slides/_rels/slide{slideIndex}.xml.rels");
        Relationship(slideRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide",
            $"../notesSlides/notesSlide{slideIndex}.xml").Should().NotBeNull();
        Relationship(slideRels,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
            $"../comments/comment{slideIndex}.xml").Should().NotBeNull();
    }

    private static string TextBodyText(TextBody? body) =>
        body is null
            ? string.Empty
            : string.Concat(body.Paragraphs.SelectMany(paragraph => paragraph.Runs.Select(run => run.Text)));

    private static string AddModeledShapeEdit(Presentation presentation, string deckName)
    {
        var slide = presentation.Slides[0];
        var shapeId = slide.Shapes.Select(shape => shape.Id).DefaultIfEmpty(0u).Max() + 1u;
        var shapeName = $"Semantic corpus edit - {Path.GetFileNameWithoutExtension(deckName)}";
        slide.Shapes.Add(new SlideShape
        {
            Id = shapeId,
            Name = shapeName,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 457200,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
        });

        return shapeName;
    }

    private static void AssertPreservedPackageEntries(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName)
    {
        foreach (var sourceEntry in sourceArchive.Entries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.FullName) && !entry.FullName.EndsWith('/'))
                     .Where(entry => !IsWriterOwnedPart(entry.FullName)))
        {
            var savedEntry = savedArchive.GetEntry(sourceEntry.FullName);
            savedEntry.Should().NotBeNull($"{deckName} should retain package entry {sourceEntry.FullName}");
            ReadBytes(savedArchive, sourceEntry.FullName).Should().Equal(
                ReadBytes(sourceArchive, sourceEntry.FullName),
                $"{deckName} should byte-preserve non-writer-owned package entry {sourceEntry.FullName}");
        }
    }

    private static void AssertPreservedContentTypes(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName)
    {
        var sourceTypes = LoadXml(sourceArchive, "[Content_Types].xml");
        var savedTypes = LoadXml(savedArchive, "[Content_Types].xml");

        var savedDefaults = savedTypes.Root!
            .Elements(ContentTypesNs + "Default")
            .ToDictionary(
                element => element.Attribute("Extension")!.Value,
                element => element.Attribute("ContentType")!.Value,
                StringComparer.OrdinalIgnoreCase);
        foreach (var sourceDefault in sourceTypes.Root!.Elements(ContentTypesNs + "Default"))
        {
            var extension = sourceDefault.Attribute("Extension")?.Value;
            var contentType = sourceDefault.Attribute("ContentType")?.Value;
            if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(contentType))
                continue;

            savedDefaults.Should().ContainKey(extension, $"{deckName} should retain Default content type for .{extension}");
            savedDefaults[extension].Should().Be(contentType, $"{deckName} should retain Default content type for .{extension}");
        }

        var savedOverrides = savedTypes.Root!
            .Elements(ContentTypesNs + "Override")
            .ToDictionary(
                element => NormalizePartName(element.Attribute("PartName")!.Value),
                element => element.Attribute("ContentType")!.Value,
                StringComparer.OrdinalIgnoreCase);
        foreach (var sourceOverride in sourceTypes.Root!.Elements(ContentTypesNs + "Override"))
        {
            var partName = sourceOverride.Attribute("PartName")?.Value;
            var contentType = sourceOverride.Attribute("ContentType")?.Value;
            if (string.IsNullOrWhiteSpace(partName) ||
                string.IsNullOrWhiteSpace(contentType) ||
                IsWriterOwnedPart(partName))
            {
                continue;
            }

            var normalizedPartName = NormalizePartName(partName);
            savedOverrides.Should().ContainKey(normalizedPartName,
                $"{deckName} should retain Override content type for {normalizedPartName}");
            savedOverrides[normalizedPartName].Should().Be(contentType,
                $"{deckName} should retain Override content type for {normalizedPartName}");
        }
    }

    private static void AssertPreservedRelationships(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName)
    {
        foreach (var sourceRelsEntry in sourceArchive.Entries
                     .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var sourcePartPath = SourcePartPathFromRelationshipPath(sourceRelsEntry.FullName);
            var retainedRelationships = OpcRelationships.Load(sourceArchive, sourceRelsEntry.FullName)
                .Where(relationship => !IsWriterOwnedRelationship(
                    sourcePartPath,
                    relationship.Type,
                    relationship.Target,
                    relationship.IsExternal))
                .ToArray();
            if (retainedRelationships.Length == 0)
                continue;

            savedArchive.GetEntry(sourceRelsEntry.FullName).Should().NotBeNull(
                $"{deckName} should retain relationship part {sourceRelsEntry.FullName}");
            var savedRelationships = OpcRelationships.Load(savedArchive, sourceRelsEntry.FullName);
            foreach (var relationship in retainedRelationships)
            {
                savedRelationships.Should().Contain(saved => RelationshipMatches(saved, relationship),
                    $"{deckName} should retain {sourceRelsEntry.FullName} relationship {relationship.Type} -> {relationship.Target}");
            }
        }
    }

    private static void AssertFeaturePackageEntriesStillPresent(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName,
        string[] featurePartPrefixes)
    {
        var sourceFeatureEntries = FeatureDataEntries(sourceArchive, featurePartPrefixes)
            .Select(entry => entry.FullName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        sourceFeatureEntries.Should().NotBeEmpty($"{deckName} should exercise the requested feature package parts");

        var savedFeatureEntries = FeatureDataEntries(savedArchive, featurePartPrefixes)
            .Select(entry => entry.FullName)
            .ToArray();
        savedFeatureEntries.Should().HaveCountGreaterThanOrEqualTo(sourceFeatureEntries.Length,
            $"{deckName} should keep the requested feature package part family after a modeled edit");

        var sourceTypes = LoadXml(sourceArchive, "[Content_Types].xml");
        var savedTypes = LoadXml(savedArchive, "[Content_Types].xml");
        var savedFeatureContentTypes = savedFeatureEntries
            .Select(entry => ContentTypeForPart(savedTypes, entry))
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .ToLookup(contentType => contentType!, StringComparer.OrdinalIgnoreCase);
        var sourceFeatureContentTypes = sourceFeatureEntries
            .Select(entry => ContentTypeForPart(sourceTypes, entry))
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .GroupBy(contentType => contentType!, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceContentType in sourceFeatureContentTypes)
        {
            savedFeatureContentTypes[sourceContentType.Key].Should().HaveCountGreaterThanOrEqualTo(
                sourceContentType.Count(),
                $"{deckName} should keep {sourceContentType.Count()} high-risk feature part(s) with content type {sourceContentType.Key}");
        }
    }

    private static IEnumerable<ZipArchiveEntry> FeatureDataEntries(
        ZipArchive archive,
        string[] featurePartPrefixes) =>
        archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FullName) && !entry.FullName.EndsWith('/'))
            .Where(entry => IsFeaturePart(entry.FullName, featurePartPrefixes))
            .Where(entry => !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            .Where(entry => !NormalizePartName(entry.FullName).Contains("/_rels/", StringComparison.OrdinalIgnoreCase));

    private static void AssertFeatureContentTypesStillPresent(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName,
        string[] featurePartPrefixes)
    {
        var sourceTypes = LoadXml(sourceArchive, "[Content_Types].xml");
        var savedTypes = LoadXml(savedArchive, "[Content_Types].xml");
        var sourceFeatureEntries = FeatureDataEntries(sourceArchive, featurePartPrefixes)
            .Select(entry => entry.FullName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var savedContentTypesByFamily = FeatureDataEntries(savedArchive, featurePartPrefixes)
            .Select(entry => ContentTypeForPart(savedTypes, entry.FullName))
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .ToLookup(contentType => contentType!, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceEntry in sourceFeatureEntries)
        {
            var sourceContentType = ContentTypeForPart(sourceTypes, sourceEntry);
            sourceContentType.Should().NotBeNull($"{deckName} should have a source content type for {sourceEntry}");
            savedContentTypesByFamily[sourceContentType!].Should().NotBeEmpty(
                $"{deckName} should keep content type {sourceContentType} for the {FeatureFamilyForPart(sourceEntry, featurePartPrefixes)} package family after a modeled edit");
        }
    }

    private static void AssertFeatureRelationshipsStillPresent(
        ZipArchive sourceArchive,
        ZipArchive savedArchive,
        string deckName,
        string[] featurePartPrefixes)
    {
        foreach (var sourceRelsEntry in sourceArchive.Entries
                     .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var sourcePartPath = SourcePartPathFromRelationshipPath(sourceRelsEntry.FullName);
            var featureRelationships = OpcRelationships.Load(sourceArchive, sourceRelsEntry.FullName)
                .Where(relationship => IsFeatureRelationship(sourcePartPath, relationship, featurePartPrefixes))
                .ToArray();
            if (featureRelationships.Length == 0)
                continue;

            savedArchive.GetEntry(sourceRelsEntry.FullName).Should().NotBeNull(
                $"{deckName} should keep relationship part {sourceRelsEntry.FullName} after a modeled edit");
            var savedFeatureRelationships = OpcRelationships.Load(savedArchive, sourceRelsEntry.FullName)
                .Where(relationship => IsFeatureRelationship(sourcePartPath, relationship, featurePartPrefixes))
                .GroupBy(relationship => relationship.Type, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var sourceGroup in featureRelationships.GroupBy(relationship => relationship.Type, StringComparer.OrdinalIgnoreCase))
            {
                savedFeatureRelationships.Should().ContainKey(sourceGroup.Key,
                    $"{deckName} should keep high-risk feature relationship type {sourceGroup.Key} in {sourceRelsEntry.FullName}");
                savedFeatureRelationships[sourceGroup.Key].Should().BeGreaterThanOrEqualTo(sourceGroup.Count(),
                    $"{deckName} should keep high-risk feature relationship count for {sourceGroup.Key} in {sourceRelsEntry.FullName}");
            }
        }
    }

    private static string FindCorpusDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "FreeP.RenderCompare", "corpus");
            if (Directory.Exists(candidate) &&
                ExpectedCorpusDeckNames.All(name => File.Exists(Path.Combine(candidate, name))))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate tools/FreeP.RenderCompare/corpus with all tracked PPTX decks.");
    }

    private static bool RelationshipMatches(OpcRelationship actual, OpcRelationship expected) =>
        string.Equals(actual.Type, expected.Type, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(actual.Target, expected.Target, StringComparison.Ordinal) &&
        actual.IsExternal == expected.IsExternal;

    private static bool IsWriterOwnedRelationship(string sourcePartPath, string type, string target, bool external)
    {
        if (WriterOwnedRelationshipTypes.Contains(type))
            return true;

        if (external || string.IsNullOrWhiteSpace(target))
            return false;

        var sourceDirectory = string.IsNullOrWhiteSpace(sourcePartPath)
            ? string.Empty
            : OpcPathHelper.GetDirectoryName(sourcePartPath);
        var targetPath = OpcPathHelper.ResolveRelativeZipPath(sourceDirectory, target);
        return IsWriterOwnedPart(targetPath);
    }

    private static bool IsWriterOwnedPart(string partName)
    {
        var normalized = NormalizePartName(partName);
        return WriterOwnedPackagePartPaths.Any(path => string.Equals(
                   NormalizePartName(path),
                   normalized,
                   StringComparison.OrdinalIgnoreCase)) ||
               WriterOwnedPackagePartPrefixes.Any(prefix =>
                   normalized.StartsWith(NormalizePartName(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static string SourcePartPathFromRelationshipPath(string relsPath)
    {
        var normalized = relsPath.Replace('\\', '/').TrimStart('/');
        if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        const string marker = "/_rels/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return normalized[..markerIndex] + "/" + normalized[(markerIndex + marker.Length)..^".rels".Length];
    }

    private static string NormalizePartName(string partName) =>
        OpcPathHelper.ToZipEntryPath(partName);

    private static bool IsFeatureRelationship(
        string sourcePartPath,
        OpcRelationship relationship,
        string[] featurePartPrefixes)
    {
        if (relationship.IsExternal || string.IsNullOrWhiteSpace(relationship.Target))
            return false;

        var sourceDirectory = string.IsNullOrWhiteSpace(sourcePartPath)
            ? string.Empty
            : OpcPathHelper.GetDirectoryName(sourcePartPath);
        var targetPath = OpcPathHelper.ResolveRelativeZipPath(sourceDirectory, relationship.Target);
        return IsFeaturePart(targetPath, featurePartPrefixes);
    }

    private static bool IsFeaturePart(string partName, string[] featurePartPrefixes)
    {
        var normalized = NormalizePartName(partName);
        return featurePartPrefixes.Any(prefix =>
            normalized.StartsWith(NormalizePartName(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static string FeatureFamilyForPart(string partName, string[] featurePartPrefixes)
    {
        var normalized = NormalizePartName(partName);
        return featurePartPrefixes.First(prefix =>
            normalized.StartsWith(NormalizePartName(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static string? ContentTypeForPart(XDocument contentTypes, string partName)
    {
        var normalizedPartName = "/" + NormalizePartName(partName);
        var overrideType = contentTypes.Root?
            .Elements(ContentTypesNs + "Override")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("PartName")?.Value,
                normalizedPartName,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;
        if (!string.IsNullOrWhiteSpace(overrideType))
            return overrideType;

        var extension = Path.GetExtension(partName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return contentTypes.Root?
            .Elements(ContentTypesNs + "Default")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Extension")?.Value,
                extension,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;
    }

    private static readonly XNamespace RelsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelsDocNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ChartNs =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static XElement? Relationship(XDocument doc, string type, string target) =>
        doc.Root?.Elements(RelsNs + "Relationship").FirstOrDefault(r =>
            r.Attribute("Type")?.Value == type &&
            r.Attribute("Target")?.Value == target);

    private static XElement? Override(XDocument doc, string partName, string contentType) =>
        doc.Root?.Elements(ContentTypesNs + "Override").FirstOrDefault(o =>
            o.Attribute("PartName")?.Value == partName &&
            o.Attribute("ContentType")?.Value == contentType);

    private static XElement? Default(XDocument doc, string extension, string contentType) =>
        doc.Root?.Elements(ContentTypesNs + "Default").FirstOrDefault(o =>
            o.Attribute("Extension")?.Value == extension &&
            o.Attribute("ContentType")?.Value == contentType);

    private static void AddRelationship(XDocument doc, string id, string type, string target, bool external = false)
    {
        var relationship = new XElement(RelsNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));
        if (external)
            relationship.Add(new XAttribute("TargetMode", "External"));

        doc.Root!.Add(relationship);
    }

    private static void AddOverride(XDocument doc, string partName, string contentType)
    {
        doc.Root!.Add(new XElement(ContentTypesNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static void AddDefault(XDocument doc, string extension, string contentType)
    {
        doc.Root!.Add(new XElement(ContentTypesNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new FileNotFoundException(path);
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadText(ZipArchive archive, string path) =>
        Encoding.UTF8.GetString(ReadBytes(archive, path));

    private static byte[] ReadBytes(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new FileNotFoundException(path);
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteText(ZipArchive archive, string path, string text) =>
        WriteBytes(archive, path, Encoding.UTF8.GetBytes(text));

    private static void WriteXml(ZipArchive archive, string path, XDocument doc)
    {
        var entry = archive.GetEntry(path);
        entry?.Delete();
        entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        doc.Save(stream);
    }

    private static void WriteBytes(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.GetEntry(path);
        entry?.Delete();
        entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }
}
