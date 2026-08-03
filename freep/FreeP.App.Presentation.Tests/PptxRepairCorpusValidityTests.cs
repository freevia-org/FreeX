using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PptxRepairCorpusValidityTests
{
    private const string PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string PresentationNamespace =
        "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string DiagramDrawingRelationshipType =
        "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    [Theory]
    [InlineData("10-motionpath.pptx")]
    [InlineData("14-smartart-live.pptx")]
    [InlineData("21-comments-notes.pptx")]
    public void RepairDialogCorpusDecks_OpenAndValidateWithoutSchemaErrors(string deckName)
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), deckName);
        var sourceBytes = File.ReadAllBytes(deckPath);
        var sourceErrors = ValidateSlideSchema(sourceBytes);
        sourceErrors.Should().BeEmpty(
            "{0} should open as a schema-valid presentation package; errors: {1}",
            deckName,
            string.Join("; ", sourceErrors));

        var presentation = PptxPackageReader.Read(new MemoryStream(sourceBytes));
        using var roundTrip = new MemoryStream();
        PptxPackageWriter.Write(presentation, roundTrip);
        var roundTripBytes = roundTrip.ToArray();
        var roundTripErrors = ValidateSlideSchema(roundTripBytes);
        roundTripErrors.Should().BeEmpty(
            "{0} should remain schema-valid after FreeP read/write; errors: {1}",
            deckName,
            string.Join("; ", roundTripErrors));
    }

    [Fact]
    public void EditingSmartArtVerticalBlockList_PreservesSchemaValidPackage()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var smartArt = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        smartArt.Should().NotBeNull("the live SmartArt corpus must contain an editable diagram");
        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.VerticalBlockList);

        result.Applied.Should().BeTrue(result.Message);
        smartArt!.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);

        using var roundTrip = new MemoryStream();
        PptxPackageWriter.Write(presentation, roundTrip);
        var roundTripBytes = roundTrip.ToArray();
        ValidateSlideSchema(roundTripBytes)
            .Should()
            .BeEmpty("an edited SmartArt package must remain Open XML schema-valid");

        using var rereadStream = new MemoryStream(roundTripBytes);
        var reread = PptxPackageReader.Read(rereadStream);
        var rereadSmartArt = reread.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        rereadSmartArt.Should().NotBeNull();
        rereadSmartArt!.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
    }

    [Fact]
    public void EditingSmartArtContinuousBlockProcess_RoundTripsLiveCacheAndSchema()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var smartArt = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        smartArt.Should().NotBeNull("the live SmartArt corpus must contain an editable diagram");
        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt,
            SmartArtLayoutPreset.ContinuousBlockProcess);

        result.Applied.Should().BeTrue(result.Message);
        smartArt!.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
        var container = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .First(shape => shape.SmartArt == smartArt);
        var cacheResult = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            container.OffsetXEmu,
            container.OffsetYEmu,
            container.ExtentCxEmu,
            container.ExtentCyEmu,
            presentation.Theme!);
        cacheResult.Applied.Should().BeTrue(cacheResult.Message);
        var expectedBlockCount = smartArt.FallbackShapes.Count;
        expectedBlockCount.Should().BeGreaterThan(0);
        var blockCount = smartArt.FallbackShapes.Count(shape => shape.Name.StartsWith(
            "SmartArt_ContinuousBlockProcess_Block_", StringComparison.Ordinal));
        blockCount.Should().BeGreaterThan(0);
        smartArt.FallbackShapes.Count(shape => shape.Name.StartsWith(
            "SmartArt_ContinuousBlockProcess_Connector_", StringComparison.Ordinal))
            .Should().Be(blockCount - 1);
        smartArt.FallbackShapes
            .Where(shape => shape.Name.StartsWith("SmartArt_ContinuousBlockProcess_Block_", StringComparison.Ordinal))
            .Should().OnlyContain(shape =>
                shape.AutoShapeKind == Free.Shared.Drawing.DrawingShapeKind.RoundedRectangle);

        using var roundTrip = new MemoryStream();
        PptxPackageWriter.Write(presentation, roundTrip);
        ValidateSlideSchema(roundTrip.ToArray())
            .Should().BeEmpty("an edited continuous block SmartArt package must remain schema-valid");

        var reread = PptxPackageReader.Read(new MemoryStream(roundTrip.ToArray()));
        var rereadSmartArt = reread.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        rereadSmartArt.Should().NotBeNull();
        rereadSmartArt!.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
        rereadSmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        rereadSmartArt.FallbackShapes.Should().HaveCount(expectedBlockCount);
        rereadSmartArt.FallbackShapes
            .Where(shape => shape.Name.StartsWith("SmartArt_ContinuousBlockProcess_Block_", StringComparison.Ordinal))
            .Select(shape => shape.PlainText)
            .Should().Equal(smartArt.FallbackShapes
                .Where(shape => shape.Name.StartsWith("SmartArt_ContinuousBlockProcess_Block_", StringComparison.Ordinal))
            .Select(shape => shape.PlainText));
    }

    [Fact]
    public void EditingSmartArtSegmentedProcess_RoundTripsLiveCacheAndSchema()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var smartArt = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        smartArt.Should().NotBeNull("the live SmartArt corpus must contain an editable diagram");
        var result = SmartArtAuthoringPlanner.ApplyLayoutPreset(
            smartArt, SmartArtLayoutPreset.SegmentedProcess);

        result.Applied.Should().BeTrue(result.Message);
        smartArt!.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
        var container = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .First(shape => shape.SmartArt == smartArt);
        var cacheResult = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            container.OffsetXEmu,
            container.OffsetYEmu,
            container.ExtentCxEmu,
            container.ExtentCyEmu,
            presentation.Theme!);

        cacheResult.Applied.Should().BeTrue(cacheResult.Message);
        var segmentCount = smartArt.FallbackShapes.Count(shape =>
            shape.Name.StartsWith("SmartArt_SegmentedProcess_Segment_", StringComparison.Ordinal));
        segmentCount.Should().BeGreaterThan(0);
        smartArt.FallbackShapes.Count(shape =>
            shape.Name.StartsWith("SmartArt_SegmentedProcess_Relationship_", StringComparison.Ordinal))
            .Should().Be(segmentCount - 1);
        smartArt.FallbackShapes
            .Where(shape => shape.Name.StartsWith("SmartArt_SegmentedProcess_Segment_", StringComparison.Ordinal))
            .Should().OnlyContain(shape => shape.AutoShapeKind == Free.Shared.Drawing.DrawingShapeKind.Rectangle);

        using var roundTrip = new MemoryStream();
        PptxPackageWriter.Write(presentation, roundTrip);
        var roundTripBytes = roundTrip.ToArray();
        ValidateSlideSchema(roundTripBytes)
            .Should().BeEmpty("an edited segmented process SmartArt package must remain schema-valid");

        var reread = PptxPackageReader.Read(new MemoryStream(roundTripBytes));
        var rereadSmartArt = reread.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        rereadSmartArt.Should().NotBeNull();
        rereadSmartArt!.Data!.LayoutUniqueId.Should().Be(result.LayoutUniqueId);
        rereadSmartArt.Data.IsLiveLayoutSupported.Should().BeTrue();
        rereadSmartArt.FallbackShapes
            .Where(shape => shape.Name.StartsWith("SmartArt_SegmentedProcess_Segment_", StringComparison.Ordinal))
            .Select(shape => shape.PlainText)
            .Should().Equal(smartArt.FallbackShapes
                .Where(shape => shape.Name.StartsWith("SmartArt_SegmentedProcess_Segment_", StringComparison.Ordinal))
                .Select(shape => shape.PlainText));
    }

    [Fact]
    public void EditingSmartArtQuickStyleAndColors_PreservesSchemaValidPackage()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var smartArt = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        smartArt.Should().NotBeNull("the live SmartArt corpus must contain an editable diagram");

        var styleResult = SmartArtAuthoringPlanner.ApplyQuickStylePreset(
            smartArt,
            SmartArtQuickStylePreset.IntenseEffect);
        styleResult.Applied.Should().BeTrue(styleResult.Message);

        var colorResult = SmartArtAuthoringPlanner.ApplyColorPreset(
            smartArt,
            SmartArtColorPreset.ColoredFillAccent2,
            presentation.Theme);
        colorResult.Applied.Should().BeTrue(colorResult.Message);

        using var roundTrip = new MemoryStream();
        PptxPackageWriter.Write(presentation, roundTrip);
        var roundTripBytes = roundTrip.ToArray();
        ValidateSlideSchema(roundTripBytes)
            .Should()
            .BeEmpty("native SmartArt style and color edits must remain Open XML schema-valid");

        var reread = PptxPackageReader.Read(new MemoryStream(roundTripBytes));
        var rereadSmartArt = reread.Slides
            .SelectMany(slide => slide.Shapes)
            .Select(shape => shape.SmartArt)
            .FirstOrDefault(candidate => candidate is not null);

        rereadSmartArt.Should().NotBeNull();
        rereadSmartArt!.QuickStyle!.UniqueId.Should().Be(styleResult.StyleUniqueId);
        rereadSmartArt.Colors!.UniqueId.Should().Be(smartArt.Colors!.UniqueId);
    }

    [Fact]
    public void MotionPathRoundTrip_UsesPowerPointTimingRoot()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "10-motionpath.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var slideXml = LoadXml(archive, "ppt/slides/slide1.xml");
        XNamespace presentationNamespace = PresentationNamespace;
        var rootTimingNode = slideXml
            .Element(presentationNamespace + "sld")!
            .Element(presentationNamespace + "timing")!
            .Element(presentationNamespace + "tnLst")!
            .Element(presentationNamespace + "par")!
            .Element(presentationNamespace + "cTn")!;

        rootTimingNode.Attribute("dur")?.Value.Should().Be("indefinite");
        rootTimingNode.Attribute("restart")?.Value.Should().Be("never");
        rootTimingNode.Attribute("fill")?.Value.Should().Be("hold");
        rootTimingNode.Attribute("nodeType")?.Value.Should().Be("tmRoot");
    }

    [Fact]
    public void SmartArtLiveCorpus_ExposesDrawingRelationshipAndPart()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        using var archive = ZipFile.OpenRead(deckPath);
        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        XNamespace contentTypeNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace presentationNamespace = PresentationNamespace;

        var hasPresentationGuideList = LoadXml(archive, "ppt/presentation.xml")
            .Descendants(presentationNamespace + "ext")
            .Any(element =>
                (string?)element.Attribute("uri") == "{EFAFB233-063F-42B5-8137-9DF3F51BA10A}" &&
                element.Element(XName.Get("sldGuideLst", "http://schemas.microsoft.com/office/powerpoint/2012/main")) != null);
        hasPresentationGuideList.Should().BeTrue();

        var slideRelationship = LoadXml(archive, "ppt/slides/_rels/slide1.xml.rels")
            .Root!
            .Elements(XName.Get("Relationship", PackageRelationshipNamespace))
            .Single(element => (string?)element.Attribute("Type") == DiagramDrawingRelationshipType);
        var target = (string)slideRelationship.Attribute("Target")!;
        var drawingPath = "ppt/slides/" + target;
        drawingPath = drawingPath.Replace("ppt/slides/../", "ppt/");

        archive.GetEntry(drawingPath).Should().NotBeNull();
        contentTypes.Root!
            .Elements(contentTypeNamespace + "Override")
            .Should()
            .Contain(element =>
                (string?)element.Attribute("PartName") == "/" + drawingPath &&
                (string?)element.Attribute("ContentType") ==
            "application/vnd.ms-office.drawingml.diagramDrawing+xml");
    }

    [Fact]
    public void SmartArtLiveCorpus_AdmitsObservedHierarchy3CacheToSharedLivePlan()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);

        var hierarchy3 = presentation.Slides
            .SelectMany(slide => slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.SmartArt)
            .Select(shape => shape.SmartArt!)
            .Where(smartArt => smartArt.Data?.LayoutUniqueId.EndsWith(
                "/hierarchy3", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        hierarchy3.Should().NotBeEmpty();
        hierarchy3.Should().OnlyContain(smartArt => smartArt.Data!.IsLiveLayoutSupported,
            "the audited hierarchy3 node/template/orthogonal cache has a bounded shared live plan");
        hierarchy3.Should().OnlyContain(smartArt => smartArt.FallbackShapes.Count > 0,
            "the imported dsp:drawing remains preserved for unsupported variants and round-tripping");
    }

    [Fact]
    public void PictureCaptionListInsertion_RoundTripsWithSchemaValidMediaParts()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var picture = SlideObjectInsertionPlanner.CreatePicturePayload(
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
            "sample.png");

        SlideObjectInsertionPlanner.ApplyCommand(
            editor,
            SlideObjectInsertionPlanner.SmartArtLayoutCommandId(SmartArtLayoutPreset.PictureCaptionList),
            smartArtPicturePayload: SlideObjectInsertionPlanner.CreateSmartArtPicturePayload([picture]))
            .Should().NotBeNull();

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        ValidateSlideSchema(stream.ToArray()).Should().BeEmpty();

        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream);
        var smart = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.SmartArt).SmartArt!;
        smart.Data!.IsLiveLayoutSupported.Should().BeTrue();
        smart.Data.Nodes[0].Picture!.Bytes.Should().Equal(picture.Bytes);
    }

    [Fact]
    public void SmartArtLiveCorpus_PreservesChordPresetAdjustments()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var smartArt = presentation.Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        var chords = smartArt.FallbackShapes
            .Where(shape => shape.AutoShapeKind == DrawingShapeKind.Chord)
            .ToArray();

        chords.Should().HaveCount(3);
        chords[0].PresetGeometryAdjustments["adj1"].Should().Be(1168272);
        chords[0].PresetGeometryAdjustments["adj2"].Should().Be(9631728);
        chords[1].PresetGeometryAdjustments["adj1"].Should().Be(20431728);
        chords[1].PresetGeometryAdjustments["adj2"].Should().Be(11968272);
        chords[2].PresetGeometryAdjustments["adj1"].Should().Be(16200000);
        chords[2].PresetGeometryAdjustments["adj2"].Should().Be(16200000);
    }

    [Fact]
    public void SmartArtLiveCorpus_PreservesRichIncreasingCircleProcessCache()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var increasingCircleProcess = presentation.Slides[0].Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        increasingCircleProcess.Data!.IsLiveLayoutSupported.Should().BeFalse(
            "the richer PowerPoint background/chord/rectangle cache is outside the bounded live grammar");
        increasingCircleProcess.FallbackShapes.Should().NotBeEmpty();

        var shapes = SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>()
            .ToArray();

        var backgroundEllipses = shapes
            .Select(shape => shape.Fill)
            .OfType<ResolvedFill.Solid>()
            .Where(fill => fill.Color == SrgbColor.FromRgb(0xCCD2D8))
            .ToArray();

        backgroundEllipses.Should().NotBeEmpty(
            "the richer imported cache remains authoritative instead of claiming the smaller live grammar");
    }

    [Fact]
    public void SmartArtLiveCorpus_AdmitsIncreasingCircleProcessFixtureToSharedLiveLayout()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "15-smartart-grouped-list.pptx");
        var presentation = PptxPackageReader.Read(deckPath);
        var slide = presentation.Slides[8];
        var smartArt = slide.Shapes
            .Single(shape => shape.Kind == SlideShapeKind.SmartArt)
            .SmartArt!;

        smartArt.Data!.IsLiveLayoutSupported.Should().BeTrue();
        smartArt.FallbackShapes.Should().HaveCount(7);

        var shapes = SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .ToArray();
        shapes.Where(shape => shape.Text is not null)
            .Select(shape => shape.Text!.Paragraphs.First().Runs.First().Text)
            .Should().ContainInOrder("Phase A", "Phase B", "Phase C", "Phase D");
        shapes.Where(shape => shape.Text is null).Should().HaveCount(3);
    }

    [Fact]
    public void SmartArtLiveCorpus_ComposesCachedCycleArrowsAsOfficeNeutral()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "14-smartart-live.pptx");
        var presentation = PptxPackageReader.Read(deckPath);

        var composedShapes = SlideCompositor.Compose(presentation, presentation.Slides[2])
            .OfType<DrawOp.Shape>()
            .ToArray();

        var neutralArrows = composedShapes
            .Select(shape => shape.Fill)
            .OfType<ResolvedFill.Solid>()
            .Where(fill => fill.Color == SrgbColor.FromRgb(0xAAB6C1))
            .ToArray();

        neutralArrows.Should().HaveCount(5);
    }

    [Fact]
    public void CommentsNotesCorpus_UsesUniqueNotesShapeIds()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "21-comments-notes.pptx");
        using var archive = ZipFile.OpenRead(deckPath);
        XNamespace presentationNamespace = PresentationNamespace;
        XNamespace drawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var presentation = LoadXml(archive, "ppt/presentation.xml");
        var notesMasterId = presentation
            .Descendants(presentationNamespace + "notesMasterId")
            .Single()
            .Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))!
            .Value;
        LoadXml(archive, "ppt/_rels/presentation.xml.rels")
            .Descendants(XName.Get("Relationship", PackageRelationshipNamespace))
            .Should()
            .Contain(element =>
                (string?)element.Attribute("Id") == notesMasterId &&
                (string?)element.Attribute("Target") == "notesMasters/notesMaster1.xml");
        var notesStyle = LoadXml(archive, "ppt/notesMasters/notesMaster1.xml")
            .Descendants(presentationNamespace + "notesStyle")
            .SingleOrDefault();
        notesStyle.Should().NotBeNull();
        notesStyle!.Elements(drawingNamespace + "lvl1pPr").Should().HaveCount(1);
        var notesSlidesHaveColorMapOverrides = archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/notesSlides/notesSlide", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(entry => LoadXml(archive, entry.FullName))
            .All(notesSlide => notesSlide.Descendants(presentationNamespace + "clrMapOvr")
                .Any(element => element.Element(drawingNamespace + "masterClrMapping") != null));
        notesSlidesHaveColorMapOverrides.Should().BeTrue();
        var notesShapeIdsBySlide = archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/notesSlides/notesSlide", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(entry => LoadXml(archive, entry.FullName)
                .Descendants(presentationNamespace + "cNvPr")
                .Select(element => (string?)element.Attribute("id"))
                .Where(id => id is not null)
                .ToArray())
            .ToArray();

        notesShapeIdsBySlide.Should().NotBeEmpty();
        notesShapeIdsBySlide
            .Should()
            .OnlyContain(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Length);
    }

    private static XDocument LoadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"{entryName} must exist");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static string[] ValidateSlideSchema(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var package = PresentationDocument.Open(stream, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(package)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => error.Description + " @ " + error.Path?.XPath)
            .ToArray();
    }

    private static string FindCorpusDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "FreeP.RenderCompare", "corpus");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "10-motionpath.pptx")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate tools/FreeP.RenderCompare/corpus.");
    }
}
