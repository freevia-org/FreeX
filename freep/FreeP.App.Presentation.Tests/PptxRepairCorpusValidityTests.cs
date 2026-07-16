using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeP.Core.IO;

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
    public void CommentsNotesCorpus_UsesUniqueNotesShapeIds()
    {
        var deckPath = Path.Combine(FindCorpusDirectory(), "21-comments-notes.pptx");
        using var archive = ZipFile.OpenRead(deckPath);
        XNamespace presentationNamespace = PresentationNamespace;
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
