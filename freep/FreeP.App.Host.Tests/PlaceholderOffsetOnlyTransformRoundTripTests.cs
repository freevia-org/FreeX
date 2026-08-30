using System;
using System.IO;
using System.Linq;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// freep-masters-layouts F2 + F3: a placeholder authored with only &lt;a:off&gt; (explicit
/// position override, inherited size) -- legal per ECMA-376 CT_Transform2D, where &lt;a:off&gt;
/// and &lt;a:ext&gt; are independently optional -- must keep its own position after resolution
/// (F2) AND must not have that position's missing extent baked into the file as an explicit
/// zero-extent &lt;a:ext&gt; on save, which would flip <see cref="SlideShape.HasExplicitZeroExtentTransform"/>
/// and permanently hide the placeholder on the next open (F3).
/// </summary>
public sealed class PlaceholderOffsetOnlyTransformRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.OffOnlyXfrmTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    /// <summary>
    /// The full story in one round trip: author a title placeholder with its own explicit
    /// position but no extent of its own (so it inherits size from the layout), save it, reopen
    /// it, and confirm it is still exactly where the user put it -- not moved back to the
    /// layout's position (F2) and not vanished as a "deliberately hidden" zero-extent
    /// placeholder (F3).
    /// </summary>
    [Fact]
    public void OffOnlyPlaceholder_KeepsOwnPosition_AndStaysVisible_AfterSaveAndReopen()
    {
        const long ownOffsetX = 9_000_000;
        const long ownOffsetY = 9_000_000;
        const long layoutOffsetX = 500_000;
        const long layoutOffsetY = 500_000;
        const long layoutExtentCx = 4_000_000;
        const long layoutExtentCy = 3_000_000;

        var pres = Presentation.CreateEmpty();
        var layout = pres.Layouts[0];
        // Layout carries the placeholder's normal (fully explicit) position + size.
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = layoutOffsetX,
            OffsetYEmu = layoutOffsetY,
            ExtentCxEmu = layoutExtentCx,
            ExtentCyEmu = layoutExtentCy
        });

        // The slide's own title placeholder: as if read from a source file with
        // <a:xfrm><a:off x="9000000" y="9000000"/></a:xfrm> -- an explicit position override
        // with NO <a:ext> child, exactly what PptxPackageReader.ReadSpPr produces for that XML.
        pres.Slides[0].Title = "Moved Title";
        var titleShape = pres.Slides[0].Shapes.Find(s => s.Placeholder?.Type == PlaceholderType.Title)!;
        titleShape.OffsetXEmu = ownOffsetX;
        titleShape.OffsetYEmu = ownOffsetY;
        // ExtentCxEmu/CyEmu deliberately left at their model default (0) -- no <a:ext> was
        // present in the source, so the shape has no extent of its own.
        titleShape.ExtentCxEmu.Should().Be(0);
        titleShape.ExtentCyEmu.Should().Be(0);
        titleShape.HasExplicitZeroExtentTransform.Should().BeFalse();

        // ── F2: resolution must keep the shape's own offset and inherit only the extent ──
        var anchorBeforeSave = PlaceholderResolver.ResolveAnchor(titleShape, pres.Slides[0], pres);
        anchorBeforeSave.OffsetXEmu.Should().Be(ownOffsetX,
            "the placeholder's own explicit <a:off> must not be discarded just because <a:ext> was inherited");
        anchorBeforeSave.OffsetYEmu.Should().Be(ownOffsetY);
        anchorBeforeSave.ExtentCxEmu.Should().Be(layoutExtentCx,
            "the missing extent must still inherit from the layout");
        anchorBeforeSave.ExtentCyEmu.Should().Be(layoutExtentCy);

        // ── Save + reopen (the real, shipped writer + reader) ──
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        var reloaded = PptxPackageReader.Read(path);

        var reloadedSlide = reloaded.Slides[0];
        var reloadedTitle = reloadedSlide.Shapes.Find(s => s.Placeholder?.Type == PlaceholderType.Title)!;

        // ── F3: the writer must not have baked the inherited (still-zero) extent in as an
        // explicit <a:ext cx="0" cy="0">, which would read back as "deliberately hidden". ──
        reloadedTitle.HasExplicitZeroExtentTransform.Should().BeFalse(
            "an off-only placeholder must not turn into a 'deliberately hidden' zero-extent placeholder after a save + reopen round trip");
        reloadedTitle.OffsetXEmu.Should().Be(ownOffsetX,
            "the user's explicit position must survive the round trip verbatim");
        reloadedTitle.OffsetYEmu.Should().Be(ownOffsetY);

        // ── The whole point: resolve again after reopening and confirm the placeholder is
        // still exactly where the user put it, not back at the layout's position and not gone. ──
        var anchorAfterReload = PlaceholderResolver.ResolveAnchor(reloadedTitle, reloadedSlide, reloaded);
        anchorAfterReload.OffsetXEmu.Should().Be(ownOffsetX,
            "the round trip must not silently move the placeholder back to the layout's position");
        anchorAfterReload.OffsetYEmu.Should().Be(ownOffsetY);
        anchorAfterReload.ExtentCxEmu.Should().Be(layoutExtentCx,
            "the layout's size must still be inheritable after reopening, dynamically, not frozen from the first resolve");
        anchorAfterReload.ExtentCyEmu.Should().Be(layoutExtentCy);

        var opsAfterReload = SlideCompositor.Compose(reloaded, reloadedSlide);
        opsAfterReload.OfType<DrawOp.Shape>().Should().ContainSingle(
            "the placeholder's text must still render -- it must not have vanished as a hidden shape");
    }

    /// <summary>
    /// No-regression sibling: a placeholder with NO geometry of its own at all (neither off nor
    /// ext -- the ordinary, overwhelmingly common case) must still fully inherit both position
    /// and size from the layout, before and after a save + reopen round trip.
    /// </summary>
    [Fact]
    public void FullyInheritedPlaceholder_StillInheritsBothOffsetAndExtent_AfterSaveAndReopen()
    {
        const long layoutOffsetX = 457_200;
        const long layoutOffsetY = 274_320;
        const long layoutExtentCx = 8_229_600;
        const long layoutExtentCy = 1_143_000;

        var pres = Presentation.CreateEmpty();
        var layout = pres.Layouts[0];
        layout.Placeholders.Add(new SlideShape
        {
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = layoutOffsetX,
            OffsetYEmu = layoutOffsetY,
            ExtentCxEmu = layoutExtentCx,
            ExtentCyEmu = layoutExtentCy
        });

        pres.Slides[0].Title = "Untouched Title";
        var titleShape = pres.Slides[0].Shapes.Find(s => s.Placeholder?.Type == PlaceholderType.Title)!;
        // No geometry set at all -- matches the ordinary "never had an <a:xfrm>" case.

        var anchorBeforeSave = PlaceholderResolver.ResolveAnchor(titleShape, pres.Slides[0], pres);
        anchorBeforeSave.OffsetXEmu.Should().Be(layoutOffsetX);
        anchorBeforeSave.OffsetYEmu.Should().Be(layoutOffsetY);
        anchorBeforeSave.ExtentCxEmu.Should().Be(layoutExtentCx);
        anchorBeforeSave.ExtentCyEmu.Should().Be(layoutExtentCy);

        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        var reloaded = PptxPackageReader.Read(path);

        var reloadedSlide = reloaded.Slides[0];
        var reloadedTitle = reloadedSlide.Shapes.Find(s => s.Placeholder?.Type == PlaceholderType.Title)!;

        reloadedTitle.HasExplicitZeroExtentTransform.Should().BeFalse();
        reloadedTitle.OffsetXEmu.Should().Be(0, "a fully-inherited placeholder must still carry no geometry of its own");
        reloadedTitle.ExtentCxEmu.Should().Be(0);

        var anchorAfterReload = PlaceholderResolver.ResolveAnchor(reloadedTitle, reloadedSlide, reloaded);
        anchorAfterReload.OffsetXEmu.Should().Be(layoutOffsetX);
        anchorAfterReload.OffsetYEmu.Should().Be(layoutOffsetY);
        anchorAfterReload.ExtentCxEmu.Should().Be(layoutExtentCx);
        anchorAfterReload.ExtentCyEmu.Should().Be(layoutExtentCy);
    }
}
