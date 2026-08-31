using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 176 (freep-undo-model F1 / F2): a shared root cause -- several commands' Revert cached a
/// captured object REFERENCE (a List&lt;SlideShape&gt; container, a Slide, a SlideShape connector, a
/// ShapeAnimation) at Apply time and reused it directly at undo time, instead of re-resolving the
/// live object from the current Presentation by a stable identity (shape Id / Slide.Id / index).
/// HeaderFooterCommandPlanner.ApplyHeaderFooterCommand wholesale-replaces the target Slide object
/// (and therefore every SlideShape/Animations list on it) via SlideCloner.CloneSlidePreservingIdentity
/// on every Apply AND Revert, so any command whose undo sits on the far side of an intervening
/// Header/Footer edit on the same slide was operating on a detached, orphaned object -- silently
/// doing nothing while the undo stack still reported the operation as reverted.
///
/// These tests all interleave a real Header/Footer apply (via HeaderFooterCommandPlanner, the same
/// planner Insert &gt; Header and Footer uses) between the operation under test and its undo -- the
/// gesture the pre-existing tests for these commands did not cover.
/// </summary>
public sealed class Round176UndoAcrossHeaderFooterTests
{
    private static HeaderFooterApplyOptions CurrentSlideFooterOptions => new(
        ShowDateTime: false,
        ShowFooter: true,
        ShowSlideNumber: false,
        FooterText: "Confidential",
        Scope: HeaderFooterApplyScope.CurrentSlide);

    private static HeaderFooterApplyOptions AllSlidesFooterOptions => CurrentSlideFooterOptions with
    {
        Scope = HeaderFooterApplyScope.AllSlides,
    };

    private static EditingSession MakeEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // F1 — Delete Shape
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteShapeUndo_AfterInterveningHeaderFooterEdit_RestoresTheShape()
    {
        // Presentation.CreateEmpty() already materializes a Title placeholder shape (Id 1) on
        // slide 0 -- use a non-colliding Id for the shape under test and assert on that Id
        // specifically, rather than assuming the slide starts with zero shapes.
        var editor = MakeEditor();
        var shape = new SlideShape { Id = 42, Kind = SlideShapeKind.AutoShape };
        editor.AddShape(shape);
        editor.Presentation.Slides[0].Shapes.Should().Contain(s => s.Id == 42);

        editor.Bus.Execute(new DeleteShapeCommand(0, 42));
        editor.Presentation.Slides[0].Shapes.Should().NotContain(s => s.Id == 42);

        // Insert > Header and Footer on the same (current) slide -- wholesale-replaces the Slide
        // object via SlideCloner.CloneSlidePreservingIdentity.
        var before = editor.Presentation.Slides[0];
        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();
        ReferenceEquals(editor.Presentation.Slides[0], before).Should().BeFalse(); // sanity: clone-swap happened

        editor.Undo(); // undo the header/footer edit
        editor.Undo(); // undo the delete

        editor.Presentation.Slides[0].Shapes.Should().Contain(s => s.Id == 42,
            "the deleted shape must reappear on the LIVE slide, not a detached list orphaned by " +
            "the intervening header/footer clone-swap");
    }

    [Fact]
    public void DeleteShapeUndo_WithNoInterveningCommand_StillRestoresTheShape()
    {
        // Sibling/no-regression: the ordinary undo path (no clone-swap in between) must keep
        // working exactly as before.
        var editor = MakeEditor();
        var shape = new SlideShape { Id = 42, Kind = SlideShapeKind.AutoShape };
        editor.AddShape(shape);

        editor.Bus.Execute(new DeleteShapeCommand(0, 42));
        editor.Presentation.Slides[0].Shapes.Should().NotContain(s => s.Id == 42);

        editor.Undo();

        editor.Presentation.Slides[0].Shapes.Should().Contain(s => s.Id == 42);
        editor.Presentation.Slides[0].Shapes.Single(s => s.Id == 42).Should().BeSameAs(shape);
    }

    [Fact]
    public void DeleteShapeUndo_AfterInterveningHeaderFooterEdit_RestoresConnectorAttachment()
    {
        // Same root cause, a second place inside DeleteShapeCommand.Revert: the connector whose
        // ConnectionStart/End was cleared by the delete was also being restored by mutating a
        // captured SlideShape REFERENCE rather than resolving the live connector by Id. Ids 101/
        // 102 avoid colliding with the Title placeholder (Id 1) CreateEmpty() already materializes.
        var editor = MakeEditor();
        var anchor = new SlideShape { Id = 101, Kind = SlideShapeKind.AutoShape };
        var connector = new SlideShape
        {
            Id = 102,
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.ElbowConnector,
            ConnectionStart = new ConnectorAttachment { ShapeId = 101, SiteIndex = 0 },
        };
        editor.AddShape(anchor);
        editor.AddShape(connector);

        editor.Bus.Execute(new DeleteShapeCommand(0, 101)); // delete the anchor shape
        editor.Presentation.Slides[0].Shapes
            .Single(s => s.Id == 102).ConnectionStart.Should().BeNull("attachment cleared on delete");

        var before = editor.Presentation.Slides[0];
        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();
        ReferenceEquals(editor.Presentation.Slides[0], before).Should().BeFalse();

        editor.Undo(); // header/footer
        editor.Undo(); // delete

        var liveConnector = editor.Presentation.Slides[0].Shapes.SingleOrDefault(s => s.Id == 102);
        liveConnector.Should().NotBeNull();
        liveConnector!.ConnectionStart.Should().NotBeNull(
            "the connector's attachment must be restored on the LIVE connector shape, not a " +
            "detached clone-swapped-away one");
        liveConnector.ConnectionStart!.ShapeId.Should().Be(101u);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // F2 — Duplicate Slide / Insert Slide
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// r176 remediation, found by auditing the fix above rather than by the review itself:
    /// PasteSlideCommand carried the identical reference-equality Revert, and Paste is the more
    /// common gesture of the two. Reproduced at runtime before this test existed.
    /// </summary>
    [Fact]
    public void PasteSlideUndo_AfterInterveningHeaderFooterEdit_RemovesThePastedSlide()
    {
        var editor = MakeEditor();
        editor.Presentation.Slides.Should().HaveCount(1);

        editor.CopyCurrentSlide();
        editor.PasteSlide();
        editor.Presentation.Slides.Should().HaveCount(2);
        editor.CurrentSlideIndex.Should().Be(1);

        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // paste slide

        editor.Presentation.Slides.Should().HaveCount(1,
            "undo must remove the pasted slide by identity -- the header/footer edit swapped in a "
            + "clone, so List<Slide>.Remove against the pre-swap reference silently no-opped");
    }

    [Fact]
    public void DuplicateSlideUndo_AfterInterveningHeaderFooterEdit_DefaultCurrentSlideScope_RemovesStrayDuplicate()
    {
        // Minimal, default-UI-path repro: duplicating a slide selects the duplicate as current, and
        // Header & Footer's default scope is "current slide" -- exactly the finding's user gesture.
        var editor = MakeEditor();
        editor.Presentation.Slides.Should().HaveCount(1);

        editor.DuplicateCurrentSlide();
        editor.Presentation.Slides.Should().HaveCount(2);
        editor.CurrentSlideIndex.Should().Be(1);

        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // duplicate slide

        editor.Presentation.Slides.Should().HaveCount(1,
            "the duplicate slide must be removed by undo, not stranded because " +
            "List<Slide>.Remove(_duplicate) no-opped against a detached, pre-clone-swap object");
    }

    [Fact]
    public void DuplicateSlideUndo_AfterInterveningHeaderFooterEdit_AllSlidesScope_RemovesStrayDuplicate()
    {
        var editor = MakeEditor();
        editor.DuplicateCurrentSlide();
        editor.Presentation.Slides.Should().HaveCount(2);

        HeaderFooterCommandPlanner.TryApply(editor, AllSlidesFooterOptions, out _).Should().BeTrue();

        editor.Undo();
        editor.Undo();

        editor.Presentation.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void DuplicateSlideUndo_WithNoInterveningCommand_StillRemovesDuplicate()
    {
        // Sibling/no-regression: the ordinary undo path must keep working exactly as before.
        var editor = MakeEditor();
        editor.DuplicateCurrentSlide();
        editor.Presentation.Slides.Should().HaveCount(2);

        editor.Undo();

        editor.Presentation.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void InsertSlideUndo_AfterInterveningHeaderFooterEdit_RemovesStraySlide()
    {
        // InsertSlideCommand shares the identical p.Slides.Remove(<captured object>) pattern as
        // DuplicateSlideCommand and is subject to the same failure mode.
        var editor = MakeEditor();
        editor.Presentation.Slides.Should().HaveCount(1);

        var inserted = new Slide { Id = "inserted-slide" };
        editor.Bus.Execute(new InsertSlideCommand(1, inserted));
        editor.Presentation.Slides.Should().HaveCount(2);

        editor.SelectSlide(1);
        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // insert slide

        editor.Presentation.Slides.Should().HaveCount(1,
            "the inserted slide must be removed by undo, not stranded by a reference-equality no-op");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Same root cause, adjacent commands fixed alongside F1/F2 (AddShapeCommand,
    // AddShapeAnimationCommand): both cached an object reference at Apply time and removed it by
    // reference equality at Revert time, with the identical HeaderFooterCommandPlanner trigger.
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddShapeUndo_AfterInterveningHeaderFooterEdit_RemovesTheShape()
    {
        var editor = MakeEditor();
        editor.AddShape(new SlideShape { Id = 99, Kind = SlideShapeKind.AutoShape });
        editor.Presentation.Slides[0].Shapes.Should().Contain(s => s.Id == 99);

        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // add shape

        editor.Presentation.Slides[0].Shapes.Should().NotContain(s => s.Id == 99,
            "the added shape must be removed by undo, not stranded because " +
            "List<SlideShape>.Remove(_shape) no-opped against a detached, pre-clone-swap object");
    }

    [Fact]
    public void AddShapeAnimationUndo_AfterInterveningHeaderFooterEdit_RemovesTheAnimation()
    {
        var editor = MakeEditor();
        var shape = new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape };
        editor.AddShape(shape);

        editor.AddAnimation(7u, new ShapeAnimation { ShapeId = 7u });
        editor.Presentation.Slides[0].Animations.Should().ContainSingle();

        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // add animation

        editor.Presentation.Slides[0].Animations.Should().BeEmpty(
            "the added animation must be removed by undo, not stranded because " +
            "List<ShapeAnimation>.Remove(_animation) no-opped against a detached, pre-clone-swap list");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // r176 remediation sweep — the rest of the cached-reference family
    //
    // Found by auditing the F1/F2 fixes rather than by the review: three more Reverts
    // in the same class. Group/Ungroup additionally cached the CONTAINER list itself
    // (and Ungroup the connector objects), which a slide clone replaces wholesale.
    // Master/layout shape commands share the shape-reference shape of this bug but are
    // NOT reachable from this trigger — HeaderFooterCommandPlanner only READS layouts
    // (IsTitleSlide) and never clones a master or layout — so they are left alone.
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PasteShapesUndo_AfterInterveningHeaderFooterEdit_RemovesThePastedShapes()
    {
        var editor = MakeEditor();
        editor.AddShape(new SlideShape { Id = 42, Kind = SlideShapeKind.AutoShape });
        editor.Select(42);
        editor.CopySelectedShapes();
        editor.PasteShapes();

        var pastedIds = editor.Presentation.Slides[0].Shapes
            .Where(shape => shape.Id != 42)
            .Select(shape => shape.Id)
            .ToList();
        var countAfterPaste = editor.Presentation.Slides[0].Shapes.Count;

        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // paste shapes

        editor.Presentation.Slides[0].Shapes.Count.Should().BeLessThan(countAfterPaste,
            "the pasted shapes must be removed by Id -- the clone-swap replaced every shape "
            + "object, so List<SlideShape>.Remove against the pre-swap references no-opped");
    }

    [Fact]
    public void GroupUndo_AfterInterveningHeaderFooterEdit_RemovesTheGroupAndRestoresTheOriginals()
    {
        var editor = MakeEditor();
        editor.AddShape(new SlideShape { Id = 42, Kind = SlideShapeKind.AutoShape });
        editor.AddShape(new SlideShape { Id = 43, Kind = SlideShapeKind.AutoShape });
        editor.Select(42);
        editor.Select(43, addToSelection: true);

        editor.GroupSelectedShapes();
        editor.Presentation.Slides[0].Shapes.Should()
            .Contain(shape => shape.Kind == SlideShapeKind.Group);

        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // group

        var shapes = editor.Presentation.Slides[0].Shapes;
        shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.Group,
            "undo must remove the group from the LIVE slide, not from the detached pre-swap list");
        shapes.Should().Contain(shape => shape.Id == 42);
        shapes.Should().Contain(shape => shape.Id == 43);
    }

    [Fact]
    public void UngroupUndo_AfterInterveningHeaderFooterEdit_RestoresTheGroup()
    {
        var editor = MakeEditor();
        editor.AddShape(new SlideShape { Id = 42, Kind = SlideShapeKind.AutoShape });
        editor.AddShape(new SlideShape { Id = 43, Kind = SlideShapeKind.AutoShape });
        editor.Select(42);
        editor.Select(43, addToSelection: true);
        editor.GroupSelectedShapes();

        editor.UngroupSelected();
        editor.Presentation.Slides[0].Shapes.Should()
            .NotContain(shape => shape.Kind == SlideShapeKind.Group);

        HeaderFooterCommandPlanner.TryApply(editor, CurrentSlideFooterOptions, out _).Should().BeTrue();

        editor.Undo(); // header/footer
        editor.Undo(); // ungroup

        var shapes = editor.Presentation.Slides[0].Shapes;
        shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.Group,
            "undo must re-group on the LIVE slide -- and must not leave the freed children "
            + "behind alongside the restored group");
        shapes.Should().NotContain(shape => shape.Id == 42);
        shapes.Should().NotContain(shape => shape.Id == 43);
    }
}
