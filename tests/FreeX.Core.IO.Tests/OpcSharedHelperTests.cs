using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

public sealed class OpcSharedHelperTests
{
    [Theory]
    [InlineData("ppt/slides/slide1.xml", "ppt/slides/_rels/slide1.xml.rels")]
    [InlineData("/word/document.xml", "word/_rels/document.xml.rels")]
    [InlineData("workbook.xml", "_rels/workbook.xml.rels")]
    public void GetRelationshipPartPath_ReturnsSiblingRelsZipEntry(string partPath, string expected)
    {
        OpcPathHelper.GetRelationshipPartPath(partPath).Should().Be(expected);
    }

    [Theory]
    [InlineData("ppt/slides", "../media/image1.png", "ppt/media/image1.png")]
    [InlineData("ppt/slides", "/docProps/core.xml", "docProps/core.xml")]
    [InlineData("xl/worksheets", "../drawings/./drawing1.xml", "xl/drawings/drawing1.xml")]
    public void ResolveRelativeZipPath_CollapsesDotSegments(string baseDirectory, string target, string expected)
    {
        OpcPathHelper.ResolveRelativeZipPath(baseDirectory, target).Should().Be(expected);
    }

    [Theory]
    [InlineData("xl/worksheets", "xl/media/image 1.png", "../media/image 1.png")]
    [InlineData("xl/drawings", "xl/charts/chart1.xml", "../charts/chart1.xml")]
    [InlineData("xl/pivotCache", "xl/pivotCache/pivotCacheRecords1.xml", "pivotCacheRecords1.xml")]
    public void GetRelativeZipPath_ReturnsOpcRelationshipTargets(
        string baseDirectory,
        string targetPath,
        string expected)
    {
        OpcPathHelper.GetRelativeZipPath(baseDirectory, targetPath).Should().Be(expected);
    }

    [Theory]
    [InlineData("../media/image%201.png", "../media/image 1.png")]
    [InlineData("../media/image%2F1.png", "../media/image%2F1.png")]
    [InlineData("%2E%2E/media/image.png", "%2E%2E/media/image.png")]
    public void UnescapeRelationshipPathSegments_PreservesOpcPathControlSegments(string encoded, string unescaped)
    {
        OpcPathHelper.UnescapeRelationshipPathSegments(encoded).Should().Be(unescaped);
    }

    [Theory]
    [InlineData("../media/image 1.png", "../media/image%201.png")]
    [InlineData("../media/image#1?.png", "../media/image%231%3F.png")]
    public void EscapeRelationshipPathSegments_EscapesUnsafeSegmentCharacters(string value, string expected)
    {
        OpcPathHelper.EscapeRelationshipPathSegments(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("/word/charts", "../media/image1.png", "/word/media/image1.png")]
    [InlineData("/word/charts", "/docProps/core.xml", "/docProps/core.xml")]
    [InlineData("/", "../escaped.xml", null)]
    public void ResolveAbsolutePartName_PreservesAbsolutePartNameConvention(
        string baseFolder,
        string target,
        string? expected)
    {
        OpcPathHelper.ResolveAbsolutePartName(baseFolder, target).Should().Be(expected);
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData("ogg", "audio/ogg")]
    [InlineData("aac", "audio/aac")]
    [InlineData("tif", "image/tiff")]
    public void TryGetDefaultContentType_CoversSharedMediaDefaults(string extension, string expected)
    {
        OpcMediaTypes.TryGetDefaultContentType(extension, out var contentType).Should().BeTrue();
        contentType.Should().Be(expected);
    }

    [Theory]
    [InlineData("ppt/media/image1.svg", "image/svg+xml")]
    [InlineData("ppt/media/photo.tiff", "image/tiff")]
    [InlineData("ppt/media/fallback.unknown", "image/png")]
    public void GetDrawingMediaContentType_MatchesPresentationImageDefaults(string path, string expected)
    {
        OpcMediaTypes.GetDrawingMediaContentType(path).Should().Be(expected);
    }

    [Fact]
    public void ContentTypeHelpers_EnsureRemoveAndPruneOverrides()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, OpcMediaTypes.ContentTypesPath, """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml" />
                  <Override PartName="/xl/stale.xml" ContentType="application/xml" />
                </Types>
                """);
            WriteText(archive, "xl/live.xml", "<live />");
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            OpcMediaTypes.EnsureDefaultContentType(archive, ".rels", OpcMediaTypes.RelationshipsContentType)
                .Should().BeTrue();
            OpcMediaTypes.EnsureOverrideContentType(
                    archive,
                    "xl/live.xml",
                    "application/vnd.example.live+xml")
                .Should()
                .BeTrue();
            OpcMediaTypes.RemoveOverrideContentTypes(archive, ["/xl/stale.xml"]).Should().BeTrue();
            OpcMediaTypes.PruneMissingOverrideContentTypes(archive).Should().BeFalse();
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        var contentTypes = OpcXml.LoadXml(readArchive.GetEntry(OpcMediaTypes.ContentTypesPath)!);
        contentTypes.Root!
            .Elements(OpcMediaTypes.ContentTypesNamespace + "Default")
            .Should()
            .Contain(element => (string?)element.Attribute("Extension") == "rels");
        contentTypes.Root!
            .Elements(OpcMediaTypes.ContentTypesNamespace + "Override")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("PartName") == "/xl/live.xml" &&
                (string?)element.Attribute("ContentType") == "application/vnd.example.live+xml");
    }

    [Theory]
    [InlineData("ppt/media/movie.m4v", "video/mp4")]
    [InlineData("ppt/media/clip.mov", "video/quicktime")]
    [InlineData("ppt/media/sound.wma", "audio/x-ms-wma")]
    [InlineData("ppt/media/unknown.bin", "video/mp4")]
    public void GetAudioVideoContentType_MatchesPresentationMediaDefaults(string path, string expected)
    {
        OpcMediaTypes.GetAudioVideoContentType(path).Should().Be(expected);
    }

    [Fact]
    public void RelationshipDocument_AddUnique_PreservesRelationshipAndAvoidsIdCollisions()
    {
        var relationships = new OpcRelationshipDocument();
        relationships.Add("rId1", "type/known", "target.xml");
        relationships.AddUnique("rId1", "type/preserved", "custom.xml");
        relationships.AddUnique("rId2", "type/preserved", "custom.xml");

        var xml = relationships.ToXDocument();
        var entries = xml.Root!.Elements(OpcRelationships.Namespace + "Relationship").ToList();

        entries.Should().HaveCount(2);
        entries[1].Attribute("Id")!.Value.Should().Be("rIdPreserved1");
        entries[1].Attribute("Type")!.Value.Should().Be("type/preserved");
        entries[1].Attribute("Target")!.Value.Should().Be("custom.xml");
    }

    [Fact]
    public void PackageRetentionClassifier_ClassifiesRegeneratedPartsAndRelationships()
    {
        var classifier = new OpcPackageRetentionClassifier(
            regeneratedPartPaths:
            [
                "[Content_Types].xml",
                "ppt/presentation.xml",
            ],
            regeneratedPartPathPrefixes:
            [
                "ppt/slides",
            ],
            regeneratedRelationshipTypes:
            [
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide",
            ]);

        classifier.IsRegeneratedPart("/ppt/slides/slide1.xml").Should().BeTrue();
        classifier.IsRegeneratedPart("customXml/item1.xml").Should().BeFalse();
        classifier.IsRegeneratedRelationship(
                "ppt/presentation.xml",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide",
                "customData/viewState.bin",
                external: false)
            .Should()
            .BeTrue();
        classifier.IsRegeneratedRelationship(
                "ppt/presentation.xml",
                "http://example.com/relationships/slide-shadow",
                "slides/slide1.xml",
                external: false)
            .Should()
            .BeTrue();
        classifier.IsRegeneratedRelationship(
                "ppt/presentation.xml",
                "http://example.com/relationships/external-slide",
                "ppt/slides/slide1.xml",
                external: true)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void LoadByIdAndTargetMap_ReadSharedRelationshipParts()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
                  <Relationship Id="rIdLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.test/" TargetMode="External"/>
                </Relationships>
                """);
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var byId = OpcRelationships.LoadById(readArchive, "word/_rels/document.xml.rels");
        var internalTargets = OpcRelationships.LoadTargetMap(
            readArchive,
            "word/_rels/document.xml.rels",
            relationship => "word/" + relationship.Target.TrimStart('/'),
            relationship => !relationship.IsExternal);

        byId["rIdLink"].IsExternal.Should().BeTrue();
        internalTargets.Should().Contain("rIdImage", "word/media/image1.png");
        internalTargets.Should().NotContainKey("rIdLink");
    }

    [Fact]
    public void RelationshipTargetHelpers_ReadCompactTargetsAndTypeMaps()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, "ppt/_rels/presentation.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="type/slide" Target="slides/slide1.xml"/>
                  <Relationship Id="rId2" Type="type/theme" Target="theme/theme1.xml"/>
                </Relationships>
                """);
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var targets = OpcRelationships.LoadTargets(readArchive, "ppt/_rels/presentation.xml.rels");
        var byTarget = OpcRelationships.LoadTypeByTargetMap(readArchive, "ppt/_rels/presentation.xml.rels");

        targets.Should().ContainSingle(target => target.Id == "rId1" && target.Type == "type/slide");
        OpcRelationships.FirstTargetByType(targets, "type/theme").Should().Be("theme/theme1.xml");
        byTarget.Should().Contain("slides/slide1.xml", "type/slide");
    }

    [Fact]
    public void ContentTypeMapHelpers_ReadAndMergePackageContentTypes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, OpcMediaTypes.ContentTypesPath, """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml" />
                  <Default Extension="emf" ContentType="image/x-emf" />
                  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml" />
                </Types>
                """);
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        OpcMediaTypes.ReadDefaultContentTypes(readArchive).Should().Contain("emf", "image/x-emf");
        OpcMediaTypes.ReadOverrideContentTypes(readArchive)
            .Should()
            .Contain("/ppt/slides/slide1.xml", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml");

        var target = new XDocument(new XElement(
            OpcMediaTypes.ContentTypesNamespace + "Types",
            new XElement(
                OpcMediaTypes.ContentTypesNamespace + "Default",
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml"))));
        var source = OpcXml.LoadXml(readArchive.GetEntry(OpcMediaTypes.ContentTypesPath)!);

        OpcMediaTypes.MergePreservedContentTypes(
            target,
            source,
            partName => partName.StartsWith("/ppt/slides/", StringComparison.OrdinalIgnoreCase));

        target.Root!.Elements(OpcMediaTypes.ContentTypesNamespace + "Default")
            .Should()
            .Contain(element => (string?)element.Attribute("Extension") == "emf");
        target.Root!.Elements(OpcMediaTypes.ContentTypesNamespace + "Override")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void CanonicalPackageRelationshipNormalizer_RepairsDuplicateDocumentPropertyRelationships()
    {
        const string corePropertiesType =
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
        var canonical = new OpcCanonicalRelationship("docProps/core.xml", corePropertiesType);
        var relationships = OpcRelationships.CreateDocument(
            OpcRelationships.CreateRelationship("rIdWorkbook", "type/workbook", "xl/workbook.xml"),
            OpcRelationships.CreateRelationship("rIdWrongType", "type/wrong", "/docProps/core.xml"),
            new XElement(
                OpcRelationships.Namespace + "Relationship",
                new XAttribute("Id", "rIdCore"),
                new XAttribute("Type", corePropertiesType),
                new XAttribute("Target", "/docProps/core.xml"),
                new XAttribute("TargetMode", "Internal")),
            OpcRelationships.CreateRelationship("rIdDuplicate", corePropertiesType, "docProps/core.xml"));

        OpcRelationships.NeedsCanonicalPackageRelationshipNormalization(
                relationships,
                canonical,
                partExists: true,
                ResolveRootTarget)
            .Should()
            .BeTrue();

        var changed = OpcRelationships.NormalizeCanonicalPackageRelationship(
            relationships,
            canonical,
            partExists: true,
            ResolveRootTarget);

        changed.Should().BeTrue();
        OpcRelationships.NeedsCanonicalPackageRelationshipNormalization(
                relationships,
                canonical,
                partExists: true,
                ResolveRootTarget)
            .Should()
            .BeFalse();
        var coreRelationship = relationships.Root!
            .Elements(OpcRelationships.Namespace + "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value == corePropertiesType ||
                ResolveRootTarget(element.Attribute("Target")?.Value ?? "") == canonical.PartName)
            .Should()
            .ContainSingle()
            .Subject;
        coreRelationship.Attribute("Id")!.Value.Should().Be("rIdCore");
        coreRelationship.Attribute("Target")!.Value.Should().Be("docProps/core.xml");
        coreRelationship.Attribute("TargetMode").Should().BeNull();
    }

    [Fact]
    public void CanonicalPackageRelationshipNormalizer_RemovesDanglingCanonicalRelationships()
    {
        const string customPropertiesType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
        var relationships = OpcRelationships.CreateDocument(
            OpcRelationships.CreateRelationship("rIdCustom", customPropertiesType, "docProps/custom.xml"));

        var changed = OpcRelationships.NormalizeCanonicalPackageRelationship(
            relationships,
            new OpcCanonicalRelationship("docProps/custom.xml", customPropertiesType),
            partExists: false,
            ResolveRootTarget);

        changed.Should().BeTrue();
        relationships.Root!.Elements(OpcRelationships.Namespace + "Relationship").Should().BeEmpty();
    }

    [Fact]
    public void OpcRelationshipDedupSourceGuard_UsesSharedHelpersAtFreeWAndFreeXCallSites()
    {
        var xlsxPropertiesSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDocumentPropertiesPreserver.cs");
        var xlsxLoadSanitizerSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxClosedXmlLoadPackageSanitizer.cs");
        var docxReaderSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxReader.cs");
        var docxWriterSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxWriter.cs");
        var pptxReaderSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.IO", "PptxPackageReader.cs");
        var pptxWriterSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.IO", "PptxPackageWriter.cs");

        xlsxPropertiesSource.Should().Contain("OpcRelationships.NormalizeCanonicalPackageRelationship");
        xlsxPropertiesSource.Should().Contain("OpcRelationships.NeedsCanonicalPackageRelationshipNormalization");
        xlsxPropertiesSource.Should().NotContain("private static bool RelationshipTargetsPart(");
        xlsxLoadSanitizerSource.Should().Contain("XlsxDocumentPropertiesPreserver.NeedsPackageGraphNormalization(archive)");
        xlsxLoadSanitizerSource.Should().NotContain("private static bool HasDocumentPropertyRelationshipIssue(");
        docxReaderSource.Should().Contain("OpcRelationships.LoadById");
        docxReaderSource.Should().Contain("OpcRelationships.LoadTargetMap");
        docxReaderSource.Should().Contain("OpcRelationships.LoadTypeByTargetMap");
        docxReaderSource.Should().Contain("OpcMediaTypes.ReadOverrideContentTypes");
        docxReaderSource.Should().Contain("OpcMediaTypes.ReadDefaultContentTypes");
        docxReaderSource.Should().NotContain("private static string? ResolveRelativePartName");
        docxWriterSource.Should().Contain("OpcRelationships.CreateRelationship(id, type, target, external)");
        pptxReaderSource.Should().Contain("using static Free.Shared.Opc.OpcPathHelper;");
        pptxReaderSource.Should().Contain("OpcRelationships.LoadTargets");
        pptxReaderSource.Should().Contain("OpcRelationships.FirstTargetByType");
        pptxReaderSource.Should().Contain("OpcXml.TryLoadXml(archive,");
        pptxReaderSource.Should().NotContain("private static XDocument? LoadXml");
        pptxReaderSource.Should().Contain("OpcMediaTypes.GetDrawingMediaContentType");
        pptxReaderSource.Should().Contain("OpcMediaTypes.GetAudioVideoContentType");
        pptxReaderSource.Should().NotContain("private static string GetRelsPath(");
        pptxReaderSource.Should().NotContain("private static string GuessContentType(");
        pptxWriterSource.Should().Contain("using static Free.Shared.Opc.OpcPathHelper;");
        pptxWriterSource.Should().Contain("OpcMediaTypes.MergePreservedContentTypes");
        pptxWriterSource.Should().Contain("OpcXml.TryLoadXml(bytes)");
        pptxWriterSource.Should().Contain("OpcMediaTypes.GetDrawingMediaExtension");
        pptxWriterSource.Should().Contain("OpcMediaTypes.GetAudioVideoExtension");
        pptxWriterSource.Should().NotContain("private static string ContentTypeToExtension(");
    }

    [Fact]
    public void FreeXOpcSubstrateDedupSourceGuard_UsesSharedPathRelationshipContentTypeAndXmlHelpers()
    {
        var xlsxPathSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxPackagePath.cs");
        var xlsxXmlEditorSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxPackageXmlEditor.cs");
        var relationshipReaderSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxRelationshipReader.cs");
        var excelCompatibilitySource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxExcelCompatibilityNormalizer.cs");
        var singleXmlCellSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetSingleXmlCellMapper.cs");

        xlsxPathSource.Should().Contain("OpcPathHelper.GetRelativeZipPath");
        xlsxPathSource.Should().Contain("OpcPathHelper.EscapeRelationshipPathSegments");
        xlsxXmlEditorSource.Should().Contain("OpcMediaTypes.EnsureDefaultContentType");
        xlsxXmlEditorSource.Should().Contain("OpcMediaTypes.EnsureOverrideContentType");
        xlsxXmlEditorSource.Should().NotContain("http://schemas.openxmlformats.org/package/2006/content-types");
        relationshipReaderSource.Should().Contain("OpcRelationships.Load");
        relationshipReaderSource.Should().NotContain("private static XDocument LoadXml");
        excelCompatibilitySource.Should().Contain("OpcMediaTypes.PruneMissingOverrideContentTypes");
        excelCompatibilitySource.Should().Contain("OpcRelationships.NextRelationshipId");
        excelCompatibilitySource.Should().NotContain("private static string NextRelationshipId");
        excelCompatibilitySource.Should().NotContain("private static void EnsureContentTypeOverride");
        singleXmlCellSource.Should().Contain("OpcMediaTypes.RemoveOverrideContentTypes");
        singleXmlCellSource.Should().NotContain("private static void RemoveSpecificContentTypes");
    }

    [Fact]
    public void LoadXml_RejectsDtdsThroughSharedHardenedReader()
    {
        using var stream = ToStream("""
            <!DOCTYPE root [ <!ENTITY x "blocked"> ]>
            <root>&x;</root>
            """);

        Action act = () => OpcXml.LoadXml(stream);

        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void LoadXml_WithLoadOptions_PreservesWhitespaceNodes()
    {
        using var stream = ToStream("""
            <root>
              <child />
            </root>
            """);

        var document = OpcXml.LoadXml(stream, LoadOptions.PreserveWhitespace);

        document.Root!.Nodes().OfType<XText>().Should().NotBeEmpty();
    }

    [Fact]
    public void ReplaceXmlEntry_DeletesDuplicateZipEntriesBeforeWritingReplacement()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, "xl/workbook.xml", "<old />");
            WriteText(archive, "xl/workbook.xml", "<stale />");
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            OpcXml.ReplaceXmlEntry(
                archive,
                "xl/workbook.xml",
                new XDocument(new XElement("replacement")));
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        readArchive.Entries.Where(e => e.FullName == "xl/workbook.xml").Should().ContainSingle();
        using var entryStream = readArchive.GetEntry("xl/workbook.xml")!.Open();
        OpcXml.LoadXml(entryStream).Root!.Name.LocalName.Should().Be("replacement");
    }

    [Fact]
    public void CoreDocumentProperties_BuildAndRead_RoundTripsSharedOpcFields()
    {
        var created = new DateTimeOffset(2026, 6, 28, 9, 10, 11, TimeSpan.Zero);
        var modified = new DateTimeOffset(2026, 6, 28, 10, 11, 12, TimeSpan.Zero);
        var properties = new CoreDocumentProperties(
            Title: "Quarterly Plan",
            Author: "FreeX",
            Subject: "Shared doc properties",
            Keywords: "opc,dedup",
            Comments: "Round-trip through shared helper",
            LastModifiedBy: "Codex",
            Created: created,
            Modified: modified,
            Category: "Planning",
            ContentStatus: "Draft",
            Language: "en-US",
            Version: "2026.06");

        var document = OpcDocumentProperties.BuildCorePropertiesDocument(
            properties,
            includeDcmiTypeNamespace: true,
            includeXmlDeclaration: true);

        document.Declaration.Should().NotBeNull();
        document.Root!.Attribute(XNamespace.Xmlns + "dcmitype")!.Value
            .Should()
            .Be(OpcDocumentProperties.DublinCoreTypeNamespace.NamespaceName);
        document.Root.Element(OpcDocumentProperties.DublinCoreTermsNamespace + "created")!
            .Attribute(OpcDocumentProperties.XmlSchemaInstanceNamespace + "type")!
            .Value
            .Should()
            .Be("dcterms:W3CDTF");
        OpcDocumentProperties.ReadCoreProperties(document).Should().Be(properties);
    }

    [Fact]
    public void DocumentProperties_ConvertsToAndFromCoreProperties()
    {
        var created = new DateTimeOffset(2026, 6, 29, 8, 0, 0, TimeSpan.Zero);
        var modified = created.AddHours(2);
        var properties = new DocumentProperties
        {
            Title = "Shared title",
            Author = "Shared author",
            Subject = "Shared subject",
            Keywords = "shared,opc",
            Comments = "Shared comments",
            LastModifiedBy = "Shared editor",
            Created = created,
            Modified = modified,
            Category = "Shared category",
            ContentStatus = "Draft",
            Language = "en-US",
            Version = "1.2.3"
        };

        var core = properties.ToCoreProperties();

        core.Should().Be(new CoreDocumentProperties(
            Title: "Shared title",
            Author: "Shared author",
            Subject: "Shared subject",
            Keywords: "shared,opc",
            Comments: "Shared comments",
            LastModifiedBy: "Shared editor",
            Created: created,
            Modified: modified,
            Category: "Shared category",
            ContentStatus: "Draft",
            Language: "en-US",
            Version: "1.2.3"));

        var copy = DocumentProperties.FromCoreProperties(core);
        copy.Should().BeEquivalentTo(properties);
        copy.CountNonEmptyCoreProperties().Should().Be(12);

        copy.Clear();
        copy.CountNonEmptyCoreProperties().Should().Be(0);
        copy.ToCoreProperties().Should().Be(new CoreDocumentProperties());
    }

    [Fact]
    public void DocumentProperties_ReadWriteCorePropertiesOverZipArchive_UsesSharedMutableModel()
    {
        var created = new DateTimeOffset(2026, 6, 30, 11, 12, 13, TimeSpan.Zero);
        var properties = new DocumentProperties
        {
            Title = "Shared zip title",
            Author = "Shared zip author",
            Subject = "",
            Keywords = "zip,shared",
            Comments = "Written from the mutable shared model",
            LastModifiedBy = "Codex",
            Created = created,
            Modified = created.AddMinutes(5),
            Category = "Dedup",
            ContentStatus = "Final",
            Language = "en-US",
            Version = "2"
        };

        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            OpcDocumentProperties.WriteCoreProperties(
                archive,
                properties,
                includeEmptyStrings: true,
                includeDcmiTypeNamespace: true,
                includeXmlDeclaration: true);
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        var copy = new DocumentProperties();
        OpcDocumentProperties.ReadCoreProperties(readArchive, copy);

        copy.ToCoreProperties().Should().Be(properties.ToCoreProperties());
    }

    [Fact]
    public void ExtendedDocumentProperties_BuildAndRead_RoundTripsSharedOpcFields()
    {
        var properties = new ExtendedDocumentProperties(
            Application: "Microsoft Excel",
            Company: "FreeX Test Lab",
            Manager: "Fidelity",
            PresentationFormat: "Workbook",
            Template: "SchemaTemplate.xltx");

        var document = OpcDocumentProperties.BuildExtendedPropertiesDocument(
            properties,
            includeXmlDeclaration: true);

        document.Declaration.Should().NotBeNull();
        OpcDocumentProperties.ReadExtendedProperties(document).Should().Be(properties);
    }

    [Fact]
    public void ExtendedDocumentProperties_ReadWriteOverZipArchive_UsesSharedOpcPartConstants()
    {
        var properties = new ExtendedDocumentProperties(
            Application: "FreeX",
            Company: "Free Suite",
            Manager: "Document Fidelity",
            PresentationFormat: "Workbook",
            Template: "RoundTrip.xltx");

        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            OpcDocumentProperties.WriteExtendedProperties(
                archive,
                properties,
                includeEmptyStrings: true,
                includeXmlDeclaration: true);
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        readArchive.GetEntry(OpcPackageProperties.ExtendedPropertiesZipEntry).Should().NotBeNull();
        OpcDocumentProperties.ReadExtendedProperties(readArchive).Should().Be(properties);
    }

    [Fact]
    public void ExtendedDocumentPropertiesDedupSourceGuard_StaysPreservationOnlyInFreeWAndFreeP()
    {
        var documentPropertiesModelSource = TestWorkspaceFiles.ReadRepoText(
            "shared",
            "Free.Shared.Opc",
            "DocumentProperties.cs");
        var textDocumentSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.Model", "TextDocument.cs");
        var presentationSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.Model", "Presentation.cs");
        var docxReaderSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxReader.cs");
        var docxWriterSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxWriter.cs");
        var pptxReaderSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.IO", "PptxPackageReader.cs");
        var pptxWriterSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.IO", "PptxPackageWriter.cs");
        var xlsxPropertiesSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDocumentPropertiesPreserver.cs");

        documentPropertiesModelSource.Should().NotContain("ExtendedDocumentProperties");
        textDocumentSource.Should().NotContain("ExtendedDocumentProperties");
        presentationSource.Should().NotContain("ExtendedDocumentProperties");

        docxReaderSource.Should()
            .Contain("OpcPackageProperties.ExtendedPropertiesZipEntry")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesPartName")
            .And.NotContain("OpcDocumentProperties.ReadExtendedProperties(");
        docxWriterSource.Should()
            .Contain("OpcPackageProperties.ExtendedPropertiesPartName")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesRelationshipType")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesZipEntry")
            .And.NotContain("OpcDocumentProperties.BuildExtendedPropertiesDocument(")
            .And.NotContain("OpcDocumentProperties.WriteExtendedProperties(");
        pptxReaderSource.Should().NotContain("OpcDocumentProperties.ReadExtendedProperties(");
        pptxWriterSource.Should().NotContain("OpcDocumentProperties.BuildExtendedPropertiesDocument(");

        xlsxPropertiesSource.Should()
            .Contain("OpcPackageProperties.ExtendedPropertiesZipEntry")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesRelationshipType")
            .And.Contain("OpcDocumentProperties.StableExtendedPropertyElementNames");
    }

    [Fact]
    public void CustomDocumentProperties_OverlayByNamePreservesRawPropertiesAndAllocatesDeterministicPids()
    {
        var cp = OpcCustomDocumentProperties.CustomPropertiesNamespace;
        var vt = OpcCustomDocumentProperties.VariantTypesNamespace;
        var source = new XElement(
            cp + "Properties",
            new XAttribute(XNamespace.Xmlns + "vt", vt.NamespaceName),
            CustomProperty(cp, vt, "2", "Project", new XElement(vt + "lpwstr", "Apollo")),
            CustomProperty(cp, vt, "5", "RawDate", new XElement(vt + "filetime", "2026-06-30T09:30:00Z")),
            CustomProperty(cp, vt, "7", "Reviewed", new XElement(vt + "bool", "1")));

        var properties = OpcCustomDocumentProperties.FromRoot(source);

        properties.GetString("Project").Should().Be("Apollo");
        properties.GetBoolean("Reviewed").Should().BeTrue();

        properties.SetString("Project", "Gemini");
        properties.SetBoolean("Approved", true);

        var byName = properties.ToXDocument().Root!
            .Elements(cp + "property")
            .ToDictionary(property => property.Attribute("name")!.Value);

        byName["Project"].Attribute("pid")!.Value.Should().Be("2");
        byName["Project"].Element(vt + "lpwstr")!.Value.Should().Be("Gemini");
        byName["Approved"].Attribute("pid")!.Value.Should().Be("3");
        byName["Approved"].Element(vt + "bool")!.Value.Should().Be("true");
        byName["RawDate"].Attribute("pid")!.Value.Should().Be("5");
        byName["RawDate"].Element(vt + "filetime")!.Value.Should().Be("2026-06-30T09:30:00Z");
        byName.Values.Select(property => property.Attribute("pid")!.Value).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CustomDocumentProperties_ReadsAndRemovesTypedOpcValues()
    {
        var properties = OpcCustomDocumentProperties.Create();

        properties.SetString("Title", "Shared custom title");
        properties.SetBoolean("Published", false);
        properties.SetDouble("Opacity", 0.625);

        properties.Contains("Title").Should().BeTrue();
        properties.GetString("Title").Should().Be("Shared custom title");
        properties.GetBoolean("Published").Should().BeFalse();
        properties.GetDouble("Opacity").Should().BeApproximately(0.625, 0.0001);

        properties.Remove("Title");

        properties.Contains("Title").Should().BeFalse();
        properties.PropertyElements.Select(property => property.Attribute("name")!.Value)
            .Should()
            .BeEquivalentTo(["Published", "Opacity"]);
    }

    [Fact]
    public void CustomDocumentPropertiesDedupSourceGuard_FreeWUsesSharedOpcHelper()
    {
        var docxReaderSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxReader.cs");
        var docxWriterSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxWriter.cs");

        docxReaderSource.Should().Contain("OpcCustomDocumentProperties.FromRoot");
        docxWriterSource.Should().Contain("OpcCustomDocumentProperties.FromRoot");
        docxWriterSource.Should().NotContain("new XElement(CustomProps + \"property\"");
        docxWriterSource.Should().NotContain("new XElement(VtVariant + \"bool\"");
    }

    [Fact]
    public void CoreDocumentPropertiesDedupSourceGuard_UsesSharedModelReaderWriterAcrossApps()
    {
        var sharedModelSource = TestWorkspaceFiles.ReadRepoText("shared", "Free.Shared.Opc", "DocumentProperties.cs");
        var sharedOpcSource = TestWorkspaceFiles.ReadRepoText("shared", "Free.Shared.Opc", "OpcDocumentProperties.cs");
        var textDocumentSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.Model", "TextDocument.cs");
        var docxReaderSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxReader.cs");
        var docxWriterSource = TestWorkspaceFiles.ReadRepoText("freew", "FreeW.Core.IO", "DocxWriter.cs");
        var presentationSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.Model", "Presentation.cs");
        var pptxReaderSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.IO", "PptxPackageReader.cs");
        var pptxWriterSource = TestWorkspaceFiles.ReadRepoText("freep", "FreeP.Core.IO", "PptxPackageWriter.cs");
        var xlsxPropertiesSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDocumentPropertiesPreserver.cs");

        sharedModelSource.Should().Contain("CoreDocumentProperties ToCoreProperties()");
        sharedModelSource.Should().Contain("ApplyCoreProperties(CoreDocumentProperties properties");
        sharedOpcSource.Should().Contain("CoreDocumentProperties ReadCoreProperties(");
        sharedOpcSource.Should().Contain("DocumentProperties target");
        sharedOpcSource.Should().Contain("DocumentProperties properties,");
        sharedOpcSource.Should().Contain("BuildCorePropertiesDocument(");

        textDocumentSource.Should().Contain("public DocumentProperties Properties { get; } = new();");
        presentationSource.Should().Contain("public DocumentProperties Properties { get; } = new();");

        docxReaderSource.Should().Contain("OpcDocumentProperties.ReadCoreProperties(");
        docxReaderSource.Should().Contain("document.Properties.ApplyCoreProperties(");
        docxWriterSource.Should().Contain("OpcDocumentProperties.BuildCorePropertiesDocument(");
        docxWriterSource.Should().NotContain("properties.ToCoreProperties()");
        pptxReaderSource.Should().Contain("OpcDocumentProperties.ReadCoreProperties(");
        pptxReaderSource.Should().Contain("OpcPackageProperties.CorePropertiesRelationshipType");
        pptxReaderSource.Should().Contain("presentation.Properties,");
        pptxReaderSource.Should().NotContain("props.ApplyCoreProperties(");
        pptxWriterSource.Should().Contain("OpcDocumentProperties.BuildCorePropertiesDocument(");
        pptxWriterSource.Should().Contain("OpcPackageProperties.CorePropertiesRelationshipType");
        pptxWriterSource.Should().Contain("OpcPackageProperties.CorePropertiesZipEntry");
        pptxWriterSource.Should().NotContain("presentation.Properties.ToCoreProperties()");
        xlsxPropertiesSource.Should().Contain("OpcDocumentProperties.PreservePropertyElements(");
    }

    [Fact]
    public void PreservePropertyElements_CopiesOnlyRequestedOpcPropertyElements()
    {
        var source = new XElement(
            OpcDocumentProperties.CorePropertiesNamespace + "coreProperties",
            new XElement(OpcDocumentProperties.DublinCoreNamespace + "title", "source title"),
            new XElement(OpcDocumentProperties.DublinCoreNamespace + "subject", "source subject"),
            new XElement(OpcDocumentProperties.CorePropertiesNamespace + "category", "source category"));
        var target = new XElement(
            OpcDocumentProperties.CorePropertiesNamespace + "coreProperties",
            new XElement(OpcDocumentProperties.DublinCoreNamespace + "title", "target title"));

        var changed = OpcDocumentProperties.PreservePropertyElements(
            source,
            target,
            OpcDocumentProperties.WorkbookStableCorePropertyElementNames);

        changed.Should().BeTrue();
        target.Element(OpcDocumentProperties.DublinCoreNamespace + "title")!.Value.Should().Be("target title");
        target.Element(OpcDocumentProperties.DublinCoreNamespace + "subject")!.Value.Should().Be("source subject");
        target.Element(OpcDocumentProperties.CorePropertiesNamespace + "category")!.Value.Should().Be("source category");
    }

    private static MemoryStream ToStream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml), writable: false);

    private static string ResolveRootTarget(string target) =>
        OpcPathHelper.ResolveRelativeZipPath("", target);

    private static XElement CustomProperty(XNamespace cp, XNamespace vt, string pid, string name, XElement value) =>
        new(
            cp + "property",
            new XAttribute("fmtid", OpcCustomDocumentProperties.DefaultFormatId),
            new XAttribute("pid", pid),
            new XAttribute("name", name),
            value);

    private static void WriteText(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }
}
