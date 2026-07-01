using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Opc;

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
    private const string UnknownSlideMirrorRelType =
        "http://example.com/freep/relationships/slideMirror";

    public static IEnumerable<object[]> CorpusDecks() =>
        ExpectedCorpusDeckNames.Select(name => new object[] { name });

    [Fact]
    public void RenderCompareCorpus_TracksExpectedTwentyDecks()
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

    private static readonly XNamespace RelsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
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
