using System.IO;
using System.IO.Compression;
using System.Text;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r144 freep-shape-geometry F1: a preset geometry FreeP does not model (e.g. prst="pie",
/// "cloud", "gear6", most flowChart* variants, etc.) used to be silently mapped to
/// DrawingShapeKind.Rectangle on load with no memory of the original preset text, so the very
/// next File&gt;Save permanently replaced the shape's true outline with a plain rectangle. The
/// fix threads the raw prst string through SlideShape.UnmodeledPresetGeometry (mirroring the
/// existing SlideShape.PictureFrameGeometry preservation path for pictures) so the writer can
/// re-emit the original preset instead of destroying it.
/// </summary>
public sealed class UnmodeledPresetGeometryPreservationTests
{
    /// <summary>
    /// Builds a minimal one-shape .pptx via the real writer, then hand-edits the persisted
    /// slide XML's a:prstGeom/@prst to an OOXML preset FreeP does not model ("pie") -- exactly
    /// what a real-world deck referencing an unmodeled autoshape looks like on disk.
    /// </summary>
    private static MemoryStream BuildPptxWithUnmodeledPreset(string unmodeledPrst)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Shape 1",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 1371600,
        });

        var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;

        ReplaceZipEntryText(buffer, "ppt/slides/slide1.xml",
            xml => xml.Replace("prst=\"rect\"", $"prst=\"{unmodeledPrst}\""));

        buffer.Position = 0;
        return buffer;
    }

    private static void ReplaceZipEntryText(MemoryStream zipStream, string entryPath, System.Func<string, string> transform)
    {
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(entryPath)!;
            string original;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                original = reader.ReadToEnd();

            var updated = transform(original);
            entry.Delete();
            var newEntry = archive.CreateEntry(entryPath);
            using var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false));
            writer.Write(updated);
        }
    }

    private static string ReadZipEntryText(Stream zipStream, string entryPath)
    {
        zipStream.Position = 0;
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryPath)!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── Finding F1: unmodeled preset must not silently become a rectangle ──────────────────

    [Fact]
    public void Read_UnmodeledPresetGeometry_CapturesRawPresetInsteadOfDroppingIt()
    {
        using var pptx = BuildPptxWithUnmodeledPreset("pie");

        var reloaded = PptxPackageReader.Read(pptx);
        var shape = reloaded.Slides[0].Shapes[0];

        // FreeP still can't render "pie" natively, so the *kind* legitimately falls back...
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
        // ...but the original preset text must be preserved for round-tripping.
        shape.UnmodeledPresetGeometry.Should().Be("pie");
    }

    [Fact]
    public void RoundTrip_UnmodeledPresetGeometry_SurvivesSaveInsteadOfBecomingRectangle()
    {
        using var pptx = BuildPptxWithUnmodeledPreset("pie");
        var loaded = PptxPackageReader.Read(pptx);

        using var resaved = new MemoryStream();
        PptxPackageWriter.Write(loaded, resaved);

        var slideXml = ReadZipEntryText(resaved, "ppt/slides/slide1.xml");

        slideXml.Should().Contain("prst=\"pie\"",
            "the shape's true original preset must survive a FreeP open/save round trip");
        slideXml.Should().NotContain("prst=\"rect\"",
            "before the fix, the unmodeled preset was lost and silently replaced with rect");

        // And reading the re-saved file back confirms the model still carries it too.
        resaved.Position = 0;
        var reloadedAgain = PptxPackageReader.Read(resaved);
        reloadedAgain.Slides[0].Shapes[0].UnmodeledPresetGeometry.Should().Be("pie");
    }

    // ── Sibling: recognized presets are unaffected by the new preservation path ────────────

    [Fact]
    public void RoundTrip_RecognizedPreset_LeavesUnmodeledPresetGeometryNullAndPresetUnchanged()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Shape 1",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 1371600,
        });

        using var buffer = new MemoryStream();
        PptxPackageWriter.Write(presentation, buffer);
        buffer.Position = 0;
        var reloaded = PptxPackageReader.Read(buffer);
        var shape = reloaded.Slides[0].Shapes[0];

        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Ellipse);
        shape.UnmodeledPresetGeometry.Should().BeNull();
    }

    [Fact]
    public void ChangeAutoShapeKindCommand_ExplicitKindChange_ClearsPreservedUnmodeledPreset()
    {
        // Simulate a shape loaded from a deck with an unmodeled preset (e.g. "pie"): kind
        // fell back to Rectangle, but the original preset text is preserved.
        var presentation = Presentation.CreateEmpty();
        var shape = presentation.Slides[0].Shapes[0];
        shape.Kind = SlideShapeKind.AutoShape;
        shape.AutoShapeKind = DrawingShapeKind.Rectangle;
        shape.UnmodeledPresetGeometry = "pie";

        var bus = new PresentationCommandBus(presentation);
        bus.Execute(new ChangeAutoShapeKindCommand(0, shape.Id, DrawingShapeKind.Ellipse));

        // The user made a deliberate shape choice -- the stale preserved "pie" text must not
        // resurrect itself and override that choice on the next save.
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Ellipse);
        shape.UnmodeledPresetGeometry.Should().BeNull();

        bus.Undo();

        // Undo restores both the kind and whatever unmodeled preset had been preserved.
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
        shape.UnmodeledPresetGeometry.Should().Be("pie");
    }
}
