namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Covers the SetSlideSizeCommand "Ensure Fit" scaling fix (round 158, finding freep-slide-size
/// F1): changing the slide size must rescale every shape's position/size along with the canvas,
/// or content that fit the old slide ends up cropped/off-slide on the new one.
/// </summary>
public sealed class SetSlideSizeCommandScalingTests
{
    private static Presentation MakePresentation(long cx = 12_192_000L, long cy = 6_858_000L)
    {
        var p = new Presentation { SlideSizeCxEmu = cx, SlideSizeCyEmu = cy };
        p.Slides.Add(new Slide());
        return p;
    }

    private static SlideShape MakeShape(uint id, long offX, long offY, long extCx, long extCy) => new()
    {
        Id = id,
        Name = $"S{id}",
        Kind = SlideShapeKind.AutoShape,
        OffsetXEmu = offX,
        OffsetYEmu = offY,
        ExtentCxEmu = extCx,
        ExtentCyEmu = extCy,
    };

    // ── The defect: shrinking 16:9 -> 4:3 must not leave a shape cropped off the new canvas ──

    [Fact]
    public void Apply_ShrinkingToNarrowerAspect_ScalesShapeSoItStillFitsOnTheNewCanvas()
    {
        var p = MakePresentation(); // 16:9 default: 12,192,000 x 6,858,000
        // A full-bleed background rectangle sized to the original 16:9 canvas.
        var background = MakeShape(1, 0, 0, 12_192_000L, 6_858_000L);
        p.Slides[0].Shapes.Add(background);

        // Standard 4:3 = 9,144,000 x 6,858,000 (the ribbon's quick "Standard (4:3)" button).
        var cmd = new SetSlideSizeCommand(9_144_000L, 6_858_000L);
        cmd.Apply(p);

        p.SlideSizeCxEmu.Should().Be(9_144_000L);

        // The regression: before the fix the shape kept its original 12,192,000-wide extent,
        // so its right edge (12,192,000) sat far beyond the new 9,144,000-wide canvas -- cropped
        // in the editor, Slide Show, and PDF export. After the fix it must be scaled down so it
        // fits entirely within the new slide bounds.
        (background.OffsetXEmu + background.ExtentCxEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu);
        (background.OffsetYEmu + background.ExtentCyEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCyEmu);

        // Ensure Fit uses the smaller of the two axis ratios (9,144,000/12,192,000 = 0.75 here,
        // since height is unchanged) applied uniformly to both axes.
        background.ExtentCxEmu.Should().Be(9_144_000L);
        background.ExtentCyEmu.Should().Be((long)Math.Round(6_858_000L * 0.75));
    }

    [Fact]
    public void Apply_ShapeNearRightEdge_NoLongerExtendsPastNewNarrowerSlide()
    {
        var p = MakePresentation();
        // A shape positioned near the right edge of the original 16:9 slide.
        var shape = MakeShape(2, 11_000_000L, 1_000_000L, 1_000_000L, 500_000L);
        p.Slides[0].Shapes.Add(shape);

        new SetSlideSizeCommand(9_144_000L, 6_858_000L).Apply(p);

        (shape.OffsetXEmu + shape.ExtentCxEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu,
            "the shape fit inside the old canvas, so a uniform Ensure-Fit scale must keep it inside the new one");
    }

    // ── Undo must restore the exact original geometry, not merely the old slide size ──

    [Fact]
    public void Revert_RestoresOriginalSlideSizeAndOriginalShapeGeometryExactly()
    {
        var p = MakePresentation();
        var shape = MakeShape(3, 11_000_000L, 1_000_000L, 1_000_000L, 500_000L);
        p.Slides[0].Shapes.Add(shape);

        var cmd = new SetSlideSizeCommand(9_144_000L, 6_858_000L);
        cmd.Apply(p);
        shape.OffsetXEmu.Should().NotBe(11_000_000L); // sanity: it really did move

        cmd.Revert(p);

        p.SlideSizeCxEmu.Should().Be(12_192_000L);
        p.SlideSizeCyEmu.Should().Be(6_858_000L);
        shape.OffsetXEmu.Should().Be(11_000_000L);
        shape.OffsetYEmu.Should().Be(1_000_000L);
        shape.ExtentCxEmu.Should().Be(1_000_000L);
        shape.ExtentCyEmu.Should().Be(500_000L);
    }

    // ── Group children (absolute slide-space coords) must scale together with the group ──

    [Fact]
    public void Apply_GroupShape_ScalesGroupAndDescendantsBySameFactorKeepingThemInSync()
    {
        var p = MakePresentation();
        var child = MakeShape(11, 10_500_000L, 500_000L, 400_000L, 300_000L);
        var group = new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 10_000_000L,
            OffsetYEmu = 400_000L,
            ExtentCxEmu = 1_000_000L,
            ExtentCyEmu = 500_000L,
        };
        group.Children.Add(child);
        p.Slides[0].Shapes.Add(group);

        new SetSlideSizeCommand(9_144_000L, 6_858_000L).Apply(p);

        double expectedScale = 9_144_000.0 / 12_192_000.0; // 0.75, the binding (smaller) ratio
        group.OffsetXEmu.Should().Be((long)Math.Round(10_000_000L * expectedScale));
        child.OffsetXEmu.Should().Be((long)Math.Round(10_500_000L * expectedScale));
        child.ExtentCxEmu.Should().Be((long)Math.Round(400_000L * expectedScale));
    }

    // ── Sibling / no-regression: growing the slide, or an unchanged aspect ratio ──

    [Fact]
    public void Apply_SameAspectRatioResize_UniformlyScalesRatherThanDistorting()
    {
        var p = MakePresentation(); // 16:9
        var shape = MakeShape(4, 0, 0, 12_192_000L, 6_858_000L);
        p.Slides[0].Shapes.Add(shape);

        // A different absolute size that keeps the exact same 16:9 ratio.
        new SetSlideSizeCommand(6_096_000L, 3_429_000L).Apply(p);

        shape.ExtentCxEmu.Should().Be(6_096_000L);
        shape.ExtentCyEmu.Should().Be(3_429_000L);
    }

    [Fact]
    public void Apply_NoSizeChange_LeavesShapeGeometryUntouched()
    {
        var p = MakePresentation();
        var shape = MakeShape(5, 123_456L, 654_321L, 200_000L, 100_000L);
        p.Slides[0].Shapes.Add(shape);

        new SetSlideSizeCommand(p.SlideSizeCxEmu, p.SlideSizeCyEmu).Apply(p);

        shape.OffsetXEmu.Should().Be(123_456L);
        shape.OffsetYEmu.Should().Be(654_321L);
        shape.ExtentCxEmu.Should().Be(200_000L);
        shape.ExtentCyEmu.Should().Be(100_000L);
    }

    // ── Round 168, finding freep-slide-size F1 ─────────────────────────────────────────────
    // A slide's placeholder shape very commonly has NO explicit xfrm of its own (Offset/Extent
    // left at 0) and inherits its position/size from the slide layout (or master) instead --
    // exactly what EditingSession.InsertSlide -> Slide.Title produces. That inherited geometry
    // must be rescaled too, or PlaceholderResolver.ResolveAnchor keeps handing back the OLD
    // canvas's absolute EMU coordinates after the slide size changes.

    private static SlideLayout MakeLayoutWithTitlePlaceholder(long offX, long offY, long extCx, long extCy, string masterId)
    {
        var layout = new SlideLayout { Id = "layout1", MasterId = masterId };
        layout.Placeholders.Add(new SlideShape
        {
            Id = 100,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = offX,
            OffsetYEmu = offY,
            ExtentCxEmu = extCx,
            ExtentCyEmu = extCy,
        });
        return layout;
    }

    private static SlideShape MakeInheritedTitleShape(uint id) => new()
    {
        Id = id,
        Name = $"S{id}",
        Kind = SlideShapeKind.AutoShape,
        Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
        // Deliberately no Offset/Extent -- mirrors Slide.cs's Title setter, which leaves these
        // at their default 0 so the shape inherits geometry from the layout placeholder.
    };

    [Fact]
    public void Apply_LayoutInheritedTitlePlaceholder_ScalesLayoutGeometrySoResolvedAnchorFitsNewCanvas()
    {
        var p = MakePresentation(); // 16:9: 12,192,000 x 6,858,000
        var layout = MakeLayoutWithTitlePlaceholder(
            offX: 838_200L, offY: 365_125L, extCx: 10_515_600L, extCy: 1_325_245L, masterId: "master1");
        p.Layouts.Add(layout);

        var master = new SlideMaster { Id = "master1" };
        p.Masters.Add(master);

        var slide = p.Slides[0];
        slide.LayoutId = layout.Id;
        var titleShape = MakeInheritedTitleShape(200);
        slide.Shapes.Add(titleShape);

        // Sanity: the layout placeholder fits inside the old 16:9 canvas.
        (layout.Placeholders[0].OffsetXEmu + layout.Placeholders[0].ExtentCxEmu)
            .Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu);

        // Ribbon's "Standard (4:3)" preset.
        new SetSlideSizeCommand(9_144_000L, 6_858_000L).Apply(p);

        p.SlideSizeCxEmu.Should().Be(9_144_000L);

        // The regression: before the fix, layout.Placeholders[0] kept its original 16:9-canvas
        // absolute EMU coordinates, so the resolved anchor (what SlideCompositor actually draws)
        // overflowed the new, narrower canvas.
        var resolved = FreeP.App.Compositor.PlaceholderResolver.ResolveAnchor(titleShape, slide, p);
        (resolved.OffsetXEmu + resolved.ExtentCxEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu,
            "the layout-inherited title placeholder fit the old canvas, so Ensure-Fit must rescale " +
            "the layout geometry it resolves through so it still fits the new one");

        // And the layout's own stored geometry was in fact scaled (not just left alone).
        layout.Placeholders[0].ExtentCxEmu.Should().Be((long)Math.Round(10_515_600L * 0.75));
    }

    [Fact]
    public void Apply_MasterInheritedPlaceholder_ScalesMasterGeometryToo()
    {
        var p = MakePresentation();
        var master = new SlideMaster { Id = "master1" };
        master.Placeholders.Add(new SlideShape
        {
            Id = 300,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 838_200L,
            OffsetYEmu = 365_125L,
            ExtentCxEmu = 10_515_600L,
            ExtentCyEmu = 1_325_245L,
        });
        p.Masters.Add(master);

        // A layout with no placeholders of its own -- resolution must fall through to the master.
        var layout = new SlideLayout { Id = "layout1", MasterId = "master1" };
        p.Layouts.Add(layout);

        var slide = p.Slides[0];
        slide.LayoutId = layout.Id;
        var titleShape = MakeInheritedTitleShape(301);
        slide.Shapes.Add(titleShape);

        new SetSlideSizeCommand(9_144_000L, 6_858_000L).Apply(p);

        master.Placeholders[0].ExtentCxEmu.Should().Be((long)Math.Round(10_515_600L * 0.75));

        var resolved = FreeP.App.Compositor.PlaceholderResolver.ResolveAnchor(titleShape, slide, p);
        (resolved.OffsetXEmu + resolved.ExtentCxEmu).Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu);
    }

    [Fact]
    public void Revert_LayoutAndMasterPlaceholderGeometry_RestoredExactly()
    {
        var p = MakePresentation();
        var layout = MakeLayoutWithTitlePlaceholder(
            offX: 838_200L, offY: 365_125L, extCx: 10_515_600L, extCy: 1_325_245L, masterId: "master1");
        p.Layouts.Add(layout);
        var master = new SlideMaster { Id = "master1" };
        master.Placeholders.Add(new SlideShape
        {
            Id = 400,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 1_000_000L,
            OffsetYEmu = 2_000_000L,
            ExtentCxEmu = 3_000_000L,
            ExtentCyEmu = 4_000_000L,
        });
        p.Masters.Add(master);

        var cmd = new SetSlideSizeCommand(9_144_000L, 6_858_000L);
        cmd.Apply(p);
        layout.Placeholders[0].ExtentCxEmu.Should().NotBe(10_515_600L); // sanity: it moved

        cmd.Revert(p);

        layout.Placeholders[0].OffsetXEmu.Should().Be(838_200L);
        layout.Placeholders[0].ExtentCxEmu.Should().Be(10_515_600L);
        master.Placeholders[0].OffsetXEmu.Should().Be(1_000_000L);
        master.Placeholders[0].ExtentCxEmu.Should().Be(3_000_000L);
    }

    // ── Round 168, finding freep-slide-size F2 ─────────────────────────────────────────────
    // SlideCompositor.ComposeTable derives the table's actually-drawn width/height purely from
    // TableShape.ColumnWidthsEmu / TableRow.HeightEmu, ignoring the shape's own ExtentCx/CyEmu.
    // Ensure-Fit rescaling the shape's outer frame alone therefore has no visible effect -- the
    // table keeps its original footprint at the new (rescaled) origin.

    private static SlideShape MakeTableShape(uint id, long offX, long offY, long extCx, long extCy,
        params long[] colWidths)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange(colWidths);
        table.Rows.Add(new TableRow { HeightEmu = extCy });

        return new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.Table,
            OffsetXEmu = offX,
            OffsetYEmu = offY,
            ExtentCxEmu = extCx,
            ExtentCyEmu = extCy,
            Table = table,
        };
    }

    [Fact]
    public void Apply_TableShape_ScalesColumnWidthsAndRowHeightsWithTheSlide()
    {
        var p = MakePresentation(); // 12,192,000 x 6,858,000
        var tableShape = MakeTableShape(500,
            offX: 500_000L, offY: 500_000L, extCx: 10_000_000L, extCy: 1_000_000L,
            colWidths: new long[] { 5_000_000L, 5_000_000L });
        p.Slides[0].Shapes.Add(tableShape);

        // A 0.5x binding scale on X (matches the F2 evidence repro).
        new SetSlideSizeCommand(6_096_000L, 6_858_000L).Apply(p);

        var table = tableShape.Table!;
        table.ColumnWidthsEmu.Sum().Should().Be(5_000_000L,
            "the table's own column widths must shrink with the slide, since SlideCompositor.ComposeTable " +
            "derives the drawn table width from ColumnWidthsEmu rather than the shape's ExtentCxEmu");
        table.Rows[0].HeightEmu.Should().Be(500_000L);

        // The table's actually-drawn right edge (origin + summed column widths) must fit the new canvas.
        (tableShape.OffsetXEmu + table.ColumnWidthsEmu.Sum()).Should().BeLessThanOrEqualTo(p.SlideSizeCxEmu);
    }

    [Fact]
    public void Revert_TableShape_RestoresOriginalColumnWidthsAndRowHeights()
    {
        var p = MakePresentation();
        var tableShape = MakeTableShape(501,
            offX: 500_000L, offY: 500_000L, extCx: 10_000_000L, extCy: 1_000_000L,
            colWidths: new long[] { 4_000_000L, 6_000_000L });
        p.Slides[0].Shapes.Add(tableShape);

        var cmd = new SetSlideSizeCommand(6_096_000L, 6_858_000L);
        cmd.Apply(p);
        cmd.Revert(p);

        var table = tableShape.Table!;
        table.ColumnWidthsEmu.Should().Equal(4_000_000L, 6_000_000L);
        table.Rows[0].HeightEmu.Should().Be(1_000_000L);
    }

    // ── Sibling / no-regression: a shape with its OWN explicit geometry (the pre-existing,
    // already-correct path) must keep working exactly as before these two fixes. ──────────────

    [Fact]
    public void Apply_ShapeWithOwnExplicitGeometry_StillScaledDirectlyNotThroughPlaceholderPath()
    {
        var p = MakePresentation();
        var layout = MakeLayoutWithTitlePlaceholder(
            offX: 100_000L, offY: 100_000L, extCx: 200_000L, extCy: 300_000L, masterId: "master1");
        p.Layouts.Add(layout);
        p.Masters.Add(new SlideMaster { Id = "master1" });

        var slide = p.Slides[0];
        slide.LayoutId = layout.Id;

        // This placeholder shape carries its OWN explicit geometry (non-zero extent) -- the
        // pre-existing, already-correct path that must be untouched by the F1 fix.
        var ownGeometryShape = new SlideShape
        {
            Id = 600,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 11_000_000L,
            OffsetYEmu = 1_000_000L,
            ExtentCxEmu = 1_000_000L,
            ExtentCyEmu = 500_000L,
        };
        slide.Shapes.Add(ownGeometryShape);

        new SetSlideSizeCommand(9_144_000L, 6_858_000L).Apply(p);

        // Resolved anchor must come from the shape's own (scaled) geometry, not the layout's.
        var resolved = FreeP.App.Compositor.PlaceholderResolver.ResolveAnchor(ownGeometryShape, slide, p);
        resolved.OffsetXEmu.Should().Be((long)Math.Round(11_000_000L * 0.75));
        resolved.ExtentCxEmu.Should().Be((long)Math.Round(1_000_000L * 0.75));
    }
}
