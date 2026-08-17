using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 139 remediation: SetSlideLayoutCommand.Apply materializes xfrm-less layout placeholders
/// so Title/Body content is reachable after a layout switch (see PresentationCommands.cs). That
/// loop must NOT do the same for Header/DateTime/Footer/SlideNumber placeholders, because their
/// visibility defaults to "on" the moment a matching shape exists on the slide and
/// Slide.HfVisibility is still null (see HeaderFooterCommandPlanner.BuildState and
/// SlideCompositor.IsVisibleByHeaderFooterFlags, both `flags?.ShowX ?? hasX`/`true`). These tests
/// go through the real user path -- EditingSession.SetCurrentSlideLayout, the exact call the
/// Layout Picker in both shells invokes -- with the production HeaderFooterCommandPlanner and
/// SlideCompositor collaborators (no stubs), so they exercise the actual bug location.
/// </summary>
public sealed class SetSlideLayoutHeaderFooterRegressionTests
{
    [Fact]
    public void SwitchingLayout_DoesNotMaterializeOrEnableHeaderFooterPlaceholders()
    {
        var editor = MakeEditorWithTargetLayout();
        var slide = editor.Presentation.Slides[0];

        // Sanity: this is a slide the user never ran Insert > Header & Footer on.
        slide.HfVisibility.Should().BeNull();

        editor.SetCurrentSlideLayout("target-layout").Should().BeTrue();

        var updatedSlide = editor.Presentation.Slides[0];

        // The bug: the materialization loop cloned Footer/DateTime/SlideNumber placeholders from
        // the layout onto the slide just like Title/Body, planting a shape that both BuildState
        // and the compositor read as "on" under the null-HfVisibility default.
        updatedSlide.Shapes.Should().NotContain(shape => IsHeaderFooterPlaceholder(shape));

        // The dialog state (Insert > Header & Footer) must read exactly as it did before the
        // layout switch: nothing checked, because nothing was ever turned on.
        var state = HeaderFooterCommandPlanner.BuildState(editor.Presentation, 0);
        state.ShowDateTime.Should().BeFalse();
        state.ShowFooter.Should().BeFalse();
        state.ShowSlideNumber.Should().BeFalse();

        // The rendered output must not gain a footer/date/slide-number that was never authored.
        var ops = SlideCompositor.Compose(editor.Presentation, updatedSlide, 0);
        var renderedShapeIds = ops.OfType<DrawOp.Shape>().Select(op => op.ShapeId).ToList();
        renderedShapeIds.Should().NotContain(501u);
        renderedShapeIds.Should().NotContain(502u);
        renderedShapeIds.Should().NotContain(503u);
    }

    [Fact]
    public void SwitchingLayout_StillMaterializesTitlePlaceholder()
    {
        // The original r139 fix (xfrm-less Title/Body placeholders becoming reachable after a
        // layout switch) must stay intact -- this test guards against a fix for the regression
        // above that overcorrects and drops Title/Body materialization too.
        var editor = MakeEditorWithTargetLayout();

        editor.SetCurrentSlideLayout("target-layout").Should().BeTrue();

        var updatedSlide = editor.Presentation.Slides[0];
        updatedSlide.Shapes.Should().Contain(shape => IsTitlePlaceholder(shape));
    }

    private static bool IsHeaderFooterPlaceholder(SlideShape shape)
    {
        var type = shape.Placeholder?.Type;
        return type == PlaceholderType.Footer ||
               type == PlaceholderType.DateTime ||
               type == PlaceholderType.SlideNumber ||
               type == PlaceholderType.Header;
    }

    private static bool IsTitlePlaceholder(SlideShape shape) =>
        shape.Placeholder?.Type == PlaceholderType.Title;

    private static EditingSession MakeEditorWithTargetLayout()
    {
        var presentation = Presentation.CreateEmpty();

        var targetLayout = new SlideLayout
        {
            Id = "target-layout",
            Name = "Target Layout",
            LayoutType = SlideLayoutType.Custom,
            MasterId = presentation.Masters[0].Id,
        };

        // Title/Body: the r139-fixed case -- must still be materialized on layout switch.
        targetLayout.Placeholders.Add(LayoutPlaceholder(PlaceholderType.Title, idx: 0));
        targetLayout.Placeholders.Add(LayoutPlaceholder(PlaceholderType.Body, idx: 1));

        // Header/DateTime/Footer/SlideNumber: this round's regression -- must NOT be
        // materialized by a bare layout switch, only by the Header & Footer flow.
        targetLayout.Placeholders.Add(LayoutPlaceholder(PlaceholderType.DateTime, idx: 10, id: 501));
        targetLayout.Placeholders.Add(LayoutPlaceholder(PlaceholderType.Footer, idx: 11, id: 502));
        targetLayout.Placeholders.Add(LayoutPlaceholder(PlaceholderType.SlideNumber, idx: 12, id: 503));

        presentation.Layouts.Add(targetLayout);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        return editor;
    }

    private static SlideShape LayoutPlaceholder(PlaceholderType type, int idx, uint id = 0) =>
        new()
        {
            Id = id != 0 ? id : (uint)(1_000 + idx),
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = type, Idx = idx },
            OffsetXEmu = 100_000,
            OffsetYEmu = 100_000,
            ExtentCxEmu = 2_000_000,
            ExtentCyEmu = 500_000,
        };
}
