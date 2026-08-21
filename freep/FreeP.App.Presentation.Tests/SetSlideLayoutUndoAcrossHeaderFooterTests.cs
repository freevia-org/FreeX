using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 161 (freep-master-edit F1): SetSlideLayoutCommand.Revert/redo-Apply resolved the shapes
/// they touch by cached object reference (the live SlideShape captured at Apply time, plus
/// PlaceholderGeometryState wrapping it). If ANY intervening command on the same undo stack
/// wholesale-replaces the Slide object -- HeaderFooterCommandPlanner's ApplyHeaderFooterCommand
/// does exactly that via SlideCloner.CloneSlidePreservingIdentity on every Apply/Revert -- every
/// shape becomes a brand-new clone, and the cached references go stale: slide.Shapes.Remove
/// (reference equality) silently no-ops on the newly-materialized placeholder, and
/// RestoreOriginalGeometry mutates a detached object with zero visible effect. These tests go
/// through the real user path: EditingSession.SetCurrentSlideLayout (the Layout Picker call) then
/// HeaderFooterCommandPlanner.TryApply (the Header and Footer dialog call) on the same
/// PresentationCommandBus undo stack, then two undos -- exactly the repro gesture in the finding.
/// </summary>
public sealed class SetSlideLayoutUndoAcrossHeaderFooterTests
{
    [Fact]
    public void UndoingLayoutSwitch_AfterInterveningHeaderFooterEdit_RemovesMaterializedPlaceholderAndRestoresGeometry()
    {
        var editor = MakeEditorWithTwoLayouts(out var originalTitleShapeId);
        var slide = editor.Presentation.Slides[0];

        slide.Shapes.Should().ContainSingle();
        slide.LayoutId.Should().Be("title-only");

        // Step 1/2: switch to a layout with an extra Body placeholder -- materializes it, and
        // updates the existing Title shape's geometry to the new layout's Title geometry.
        editor.SetCurrentSlideLayout("title-and-body").Should().BeTrue();

        var afterSwitch = editor.Presentation.Slides[0];
        afterSwitch.LayoutId.Should().Be("title-and-body");
        afterSwitch.Shapes.Should().HaveCount(2);
        var titleAfterSwitch = afterSwitch.Shapes.Single(s => s.Id == originalTitleShapeId);
        titleAfterSwitch.OffsetXEmu.Should().Be(2_000_000); // the NEW layout's Title geometry

        // Step 3: Insert > Header & Footer, current slide -- this wholesale-replaces the Slide
        // object (and every SlideShape on it) via SlideCloner.CloneSlidePreservingIdentity.
        var options = new HeaderFooterApplyOptions(
            ShowDateTime: false,
            ShowFooter: false,
            ShowSlideNumber: true,
            FooterText: string.Empty,
            Scope: HeaderFooterApplyScope.CurrentSlide);
        HeaderFooterCommandPlanner.TryApply(editor, options, out _).Should().BeTrue();

        var afterHf = editor.Presentation.Slides[0];
        ReferenceEquals(afterHf, afterSwitch).Should().BeFalse(); // sanity: whole-slide clone-swap happened
        afterHf.Shapes.Should().HaveCount(3); // Title clone + Body placeholder clone + slide-number shape

        // Step 4: Ctrl+Z twice -- undo the header/footer edit, then undo the layout switch.
        editor.Undo();
        editor.Undo();

        var reverted = editor.Presentation.Slides[0];
        reverted.LayoutId.Should().Be("title-only");

        // The bug: the Body placeholder materialized by the (now-reverted) layout switch stayed
        // on the slide forever, because slide.Shapes.Remove(placeholder) no-opped against a
        // detached, pre-clone-swap object reference.
        reverted.Shapes.Should().ContainSingle(
            "the Body placeholder materialized by the reverted layout switch must be removed, " +
            "not stranded on the slide as a reference-equality no-op");

        // The un-reverted-geometry half of the same finding: the Title shape's geometry must go
        // back to what it was before the layout switch, not stay stuck on the newer layout's
        // geometry because RestoreOriginalGeometry mutated a detached clone.
        var titleAfterUndo = reverted.Shapes.Single();
        titleAfterUndo.Id.Should().Be(originalTitleShapeId);
        titleAfterUndo.OffsetXEmu.Should().Be(1_000_000);
        titleAfterUndo.OffsetYEmu.Should().Be(1_000_000);
    }

    [Fact]
    public void UndoingLayoutSwitch_WithNoInterveningCommand_StillRemovesMaterializedPlaceholder()
    {
        // Sibling/no-regression case: the ordinary undo path (no intervening whole-slide clone
        // swap) must keep working exactly as before -- this fix must not depend on a clone swap
        // having happened.
        var editor = MakeEditorWithTwoLayouts(out var originalTitleShapeId);

        editor.SetCurrentSlideLayout("title-and-body").Should().BeTrue();
        editor.Presentation.Slides[0].Shapes.Should().HaveCount(2);

        editor.Undo();

        var reverted = editor.Presentation.Slides[0];
        reverted.LayoutId.Should().Be("title-only");
        reverted.Shapes.Should().ContainSingle();
        var title = reverted.Shapes.Single();
        title.Id.Should().Be(originalTitleShapeId);
        title.OffsetXEmu.Should().Be(1_000_000);
        title.OffsetYEmu.Should().Be(1_000_000);
    }

    private static EditingSession MakeEditorWithTwoLayouts(out uint originalTitleShapeId)
    {
        var presentation = Presentation.CreateEmpty();
        var master = presentation.Masters[0];

        // Reshape the default layout into an explicit "title-only" layout with a Title
        // placeholder definition (so the materialization loop can match the slide's Title shape
        // against it), matching the user gesture's starting point.
        var titleOnlyLayout = presentation.Layouts[0];
        titleOnlyLayout.Id = "title-only";
        titleOnlyLayout.Placeholders.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 1_000_000,
            OffsetYEmu = 1_000_000,
            ExtentCxEmu = 4_000_000,
            ExtentCyEmu = 900_000,
        });

        var titleAndBodyLayout = new SlideLayout
        {
            Id = "title-and-body",
            Name = "Title and Content",
            LayoutType = SlideLayoutType.Custom,
            MasterId = master.Id,
        };
        titleAndBodyLayout.Placeholders.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 },
            OffsetXEmu = 2_000_000,
            OffsetYEmu = 2_000_000,
            ExtentCxEmu = 4_500_000,
            ExtentCyEmu = 950_000,
        });
        titleAndBodyLayout.Placeholders.Add(new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 500_000,
            OffsetYEmu = 1_500_000,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
        });
        presentation.Layouts.Add(titleAndBodyLayout);

        // Presentation.CreateEmpty() already set slide.Title, which materializes a single Title
        // placeholder shape (Slide.Title's setter) -- reuse that shape instead of adding a
        // second one, so the slide starts with exactly the one shape the user gesture assumes.
        var slide = presentation.Slides[0];
        slide.LayoutId = "title-only";
        var titleShape = slide.Shapes.Single(s => s.Placeholder?.Type == PlaceholderType.Title);
        titleShape.OffsetXEmu = 1_000_000;
        titleShape.OffsetYEmu = 1_000_000;
        titleShape.ExtentCxEmu = 4_000_000;
        titleShape.ExtentCyEmu = 900_000;
        originalTitleShapeId = titleShape.Id;

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        return editor;
    }
}
