using System.IO;
using System.IO.Compression;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class NotesMasterRoundTripTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.NotesMasterTests", Guid.NewGuid().ToString("N"));

    public NotesMasterRoundTripTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void CorpusNotesMaster_IsReadWithNativeStyleAndRetainedAcrossRoundTrip()
    {
        var path = CorpusPath("21-comments-notes.pptx");
        if (!File.Exists(path)) return;

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

    private static string CorpusPath(string filename)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "tools", "FreeP.RenderCompare", "corpus", filename);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }

        return Path.Combine(
            @"C:\Users\quinn\Documents\GitHub\FreeX\.worktrees\freep-animation-parity-20260720\tools\FreeP.RenderCompare\corpus",
            filename);
    }
}
