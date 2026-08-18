using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r143 findings freep-group-effects-dropped-on-save (HIGH) and freep-autofit-2 (MED):
/// group-level shape effects (shadow/glow/soft edge) were parsed on read but never written back
/// on save, and a cached <c>a:normAutofit</c> fontScale/lnSpcReduction was re-emitted verbatim on
/// save with no attempt to catch up with the shape's current geometry/text.
/// </summary>
public sealed class R143_ShapeEffectsAndAutoFitPersistenceTests
{
    private static PresentationModel MakePresentation() => PresentationModel.CreateEmpty();

    private static SlideShape MakeShapeWithText(TextBody body, long extentCxEmu = 4572000, long extentCyEmu = 3000000)
    {
        return new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = extentCxEmu,
            ExtentCyEmu = extentCyEmu,
            TextBody = body
        };
    }

    // ─── freep-group-effects-dropped-on-save ───────────────────────────────────

    [Fact]
    public void RoundTrip_GroupLevelOuterShadow_SurvivesWriteRead()
    {
        var child = new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        };

        var group = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Effects = new ShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowColor = new SrgbColor(0x33, 0x66, 0x99),
                OuterShadowAlpha = 0xC0,
                OuterShadowBlurRadEmu = 40000,
                OuterShadowDistEmu = 23000,
                OuterShadowDirDeg = 45.0
            }
        };
        group.Children.Add(child);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(group);

        using var ms = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var roundTripped = p2.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Group);
        roundTripped.Effects.Should().NotBeNull(
            "the group's own a:grpSpPr/a:effectLst must round-trip, not be dropped on save");
        roundTripped.Effects!.HasOuterShadow.Should().BeTrue();
        roundTripped.Effects.OuterShadowBlurRadEmu.Should().Be(40000);
        roundTripped.Effects.OuterShadowDistEmu.Should().Be(23000);
    }

    [Fact]
    public void RoundTrip_GroupWithoutEffects_StillOmitsEffectLst()
    {
        // Sibling: a group with NO effects must not suddenly grow an (empty) effectLst on save,
        // and its children/geometry must still round-trip normally.
        var child = new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        };

        var group = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Group,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400
        };
        group.Children.Add(child);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(group);

        using var ms = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var roundTripped = p2.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Group);
        roundTripped.Effects.Should().BeNull();
        roundTripped.Children.Should().HaveCount(1);
    }

    [Fact]
    public void RoundTrip_OrdinaryShapeOuterShadow_StillSurvivesWriteRead()
    {
        // Sibling: the pre-existing (already-working) shape-level effects path must be untouched
        // by the BuildGrpSpPrEl change.
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Effects = new ShapeEffects
            {
                HasOuterShadow = true,
                OuterShadowColor = new SrgbColor(0x11, 0x22, 0x33),
                OuterShadowBlurRadEmu = 12000,
                OuterShadowDistEmu = 5000,
                OuterShadowDirDeg = 10.0
            }
        };

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(shape);

        using var ms = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var roundTripped = p2.Slides[0].Shapes.Single();
        roundTripped.Effects.Should().NotBeNull();
        roundTripped.Effects!.HasOuterShadow.Should().BeTrue();
        roundTripped.Effects.OuterShadowBlurRadEmu.Should().Be(12000);
    }

    // ─── freep-autofit-2 ────────────────────────────────────────────────────────

    [Fact]
    public void Save_NormalAutoFitTextOverflowingCurrentBox_RecomputesFontScaleInsteadOfStaleVerbatimValue()
    {
        // A normAutofit box that has never been shrunk (no cached fontScale, matching a shape
        // whose text/box just changed since import) but whose CURRENT geometry cannot possibly
        // hold the authored text at 100%. Before the fix, PptxPackageWriter re-emits whatever was
        // in the model with no regard for the current box, so an absent/unset FontScalePPT stays
        // absent forever and the saved file keeps claiming (via the fontScale attribute's absence)
        // that the text fits unshrunk.
        var body = new TextBody { AutoFit = true };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = new string('x', 200), FontSizePt = 24.0 });
        body.Paragraphs.Add(para);

        // 1in x 0.25in box (72pt x 18pt), well under what 200 chars of 24pt text needs.
        var shape = MakeShapeWithText(body, extentCxEmu: 914400, extentCyEmu: 228600);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(shape);

        using var ms = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var roundTripped = p2.Slides[0].Shapes[0].TextBody!;
        roundTripped.FontScalePPT.Should().NotBeNull(
            "the writer must recompute and persist a shrink for text that clearly overflows the current box");
        roundTripped.FontScalePPT!.Value.Should().Be(60000, "the box is small enough to hit the 60% runtime floor");
        roundTripped.LnSpcReductionPPT.Should().Be(20000, "even at the 60% floor the text still overflows, capping line-spacing reduction at 20%");
    }

    [Fact]
    public void Save_NormalAutoFitTextThatAlreadyFits_LeavesCachedFontScaleUnchanged()
    {
        // Sibling: a normAutofit box whose cached scale is already correct for a roomy box must
        // not be perturbed by the new recompute step.
        var body = new TextBody { AutoFit = true, FontScalePPT = 100000 };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Hi", FontSizePt = 18.0 });
        body.Paragraphs.Add(para);

        // 10in x 7.5in box — ample room for two characters of 18pt text.
        var shape = MakeShapeWithText(body, extentCxEmu: 9144000, extentCyEmu: 6858000);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(shape);

        using var ms = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var roundTripped = p2.Slides[0].Shapes[0].TextBody!;
        roundTripped.FontScalePPT.Should().Be(100000);
        roundTripped.LnSpcReductionPPT.Should().BeNull();
    }

    [Fact]
    public void Save_NormalAutoFitAlreadyBelowRuntimeFloor_NeverGrowsScaleBack()
    {
        // Sibling: a cached scale below the 60% runtime floor (PowerPoint's own aggressive
        // shrink-to-fit) must never be floored back UP by the writer's coarse, text-metrics-free
        // estimate — only genuine evidence of MORE overflow than already cached should move it,
        // and the estimate must never claim a wrong, more-generous fit than what PowerPoint itself
        // already determined was necessary.
        var body = new TextBody { AutoFit = true, FontScalePPT = 25000 }; // 25%, below the 60% floor
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Short", FontSizePt = 18.0 });
        body.Paragraphs.Add(para);

        var shape = MakeShapeWithText(body, extentCxEmu: 4572000, extentCyEmu: 3000000);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(shape);

        using var ms = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var roundTripped = p2.Slides[0].Shapes[0].TextBody!;
        roundTripped.FontScalePPT.Should().Be(25000, "the writer's coarse estimate must never grow a cached scale back up");
    }
}
