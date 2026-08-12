using System.IO.Compression;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 135: <c>p:sldLayout/@showMasterSp</c> ("Hide Background Graphics" authored against a
/// layout in Slide Master view) was neither read nor written — <see cref="SlideLayout"/> had no
/// field for it — so a layout that hides master shapes lost the setting on save AND still
/// rendered the master's decoration shapes on every slide using that layout. Covers the
/// reader/writer round-trip plus the <see cref="SlideCompositor"/> gate, with a sibling
/// no-regression test for the default (true) case next door.
/// </summary>
public sealed class R135_LayoutShowMasterShapesTests
{
    private static SlideShape MasterDecorationShape(uint id) => new()
    {
        Id = id,
        Kind = SlideShapeKind.AutoShape,
        AutoShapeKind = DrawingShapeKind.Rectangle,
        OffsetXEmu = 100, OffsetYEmu = 200, ExtentCxEmu = 1000, ExtentCyEmu = 600,
        Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33))),
    };

    // ── Reader/writer round-trip ─────────────────────────────────────────────────────────────

    [Fact]
    public void LayoutShowMasterShapesRoundTripsFalseAndOmitsAttributeWhenDefault()
    {
        // Bare Presentation (not CreateEmpty()): CreateEmpty() pre-seeds its own master + layout,
        // which would make a "there are exactly two layouts" assertion fragile/misleading.
        var presentation = new Presentation();
        var master = new SlideMaster { Id = "rId1" };
        presentation.Masters.Add(master);

        var hiddenLayout = new SlideLayout { Id = "rIdL1", MasterId = master.Id, ShowMasterShapes = false };
        var shownLayout = new SlideLayout { Id = "rIdL2", MasterId = master.Id, ShowMasterShapes = true };
        presentation.Layouts.Add(hiddenLayout);
        presentation.Layouts.Add(shownLayout);

        var slide = new Slide { LayoutId = hiddenLayout.Id };
        presentation.Slides.Add(slide);

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);

        using (var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read))
        {
            var layoutEntries = archive.Entries
                .Where(e => e.FullName.StartsWith("ppt/slideLayouts/slideLayout", StringComparison.Ordinal)
                            && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                .OrderBy(e => e.FullName, StringComparer.Ordinal)
                .ToList();
            layoutEntries.Should().HaveCount(2, "the empty-presentation seed layout was replaced by the two authored layouts");

            using var reader1 = new StreamReader(layoutEntries[0].Open());
            var layout1Xml = XDocument.Parse(reader1.ReadToEnd());
            layout1Xml.Root!.Attribute("showMasterSp")!.Value.Should().Be("0",
                "the hidden layout must write an explicit showMasterSp=0");

            using var reader2 = new StreamReader(layoutEntries[1].Open());
            var layout2Xml = XDocument.Parse(reader2.ReadToEnd());
            layout2Xml.Root!.Attribute("showMasterSp").Should().BeNull(
                "the default (true) value must not be written");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(output.ToArray()));
        reloaded.Layouts.Should().HaveCount(2);
        reloaded.Layouts[0].ShowMasterShapes.Should().BeFalse("the authored showMasterSp=0 must survive the round-trip");
        reloaded.Layouts[1].ShowMasterShapes.Should().BeTrue("a sibling layout with the default must not inherit the other layout's override");
    }

    // ── Compositor gate ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compose_HidesMasterDecorationWhenLayoutShowMasterShapesIsFalse()
    {
        // Presentation- and slide-level gates both stay at their defaults (true): only the
        // layout's authored showMasterSp should decide visibility here.
        var presentation = new Presentation();
        presentation.ShowMasterShapes.Should().BeTrue();

        var master = new SlideMaster { Id = "m1" };
        master.Placeholders.Add(MasterDecorationShape(10));
        presentation.Masters.Add(master);

        var hiddenLayout = new SlideLayout { Id = "l1", MasterId = "m1", ShowMasterShapes = false };
        presentation.Layouts.Add(hiddenLayout);

        var slide = new Slide { LayoutId = hiddenLayout.Id };
        slide.ShowMasterShapes.Should().BeTrue("the slide itself does not override showMasterSp");
        presentation.Slides.Add(slide);

        SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Should().BeEmpty("the layout authored showMasterSp=false: its master's decoration must not bleed through");
    }

    [Fact]
    public void Compose_StillShowsMasterDecorationWhenLayoutShowMasterShapesIsDefaultTrue()
    {
        // Sibling no-regression test: a layout that does NOT hide master shapes (the default)
        // must keep showing the master's decoration exactly as before this fix.
        var presentation = new Presentation();
        var master = new SlideMaster { Id = "m1" };
        master.Placeholders.Add(MasterDecorationShape(10));
        presentation.Masters.Add(master);

        var shownLayout = new SlideLayout { Id = "l1", MasterId = "m1" };
        shownLayout.ShowMasterShapes.Should().BeTrue();
        presentation.Layouts.Add(shownLayout);

        var slide = new Slide { LayoutId = shownLayout.Id };
        presentation.Slides.Add(slide);

        SlideCompositor.Compose(presentation, slide)
            .OfType<DrawOp.Shape>()
            .Should().HaveCount(1, "a layout with the default showMasterSp=true must still show the master's decoration");
    }
}
