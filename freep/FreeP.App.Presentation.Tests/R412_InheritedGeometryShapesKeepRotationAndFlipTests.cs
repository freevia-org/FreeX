using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r412: a shape that inherits its position and size must still persist its rotation and flips.
///
/// <para>The writer guarded the whole <c>&lt;a:xfrm&gt;</c> element on the shape having an explicit
/// offset or extent. A placeholder that inherits its geometry has neither, so rotating or flipping
/// it wrote nothing at all: the edit survived in memory, vanished on save, and the user saw it
/// reappear unrotated on reopen -- with no error anywhere.</para>
///
/// <para>Found through the undo harness rather than by reading the writer. Flipping a shape produced
/// a byte-identical package, which the harness's "must actually change" gate refused to accept as an
/// undo test. The gate exists to stop a no-op passing for a restore; here it surfaced a product bug
/// instead.</para>
/// </summary>
public sealed class R412_InheritedGeometryShapesKeepRotationAndFlipTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static XElement? FirstShapeTransform(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = archive.GetEntry("ppt/slides/slide1.xml")!.Open();
        var document = XDocument.Load(entry);

        // Must be the SHAPE's own transform. A plain Descendants() search finds the shape TREE's
        // <p:grpSpPr><a:xfrm> first -- that one always exists, with zeroed off/ext -- which made the
        // first version of this test report a transform for a shape that had none.
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var shape = document.Descendants(p + "sp").Single();
        return shape.Element(p + "spPr")?.Element(A + "xfrm");
    }

    private static Presentation DeckWithInheritedGeometryShape(Action<SlideShape> edit)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape { Id = 2, Name = "Title", TextBody = new TextBody() };

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "title" });
        shape.TextBody!.Paragraphs.Add(paragraph);

        edit(shape);
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        return presentation;
    }

    [Fact]
    public void AnUneditedInheritedShapeStillWritesNoTransform()
    {
        // The control. The guard exists so an inherited placeholder does not get a bogus
        // <a:ext cx="0" cy="0">, which the reader treats as "deliberately hidden" -- widening it
        // must not start emitting a transform for shapes that have nothing to say.
        FirstShapeTransform(DeckWithInheritedGeometryShape(_ => { }))
            .Should().BeNull("a shape with no geometry, rotation or flip needs no transform element");
    }

    [Fact]
    public void FlippingAnInheritedShapeIsPersisted()
    {
        var transform = FirstShapeTransform(DeckWithInheritedGeometryShape(shape => shape.FlipH = true));

        transform.Should().NotBeNull("the flip has to reach the file or it is lost on reopen");
        transform!.Attribute("flipH")?.Value.Should().Be("1");
        transform.Element(A + "ext").Should().BeNull(
            "an inherited extent must not be baked in as an explicit zero -- that is the trap this " +
            "guard was protecting against, and the fix must not reintroduce it");
    }

    [Fact]
    public void RotatingAnInheritedShapeIsPersisted()
    {
        var transform = FirstShapeTransform(DeckWithInheritedGeometryShape(shape => shape.RotationDeg = 45));

        transform.Should().NotBeNull("the rotation has to reach the file or it is lost on reopen");
        transform!.Attribute("rot")?.Value.Should().Be(
            (45 * 60000).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "DrawingML stores rotation in 60000ths of a degree");
        transform.Element(A + "off").Should().BeNull("the shape still inherits its position");
    }
}
