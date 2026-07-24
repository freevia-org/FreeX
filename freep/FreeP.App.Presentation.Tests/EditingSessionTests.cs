using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for <see cref="EditingSession"/>.
/// </summary>
public sealed class EditingSessionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Creates a session with <paramref name="slideCount"/> blank slides (no shapes).</summary>
    private static EditingSession Make(int slideCount = 1)
    {
        var p = new Presentation();
        for (int i = 0; i < slideCount; i++)
            p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);
        return new EditingSession(p, bus);
    }

    private static SlideShape MakeShape(uint id = 1) => new()
    {
        Id          = id,
        Name        = $"S{id}",
        Kind        = SlideShapeKind.AutoShape,
        OffsetXEmu  = 0,
        OffsetYEmu  = 0,
        ExtentCxEmu = 100,
        ExtentCyEmu = 100,
    };

    // ── Construction ──────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_SingleSlidePresentation_CurrentSlideIsSlide0()
    {
        var sess = Make();
        sess.CurrentSlideIndex.Should().Be(0);
        sess.CurrentSlide.Should().NotBeNull();
    }

    [Fact]
    public void Ctor_EmptyPresentation_CurrentSlideIsMinusOne()
    {
        var p   = new Presentation();
        var bus = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.CurrentSlideIndex.Should().Be(-1);
        sess.CurrentSlide.Should().BeNull();
    }

    // ── Slide operations ──────────────────────────────────────────────────────────

    [Fact]
    public void InsertSlide_IncreasesSlideCount()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void InsertSlide_UpdatesCurrentSlideIndex()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.CurrentSlideIndex.Should().Be(1, "new slide inserted after current");
    }

    [Fact]
    public void DeleteCurrentSlide_DecreasesSlideCount()
    {
        var sess = Make(2);
        sess.DeleteCurrentSlide();
        sess.Presentation.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void DeleteCurrentSlide_ClampsIndexWhenDeletingLast()
    {
        var sess = Make(2);
        sess.SelectSlide(1);
        sess.DeleteCurrentSlide();
        sess.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void DeleteCurrentSlide_NoOp_WhenNoSlides()
    {
        var p   = new Presentation();
        var bus = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        var act = () => sess.DeleteCurrentSlide();
        act.Should().NotThrow();
    }

    [Fact]
    public void DuplicateCurrentSlide_IncreasesSlideCount()
    {
        var sess = Make();
        sess.DuplicateCurrentSlide();
        sess.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void DuplicateCurrentSlide_MovesCurrentToClone()
    {
        var sess = Make();
        sess.DuplicateCurrentSlide();
        sess.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void MoveSlide_ReordersSlides()
    {
        var sess  = Make(3);
        var first = sess.Presentation.Slides[0];
        // Move slide 0 to index 2: [A,B,C] => [B,C,A]
        sess.MoveSlide(0, 2);
        sess.Presentation.Slides[2].Should().BeSameAs(first);
    }

    [Fact]
    public void ToggleCurrentSlideHidden_IsUndoableAndRedoable()
    {
        var sess = Make();

        sess.ToggleCurrentSlideHidden().Should().BeTrue();
        sess.CurrentSlide!.IsHidden.Should().BeTrue();

        sess.Undo();
        sess.CurrentSlide!.IsHidden.Should().BeFalse();

        sess.Redo();
        sess.CurrentSlide!.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void SelectSlide_ChangesCurrentSlide()
    {
        var sess = Make(2);
        sess.SelectSlide(1);
        sess.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void SelectSlide_ClearsSelection()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.SelectSlide(0);
        sess.SelectedShapeIds.Should().BeEmpty();
    }

    // ── Undo/redo through session ─────────────────────────────────────────────────

    [Fact]
    public void Undo_AfterInsertSlide_RestoresPreviousCount()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.Undo();
        sess.Presentation.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void Undo_ClampsCurrentSlideIndex()
    {
        var sess = Make();
        sess.InsertSlide(); // now at index 1
        sess.Undo();        // slide removed, index must clamp to 0
        sess.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void Redo_AfterUndoInsert_ReappliesInsert()
    {
        var sess = Make();
        sess.InsertSlide();
        sess.Undo();
        sess.Redo();
        sess.Presentation.Slides.Should().HaveCount(2);
    }

    // ── Selection ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_AddsShapeToSelection()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.SelectedShapeIds.Should().Contain(1u);
    }

    [Fact]
    public void Select_WithoutAdd_ReplacesSelection()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(1u);
        sess.Select(2u, addToSelection: false);
        sess.SelectedShapeIds.Should().HaveCount(1).And.Contain(2u);
    }

    [Fact]
    public void Select_WithAdd_ExtendsSelection()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(1u);
        sess.Select(2u, addToSelection: true);
        sess.SelectedShapeIds.Should().HaveCount(2);
    }

    [Fact]
    public void ClearSelection_EmptiesSelection()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.ClearSelection();
        sess.SelectedShapeIds.Should().BeEmpty();
    }

    [Fact]
    public void SelectAll_SelectsAllShapesOnCurrentSlide()
    {
        var sess = Make();
        sess.CurrentSlide!.Shapes.AddRange([MakeShape(1), MakeShape(2), MakeShape(3)]);
        sess.SelectAll();
        sess.SelectedShapeIds.Should().HaveCount(3);
    }

    [Fact]
    public void SelectionChanged_FiresOnSelect()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        int fired = 0;
        sess.SelectionChanged += (_, _) => fired++;
        sess.Select(1u);
        fired.Should().Be(1);
    }

    [Fact]
    public void CurrentSlideChanged_FiresOnInsert()
    {
        var sess  = Make();
        int fired = 0;
        sess.CurrentSlideChanged += (_, _) => fired++;
        sess.InsertSlide();
        fired.Should().BeGreaterThan(0);
    }

    // ── Shape operations through session ─────────────────────────────────────────

    [Fact]
    public void AddShape_AddsShapeToCurrentSlide_AndIsUndoable()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.AddShape(shape);
        sess.CurrentSlide!.Shapes.Should().Contain(shape);
        sess.Undo();
        sess.CurrentSlide!.Shapes.Should().NotContain(shape);
    }

    [Fact]
    public void DeleteSelected_RemovesSelectedShape_AndIsUndoable()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        // Add shape directly to the slide model (not through bus so undo stack is clean).
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.DeleteSelected();
        sess.CurrentSlide!.Shapes.Should().NotContain(shape);
        // Undo the delete — shape returns.
        sess.Undo();
        sess.CurrentSlide!.Shapes.Should().Contain(shape);
    }

    [Fact]
    public void MoveSelected_TranslatesShape()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.MoveSelected(200, 150);
        shape.OffsetXEmu.Should().Be(200);
        shape.OffsetYEmu.Should().Be(150);
    }

    [Fact]
    public void MoveSelected_NoOp_WhenNothingSelected()
    {
        var sess = Make();
        var act  = () => sess.MoveSelected(100, 100);
        act.Should().NotThrow();
    }

    [Fact]
    public void BringForward_IncrementsZOrder()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(1u);
        sess.BringForward();
        // s1 was at index 0; after BringForward it should be at index 1.
        sess.CurrentSlide!.Shapes[1].Should().BeSameAs(s1);
    }

    [Fact]
    public void SendBackward_DecrementsZOrder()
    {
        var sess = Make();
        var s1   = MakeShape(1);
        var s2   = MakeShape(2);
        sess.CurrentSlide!.Shapes.AddRange([s1, s2]);
        sess.Select(2u);
        sess.SendBackward();
        // s2 was at index 1; after SendBackward it should be at index 0.
        sess.CurrentSlide!.Shapes[0].Should().BeSameAs(s2);
    }

    // ── Default shape factories ───────────────────────────────────────────────────

    [Fact]
    public void InsertDefaultTextBox_AddsShapeAtCenterPosition()
    {
        var sess  = Make();
        var shape = sess.InsertDefaultTextBox();
        shape.Should().NotBeNull();
        shape.Kind.Should().Be(SlideShapeKind.AutoShape);
        shape.OffsetXEmu.Should().BeGreaterThan(0);
        shape.TextBody.Should().NotBeNull();
    }

    [Fact]
    public void InsertMedia_AddsEmbeddedVideoAndIsUndoable()
    {
        var sess = Make();
        var shape = sess.InsertMedia(new byte[] { 1, 2, 3 }, true, "video/mp4");

        shape.Kind.Should().Be(SlideShapeKind.Media);
        shape.Media!.IsVideo.Should().BeTrue();
        sess.CurrentSlide!.Shapes.Should().ContainSingle();

        sess.Undo();
        sess.CurrentSlide.Shapes.Should().BeEmpty();
        sess.Redo();
        sess.CurrentSlide.Shapes.Should().ContainSingle();
    }

    [Fact]
    public void InsertDefaultRectangle_AddsRectangle()
    {
        var sess  = Make();
        var shape = sess.InsertDefaultRectangle();
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Rectangle);
    }

    [Fact]
    public void InsertDefaultEllipse_AddsEllipse()
    {
        var sess  = Make();
        var shape = sess.InsertDefaultEllipse();
        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Ellipse);
    }

    // ── Format toggles ────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleBoldOnSelection_TogglesBoldOnAllRuns()
    {
        var sess  = Make();
        var shape = MakeShape(1);
        var tb    = new TextBody();
        var para  = new Paragraph();
        var run   = new Run { Text = "hi", Bold = false };
        para.Runs.Add(run);
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        sess.CurrentSlide!.Shapes.Add(shape);
        sess.Select(1u);
        sess.ToggleBoldOnSelection();
        run.Bold.Should().BeTrue();
    }

    [Fact]
    public void ToggleBoldOnSelection_NoOp_WhenNothingSelected()
    {
        var sess = Make();
        var act  = () => sess.ToggleBoldOnSelection();
        act.Should().NotThrow();
    }
}
