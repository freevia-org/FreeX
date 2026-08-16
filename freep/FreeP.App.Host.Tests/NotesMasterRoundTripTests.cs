using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class NotesMasterRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.NotesMasterTests-");
    private string _tempDir => _temporaryDirectory.Path;

    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string HandoutMasterRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/handoutMaster";

    public void Dispose() => _temporaryDirectory.Dispose();

    // R137: PptxPackageReader must parse the notes master's own p:hf (visibility flags) and the
    // handout master's placeholders/p:hf, since PresentationNotesPagePreviewPlanner and
    // PresentationHandoutPdfExporter now read from Presentation.NotesHfVisibility /
    // HandoutMasterPlaceholders / HandoutHfVisibility instead of the slide's own HfVisibility.
    [Fact]
    public void NotesAndHandoutMaster_HfVisibilityAndPlaceholders_AreParsedFromRealPackageXml()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Notes = new TextBody();
        var output = Path.Combine(_tempDir, "notes-handout-hf.pptx");
        PptxPackageWriter.Write(presentation, output);

        InjectNotesMasterHf(output, ftr: "0", dt: "1", sldNum: "1", hdr: "0");
        InjectHandoutMasterWithFooterAndHf(output, ftr: "0", dt: "1", sldNum: "1", hdr: "0");

        var reloaded = PptxPackageReader.Read(output);

        reloaded.NotesHfVisibility.Should().NotBeNull();
        reloaded.NotesHfVisibility!.ShowFooter.Should().BeFalse();
        reloaded.NotesHfVisibility.ShowDate.Should().BeTrue();
        reloaded.NotesHfVisibility.ShowSlideNum.Should().BeTrue();
        reloaded.NotesHfVisibility.ShowHeader.Should().BeFalse();

        reloaded.HandoutHfVisibility.Should().NotBeNull();
        reloaded.HandoutHfVisibility!.ShowFooter.Should().BeFalse();
        reloaded.HandoutHfVisibility.ShowDate.Should().BeTrue();

        var footerShape = reloaded.HandoutMasterPlaceholders
            .Single(shape => shape.Placeholder?.Type == PlaceholderType.Footer);
        footerShape.TextBody!.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should().Contain("Handout Footer Text");
    }

    private static void InjectNotesMasterHf(string pptxPath, string ftr, string dt, string sldNum, string hdr)
    {
        using var archive = ZipFile.Open(pptxPath, ZipArchiveMode.Update);
        const string entryName = "ppt/notesMasters/notesMaster1.xml";
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException(
            $"expected {entryName} to already exist (written because the deck has notes)");

        XDocument doc;
        using (var stream = entry.Open())
            doc = XDocument.Load(stream);

        var clrMap = doc.Root!.Element(P + "clrMap")!;
        clrMap.AddAfterSelf(new XElement(P + "hf",
            new XAttribute("ftr", ftr),
            new XAttribute("dt", dt),
            new XAttribute("sldNum", sldNum),
            new XAttribute("hdr", hdr)));

        entry.Delete();
        var newEntry = archive.CreateEntry(entryName);
        using var writeStream = newEntry.Open();
        doc.Save(writeStream);
    }

    private static void InjectHandoutMasterWithFooterAndHf(
        string pptxPath, string ftr, string dt, string sldNum, string hdr)
    {
        var handoutMasterXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "handoutMaster",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr"),
                        new XElement(P + "sp",
                            new XElement(P + "nvSpPr",
                                new XElement(P + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "Footer Placeholder 1")),
                                new XElement(P + "cNvSpPr"),
                                new XElement(P + "nvPr", new XElement(P + "ph", new XAttribute("type", "ftr")))),
                            new XElement(P + "spPr",
                                new XElement(A + "xfrm",
                                    new XElement(A + "off", new XAttribute("x", "100000"), new XAttribute("y", "200000")),
                                    new XElement(A + "ext", new XAttribute("cx", "300000"), new XAttribute("cy", "50000")))),
                            new XElement(P + "txBody",
                                new XElement(A + "bodyPr"),
                                new XElement(A + "p",
                                    new XElement(A + "r",
                                        new XElement(A + "t", "Handout Footer Text")))))),
                    new XElement(P + "extLst")),
                new XElement(P + "clrMap",
                    new XAttribute("bg1", "lt1"), new XAttribute("tx1", "dk1"),
                    new XAttribute("bg2", "lt2"), new XAttribute("tx2", "dk2"),
                    new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"),
                    new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"),
                    new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"),
                    new XAttribute("hlink", "hlink"), new XAttribute("folHlink", "folHlink")),
                new XElement(P + "hf",
                    new XAttribute("ftr", ftr),
                    new XAttribute("dt", dt),
                    new XAttribute("sldNum", sldNum),
                    new XAttribute("hdr", hdr))));

        using var archive = ZipFile.Open(pptxPath, ZipArchiveMode.Update);

        var masterEntry = archive.CreateEntry("ppt/handoutMasters/handoutMaster1.xml");
        using (var stream = masterEntry.Open())
            handoutMasterXml.Save(stream);

        const string presRelsName = "ppt/_rels/presentation.xml.rels";
        var relsEntry = archive.GetEntry(presRelsName) ?? throw new InvalidOperationException(
            $"expected {presRelsName} to already exist");
        XDocument relsDoc;
        using (var stream = relsEntry.Open())
            relsDoc = XDocument.Load(stream);

        var existingIds = relsDoc.Root!.Elements(Rel + "Relationship")
            .Select(rel => rel.Attribute("Id")!.Value)
            .ToHashSet();
        var newId = "rIdHandoutMasterTest";
        while (existingIds.Contains(newId))
            newId += "x";
        relsDoc.Root!.Add(new XElement(Rel + "Relationship",
            new XAttribute("Id", newId),
            new XAttribute("Type", HandoutMasterRelType),
            new XAttribute("Target", "handoutMasters/handoutMaster1.xml")));

        relsEntry.Delete();
        var newRelsEntry = archive.CreateEntry(presRelsName);
        using var writeRelsStream = newRelsEntry.Open();
        relsDoc.Save(writeRelsStream);
    }

    [Fact]
    public void CorpusNotesMaster_IsReadWithNativeStyleAndRetainedAcrossRoundTrip()
    {
        var path = TestWorkspaceFileLocator.TryFindFileFromBaseDirectory(
            "tools", "FreeP.RenderCompare", "corpus", "21-comments-notes.pptx");
        if (path is null) return;

        var original = PptxPackageReader.Read(path);
        original.NotesMasterXml.Should().NotBeNullOrEmpty();
        original.NotesMasterRelsXml.Should().NotBeNullOrEmpty();
        original.NotesMasterTextStyles.Should().NotBeNull();
        original.NotesMasterTextStyles!.BodyStyle[0]!.FontSizePt.Should().Be(12);

        var output = Path.Combine(_tempDir, "notes-master-roundtrip.pptx");
        PptxPackageWriter.Write(original, output);
        var reloaded = PptxPackageReader.Read(output);

        reloaded.NotesMasterXml.Should().Equal(original.NotesMasterXml!);
        reloaded.NotesMasterRelsXml.Should().Equal(original.NotesMasterRelsXml!);
        reloaded.NotesMasterTextStyles!.BodyStyle[0]!.FontSizePt.Should().Be(12);

        using var archive = ZipFile.OpenRead(output);
        archive.GetEntry("ppt/notesMasters/notesMaster1.xml").Should().NotBeNull();
        archive.GetEntry("ppt/notesMasters/_rels/notesMaster1.xml.rels").Should().NotBeNull();
    }

    [Fact]
    public void NotesPreview_UsesNativeNotesMasterPlaceholderGeometryBeforeFallback()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Notes = new TextBody();
        presentation.NotesMasterPlaceholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Body },
            OffsetXEmu = 1_270_000,
            OffsetYEmu = 2_540_000,
            ExtentCxEmu = 3_810_000,
            ExtentCyEmu = 1_270_000,
        });

        var plan = PresentationNotesPagePreviewPlanner.Build(presentation, 0);

        plan.NotesBounds.Should().Be(new LayoutRect(100, 200, 300, 100));
    }

    [Fact]
    public void NewPresentation_WritesPowerPointNotesMasterPlaceholderGeometry()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Notes = new TextBody();
        var output = Path.Combine(_tempDir, "new-notes-master.pptx");

        PptxPackageWriter.Write(presentation, output);
        var reloaded = PptxPackageReader.Read(output);

        reloaded.NotesMasterPlaceholders.Should().HaveCount(6);
        var slideImage = reloaded.NotesMasterPlaceholders
            .Single(shape => shape.Name == "Slide Image Placeholder 3");
        slideImage.Placeholder!.Type.Should().Be(PlaceholderType.Picture);
        slideImage.OffsetXEmu.Should().Be(685800);
        slideImage.OffsetYEmu.Should().Be(1143000);
        slideImage.ExtentCxEmu.Should().Be(5486400);
        slideImage.ExtentCyEmu.Should().Be(3086100);

        var notesBody = reloaded.NotesMasterPlaceholders
            .Single(shape => shape.Placeholder?.Type == PlaceholderType.Body);
        notesBody.OffsetXEmu.Should().Be(685800);
        notesBody.OffsetYEmu.Should().Be(4400550);
        notesBody.ExtentCxEmu.Should().Be(5486400);
        notesBody.ExtentCyEmu.Should().Be(3600450);

        var plan = PresentationNotesPagePreviewPlanner.Build(reloaded, 0);
        plan.SlideBounds.Should().Be(new LayoutRect(54, 90, 432, 243));
        plan.NotesBounds.Should().Be(new LayoutRect(54, 346.5, 432, 283.5));
    }

}
