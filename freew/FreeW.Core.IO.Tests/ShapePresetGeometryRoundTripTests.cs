using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for round133's fix: <see cref="DocxReader"/>'s <c>ShapeKindFromPreset</c> only
/// recognizes "roundRect" and "ellipse"; every OTHER <c>a:prstGeom/@prst</c> token (triangle, arrows,
/// stars, callouts, ... — 42 of the 45 shared presets) used to collapse to a plain
/// <see cref="ShapeKind.Rectangle"/>, silently discarding the shape's real geometry. The fix routes those
/// presets through the shared <see cref="Free.Shared.Drawing.ShapeGeometryBuilder"/> tier instead, so they
/// import with distinct <see cref="CustomGeometry"/> instead of losing their shape.
/// </summary>
public class ShapePresetGeometryRoundTripTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static TextDocument DocumentWith(Shape shape)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(para);
        return doc;
    }

    /// <summary>
    /// Writes a plain-rectangle shape, then overwrites its a:prstGeom/@prst token in the raw XML before
    /// reading it back — simulating a DOCX authored by real Word with an arbitrary preset FreeW's own
    /// writer never emits (the writer only ever emits rect/roundRect/ellipse/custGeom).
    /// </summary>
    private static Shape RoundTripWithPresetOverride(string preset)
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);

        using var writeStream = new MemoryStream();
        DocxWriter.Write(DocumentWith(shape), writeStream);
        writeStream.Position = 0;

        using var outStream = new MemoryStream();
        using (var srcZip = new ZipArchive(writeStream, ZipArchiveMode.Read))
        using (var dstZip = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in srcZip.Entries)
            {
                var newEntry = dstZip.CreateEntry(entry.FullName);
                using var src = entry.Open();
                using var dst = newEntry.Open();
                if (entry.FullName == "word/document.xml")
                {
                    var xdoc = XDocument.Load(src);
                    var prstGeom = xdoc.Descendants(A + "prstGeom").Single();
                    prstGeom.Attribute("prst")!.Value = preset;
                    xdoc.Save(dst);
                }
                else
                {
                    src.CopyTo(dst);
                }
            }
        }
        outStream.Position = 0;
        var read = DocxReader.Read(outStream);
        return read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
    }

    // ── The bug: unmapped presets must not collapse to a plain rectangle ──────────────────────

    [Theory]
    [InlineData("triangle")]
    [InlineData("star5")]
    [InlineData("rightArrow")]
    [InlineData("pentagon")]
    [InlineData("wedgeRectCallout")]
    public void UnmappedPreset_ImportsWithCustomGeometry_NotCollapsedToRectangle(string preset)
    {
        var shape = RoundTripWithPresetOverride(preset);

        shape.HasCustomGeometry.Should().BeTrue(
            $"preset '{preset}' has a dedicated shared-tier geometry and must not collapse to a plain rectangle");
    }

    [Fact]
    public void DistinctUnmappedPresets_ProduceDistinctGeometry_NotAllTheSameShape()
    {
        var triangle = RoundTripWithPresetOverride("triangle");
        var star = RoundTripWithPresetOverride("star5");
        var arrow = RoundTripWithPresetOverride("rightArrow");

        triangle.CustomGeometry.Should().NotBeNull();
        star.CustomGeometry.Should().NotBeNull();
        arrow.CustomGeometry.Should().NotBeNull();

        // If the bug were merely "always rectangle" fixed by "always the same other shape", these would be
        // indistinguishable. Assert each preset's synthesized geometry differs from the others', proving
        // the shared tier is actually being consulted per-preset rather than hard-coded to one substitute.
        var triangleKey = string.Join(";", triangle.CustomGeometry!.Segments.Select(s => (s.Kind, s.Point)));
        var starKey = string.Join(";", star.CustomGeometry!.Segments.Select(s => (s.Kind, s.Point)));
        var arrowKey = string.Join(";", arrow.CustomGeometry!.Segments.Select(s => (s.Kind, s.Point)));

        triangleKey.Should().NotBe(starKey, "triangle and star5 must render with their own distinct geometry");
        starKey.Should().NotBe(arrowKey, "star5 and rightArrow must render with their own distinct geometry");
        triangleKey.Should().NotBe(arrowKey, "triangle and rightArrow must render with their own distinct geometry");
    }

    [Fact]
    public void Triangle_Geometry_HasThreeDistinctVertices_NotFourRectangleCorners()
    {
        var shape = RoundTripWithPresetOverride("triangle");

        shape.HasCustomGeometry.Should().BeTrue();
        var vertices = shape.CustomGeometry!.Segments
            .Where(s => s.Kind is CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo)
            .Select(s => s.Point)
            .Distinct()
            .ToList();

        // A rectangle has 4 distinct corners; a triangle has 3. This directly distinguishes "collapsed to
        // rectangle" (the bug) from "synthesized as an actual triangle" (the fix).
        vertices.Should().HaveCount(3, "a triangle preset has three vertices, not a rectangle's four corners");
    }

    // ── Sibling / no-regression: the two dedicated presets must NOT go through custom geometry ──

    [Fact]
    public void RoundRectPreset_StillMapsToDedicatedShapeKind_NotCustomGeometry()
    {
        var read = DocxReader.Read(WriteThenReopen(Shape.Preset(ShapeKind.RoundedRectangle, 100, 50)));
        var shape = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        shape.Kind.Should().Be(ShapeKind.RoundedRectangle);
        shape.HasCustomGeometry.Should().BeFalse(
            "roundRect has a dedicated ShapeKind and must not be routed through the synthesized-geometry path");
    }

    [Fact]
    public void EllipsePreset_StillMapsToDedicatedShapeKind_NotCustomGeometry()
    {
        var read = DocxReader.Read(WriteThenReopen(Shape.Preset(ShapeKind.Ellipse, 80, 80)));
        var shape = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        shape.Kind.Should().Be(ShapeKind.Ellipse);
        shape.HasCustomGeometry.Should().BeFalse(
            "ellipse has a dedicated ShapeKind and must not be routed through the synthesized-geometry path");
    }

    [Fact]
    public void PlainRectPreset_StillMapsToRectangle_NotCustomGeometry()
    {
        // A genuine "rect" preset (Word's actual plain rectangle, not an unmapped substitute) must still
        // import as ShapeKind.Rectangle with no synthesized geometry — the fix only targets presets that
        // ShapeKindFromPreset does NOT already have a dedicated mapping for.
        var read = DocxReader.Read(WriteThenReopen(Shape.Preset(ShapeKind.Rectangle, 100, 60)));
        var shape = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        shape.Kind.Should().Be(ShapeKind.Rectangle);
        shape.HasCustomGeometry.Should().BeFalse("a genuine rect preset must not spuriously acquire custom geometry");
    }

    private static MemoryStream WriteThenReopen(Shape shape)
    {
        var stream = new MemoryStream();
        DocxWriter.Write(DocumentWith(shape), stream);
        stream.Position = 0;
        return stream;
    }
}
