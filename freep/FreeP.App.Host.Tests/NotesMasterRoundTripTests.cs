using System.IO;
using System.IO.Compression;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class NotesMasterRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.NotesMasterTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

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
