namespace FreeW.Core.Model.Tests;

/// <summary>
/// r199: <c>ChangeDrawingGroupChildZOrderCommand</c> never overrode <c>HasEffect</c>, so Bring Forward
/// on a group child that is already frontmost within its group -- a path its own <c>Apply</c> exits
/// early from without touching anything -- still reached <c>DocumentCommandBus.Execute</c>'s push.
/// That push clears the redo stack, so an operation that changed nothing destroyed the user's ability
/// to redo a real edit. The sibling <c>ChangeZOrderCommand</c> has had the override all along.
/// </summary>
public sealed class R199_GroupChildZOrderNoOpTests
{
    private sealed class TestCtx(TextDocument doc) : IDocumentCommandContext
    {
        public TextDocument Document => doc;
    }

    private static (TextDocument Document, DrawingGroup Group, Shape Back, Shape Front) Fixture()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var back = new Shape(ShapeKind.Rectangle, 36, 22);
        var front = new Shape(ShapeKind.Ellipse, 44, 28);
        var group = new DrawingGroup();
        group.Children.Add(back);
        group.ChildOffsets.Add((10, 8));
        group.Children.Add(front);
        group.ChildOffsets.Add((58, 30));

        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        document.Blocks.Add(paragraph);
        return (document, group, back, front);
    }

    [Theory]
    [InlineData(1, ZOrderOperation.BringForward)]  // already frontmost
    [InlineData(0, ZOrderOperation.SendBackward)]  // already backmost
    public void ReorderingPastTheEndOfTheGroup_LeavesRedoIntact(int childIndex, ZOrderOperation operation)
    {
        var (document, group, _, _) = Fixture();
        var bus = new DocumentCommandBus(new TestCtx(document));

        // A real edit the user can redo.
        bus.Execute(new SetDrawingGroupChildPositionCommand(0, 0, 1, 96, 54));
        bus.Undo().Should().BeTrue();
        bus.CanRedo.Should().BeTrue();

        bus.Execute(new ChangeDrawingGroupChildZOrderCommand(0, 0, [childIndex], operation));

        bus.CanRedo.Should().BeTrue(
            "a z-order move that changed nothing must not discard the pending redo");
        group.Children.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(1, ZOrderOperation.BringForward)]
    [InlineData(0, ZOrderOperation.SendBackward)]
    public void ReorderingPastTheEndOfTheGroup_PushesNoUndoEntry(int childIndex, ZOrderOperation operation)
    {
        var (document, _, _, _) = Fixture();
        var bus = new DocumentCommandBus(new TestCtx(document));

        bus.Execute(new ChangeDrawingGroupChildZOrderCommand(0, 0, [childIndex], operation));

        bus.CanUndo.Should().BeFalse("nothing moved, so there is nothing to undo");
    }

    [Fact]
    public void AReorderThatCanMove_StillPushesItsUndoEntry()
    {
        // The control: the ordinary case is unchanged.
        var (document, group, back, front) = Fixture();
        var bus = new DocumentCommandBus(new TestCtx(document));

        bus.Execute(new ChangeDrawingGroupChildZOrderCommand(0, 0, [0], ZOrderOperation.BringForward));

        group.Children.Should().Equal(front, back);
        bus.CanUndo.Should().BeTrue();
        bus.Undo().Should().BeTrue();
        group.Children.Should().Equal(back, front);
    }
}
