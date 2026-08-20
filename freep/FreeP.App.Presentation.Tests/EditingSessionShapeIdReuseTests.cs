namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round-159 sweep97 F1: Undo/Redo never reconciled <c>_selectedShapeIds</c>, and shape-id
/// allocation was recomputed from the live shape list, so an id freed by an undone
/// paste/insert could be handed straight back out to the very next shape the user inserted.
/// A stale selected id then silently reattached to that brand-new, never-clicked shape, and the
/// next formatting command (Bold, Delete, ...) mutated it instead of doing nothing.
///
/// These tests drive the real user gesture end to end: paste, undo the paste, insert a new
/// object through <see cref="SlideObjectInsertionPlanner"/> (the same entry point the Insert
/// ribbon uses), then issue a formatting command -- and assert the new, never-selected shape is
/// left completely unchanged. A selection-only assertion would pass even if the id reuse itself
/// were never fixed, so <see cref="PasteUndoRibbonInsertFormat_LeavesTheNewUnselectedShapeUnchanged"/>
/// additionally proves the two shapes end up with different ids.
/// </summary>
public sealed class EditingSessionShapeIdReuseTests
{
    private static EditingSession Make()
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);
        return new EditingSession(p, bus);
    }

    private static SlideShape MakeTextShape(uint id)
    {
        var shape = new SlideShape
        {
            Id          = id,
            Name        = $"S{id}",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 100_000L,
            OffsetYEmu  = 100_000L,
            ExtentCxEmu = 500_000,
            ExtentCyEmu = 300_000,
        };
        var tb   = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Hello", Bold = false });
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        return shape;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // THE REPORTED GESTURE
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PasteUndoRibbonInsertFormat_LeavesTheNewUnselectedShapeUnchanged()
    {
        var sess     = Make();
        var original = MakeTextShape(1);
        sess.CurrentSlide!.Shapes.Add(original);

        // 1) Copy + paste -- pasted shape becomes the selection.
        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();
        sess.SelectedShapeIds.Should().HaveCount(1);
        var pastedId = sess.SelectedShapeIds[0];

        // 2) Undo the paste. The pasted shape is gone, but before the round-159 fix
        // _selectedShapeIds still held its id.
        sess.Undo();
        sess.CurrentSlide!.Shapes.Should().HaveCount(1, "undo must remove the pasted shape");

        // 3) Insert a brand-new text box through the exact same entry point the Insert ribbon
        // uses (FreePRibbonCommandWorkflow.RegisterInsertCommands calls
        // SlideObjectInsertionPlanner.Apply(editor, plan) and discards the return value without
        // selecting it).
        SlideObjectInsertionPlanner.TryCreatePlan(SlideObjectInsertionPlanner.TextBoxCommandId, out var plan)
            .Should().BeTrue();
        var newShape = SlideObjectInsertionPlanner.Apply(sess, plan);
        newShape.Should().NotBeNull();

        // The id-reuse hazard itself: a fresh insert must never be handed the exact id an undone
        // paste just gave up, or any stale reference to that id (selection or otherwise) would
        // silently reattach to this unrelated shape.
        newShape!.Id.Should().NotBe(pastedId,
            "an id freed by Undo must never be handed back out to the next inserted shape");

        var boldBeforeFormat = newShape.TextBody!.Paragraphs[0].Runs[0].Bold;
        boldBeforeFormat.Should().BeFalse("a freshly inserted text box starts out not bold");

        // 4) The user never clicked the new shape. A formatting command issued now must be a
        // no-op, not a silent edit of a shape they never selected.
        sess.ToggleBoldOnSelection();

        var liveNewShape = sess.CurrentSlide.Shapes.Single(s => s.Id == newShape.Id);
        liveNewShape.TextBody!.Paragraphs[0].Runs[0].Bold.Should().Be(boldBeforeFormat,
            "Bold must not silently apply to a shape the user never selected");
    }

    [Fact]
    public void PasteUndoRibbonInsertDelete_DoesNotDeleteTheNewUnselectedShape()
    {
        var sess     = Make();
        var original = MakeTextShape(1);
        sess.CurrentSlide!.Shapes.Add(original);

        sess.Select(1u);
        sess.CopySelectedShapes();
        sess.PasteShapes();
        sess.Undo();

        SlideObjectInsertionPlanner.TryCreatePlan(SlideObjectInsertionPlanner.TextBoxCommandId, out var plan)
            .Should().BeTrue();
        var newShape = SlideObjectInsertionPlanner.Apply(sess, plan);
        newShape.Should().NotBeNull();

        // Delete() acts on _selectedShapeIds exactly like the formatting toggles; a stale
        // selection must not delete a shape the user never selected.
        sess.DeleteSelected();

        sess.CurrentSlide.Shapes.Should().Contain(s => s.Id == newShape!.Id,
            "the never-selected, freshly inserted shape must survive a Delete driven by a stale selection");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // SIBLING / NO-REGRESSION -- pruning must not blow away a still-valid selection
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Undo_OfUnrelatedEdit_PreservesSelectionOfAShapeThatStillExists()
    {
        var sess = Make();
        var a = MakeTextShape(1);
        sess.CurrentSlide!.Shapes.Add(a);

        sess.Select(1u);
        sess.MoveSelected(10_000, 10_000);
        sess.Undo(); // undoes the move, not a removal -- shape 1 still exists

        sess.SelectedShapeIds.Should().BeEquivalentTo(new[] { 1u },
            "Undo must only prune ids for shapes that no longer exist, not clear a live selection");

        // And the selection must still be functionally live: a formatting command now must
        // actually reach shape 1.
        sess.ToggleBoldOnSelection();
        var live = sess.CurrentSlide.Shapes.Single(s => s.Id == 1u);
        live.TextBody!.Paragraphs[0].Runs[0].Bold.Should().BeTrue(
            "Bold must still apply normally to a shape that is genuinely selected");
    }

    [Fact]
    public void Redo_ThatReintroducesADeletedShape_DoesNotResurrectItsStaleSelection()
    {
        var sess = Make();
        var a = MakeTextShape(1);
        sess.CurrentSlide!.Shapes.Add(a);
        sess.Select(1u);
        sess.DeleteSelected();
        sess.SelectedShapeIds.Should().BeEmpty("Delete clears the selection of what it removed");

        sess.Undo(); // shape 1 comes back
        sess.CurrentSlide.Shapes.Should().ContainSingle(s => s.Id == 1u);

        sess.Redo(); // shape 1 is removed again
        sess.CurrentSlide.Shapes.Should().BeEmpty();
        sess.SelectedShapeIds.Should().BeEmpty(
            "Redo must not leave a selection pointing at a shape it just removed");
    }
}
