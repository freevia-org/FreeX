using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// theme-color-resolution F1: PptxPackageWriter's BuildColorEl must preserve the authored OOXML
/// role name (e.g. "tx1"/"bg1"/"tx2"/"bg2") on write-back instead of collapsing it to the
/// slot's canonical name (e.g. "dk1"/"lt1"). A p:clrMapOvr on the slide/layout can remap
/// tx1/bg1/tx2/bg2 to a different dk*/lt* slot than the Office default, so baking the
/// currently-resolved slot into the shape XML silently changes the shape's color the next
/// time the deck is opened under a different effective clrMap.
/// </summary>
public sealed class ShapeSchemeColorRoleNameWriteBackTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static byte[] WriteDeck(SchemeColorRef schemeColor)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Rect",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.Black, schemeColor)),
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static XElement ShapeSpPrSchemeClr(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry("ppt/slides/slide1.xml")!;
        using var entryStream = entry.Open();
        var root = XDocument.Load(entryStream).Root!;
        // p:cSld/p:spTree/p:sp/p:spPr/a:solidFill/a:schemeClr — the shape fill written by
        // BuildShapePropertiesEl(shape.Fill, ...) via BuildColorEl.
        var spPr = root.Descendants(A + "solidFill").Single().Parent!;
        return spPr.Element(A + "solidFill")!.Element(A + "schemeClr")!;
    }

    [Fact]
    public void ShapeFill_AuthoredAsTx1Role_PreservesRoleNameOnWriteBack()
    {
        // Authored (read from XML) as schemeClr val="tx1": RoleName="tx1", Slot resolved via the
        // DEFAULT clrMap (tx1 -> Dk1). Under a clrMapOvr, "tx1" and "dk1" are NOT interchangeable
        // on write-back -- only the raw role name round-trips correctly through remapping.
        var bytes = WriteDeck(new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 });
        var schemeClr = ShapeSpPrSchemeClr(bytes);

        // Before the fix this writes "dk1" (PptxColorReader.ToSchemeColorString(Slot)), silently
        // discarding the tx1 indirection. After the fix it must write back "tx1" verbatim.
        schemeClr.Attribute("val")!.Value.Should().Be("tx1",
            "the writer must preserve the authored role name so a clrMapOvr's tx1->slot " +
            "indirection survives the round trip, not bake in the currently-resolved slot");
    }

    [Fact]
    public void ShapeFill_NoRoleNameCaptured_FallsBackToCanonicalSlotName()
    {
        // Sibling/no-regression case: a SchemeColorRef built programmatically (no RoleName, e.g.
        // tests or in-app color-picker construction) must still write a valid schemeClr using the
        // slot's canonical name -- this is the documented fallback in SchemeColorRef.RoleName's
        // XML doc comment ("Null/empty ... in that case Slot is used directly").
        var bytes = WriteDeck(new SchemeColorRef { RoleName = null, Slot = ThemeColorSlot.Accent1, LumMod = 1.0 });
        var schemeClr = ShapeSpPrSchemeClr(bytes);

        schemeClr.Attribute("val")!.Value.Should().Be("accent1");
    }
}
