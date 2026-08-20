namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for <see cref="ChangeZOrderCommand"/> (Phase 2): the four arrange operations
/// (BringToFront, SendToBack, BringForward, SendBackward) mutate <see cref="InlineImage.ZOrderIndex"/>
/// across all floating images, are reversible via Revert, and leave the document unchanged when there
/// are no floating images.
/// </summary>
public class ZOrderCommandTests
{
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47];

    private static InlineImage FloatingImage(int zOrder = 0) =>
        new(Png(), 60, 60)
        {
            Wrapping = ImageWrapping.Square,
            ZOrderIndex = zOrder
        };

    private static (TextDocument doc, DocumentCommandBus bus, Paragraph p0) SingleFloatingDoc(int zOrder = 0)
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(FloatingImage(zOrder)));
        doc.Blocks.Add(para);
        var bus = new DocumentCommandBus(new TestContext(doc));
        return (doc, bus, para);
    }

    private static (TextDocument doc, DocumentCommandBus bus) ThreeFloatingDoc(int z0, int z1, int z2)
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        // Three separate paragraphs, each with one floating image.
        for (var i = 0; i < 3; i++)
        {
            var para = new Paragraph();
            var z = i == 0 ? z0 : i == 1 ? z1 : z2;
            para.Runs.Add(Run.FromImage(FloatingImage(z)));
            doc.Blocks.Add(para);
        }
        var bus = new DocumentCommandBus(new TestContext(doc));
        return (doc, bus);
    }

    private static InlineImage ImageAt(TextDocument doc, int blockIndex) =>
        ((Paragraph)doc.Blocks[blockIndex]).Runs[0].Image!;

    // ── BringToFront ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BringToFront_SetsMaxPlusOne()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        // Target the first image (z=1); bring it to front (above z=5).
        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.BringToFront));

        ImageAt(doc, 0).ZOrderIndex.Should().Be(6); // 5 + 1
    }

    [Fact]
    public void BringToFront_Revert_RestoresAll()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.BringToFront));
        bus.Undo();

        ImageAt(doc, 0).ZOrderIndex.Should().Be(1);
        ImageAt(doc, 1).ZOrderIndex.Should().Be(3);
        ImageAt(doc, 2).ZOrderIndex.Should().Be(5);
    }

    // ── SendToBack ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SendToBack_SetsMinMinusOne()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        // Target the last image (z=5); send it to back (below z=1).
        bus.Execute(new ChangeZOrderCommand(2, 0, ZOrderOperation.SendToBack));

        ImageAt(doc, 2).ZOrderIndex.Should().Be(0); // 1 - 1
    }

    [Fact]
    public void SendToBack_Revert_RestoresAll()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        bus.Execute(new ChangeZOrderCommand(2, 0, ZOrderOperation.SendToBack));
        bus.Undo();

        ImageAt(doc, 2).ZOrderIndex.Should().Be(5);
    }

    // ── BringForward ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BringForward_SwapsWithNextHigherNeighbor()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        // Target image 0 (z=1); its next-higher neighbor is image 1 (z=3). They should swap.
        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.BringForward));

        ImageAt(doc, 0).ZOrderIndex.Should().Be(3);
        ImageAt(doc, 1).ZOrderIndex.Should().Be(1);
        ImageAt(doc, 2).ZOrderIndex.Should().Be(5); // unchanged
    }

    [Fact]
    public void BringForward_AlreadyAtTop_NoChange()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        // Target the top image (z=5) — no higher neighbor, so z stays the same.
        bus.Execute(new ChangeZOrderCommand(2, 0, ZOrderOperation.BringForward));

        ImageAt(doc, 2).ZOrderIndex.Should().Be(5);
    }

    [Fact]
    public void BringForward_AlreadyAtTop_DoesNotClearPendingRedo()
    {
        // Regression for shared-undo-redo-invalidation F1: a true no-op command must not
        // reach UndoRedoStack.Push (which unconditionally clears redo) via
        // DocumentCommandBus.Execute.
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);

        // A real edit, then undo it, leaves a pending redo.
        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.BringForward));
        bus.Undo();
        bus.CanRedo.Should().BeTrue();

        // BringForward on the already-topmost image mutates nothing (see
        // BringForward_AlreadyAtTop_NoChange above) and must not silently discard the
        // pending redo.
        bus.Execute(new ChangeZOrderCommand(2, 0, ZOrderOperation.BringForward));

        bus.CanRedo.Should().BeTrue();
        ImageAt(doc, 2).ZOrderIndex.Should().Be(5);
    }

    [Fact]
    public void BringForward_SwapsWithNextHigherNeighbor_StillClearsPendingRedo()
    {
        // Adjacent-case guard: a real (effectful) ZOrder edit must still invalidate any
        // pending redo, same as before the HasEffect gate was added.
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);

        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.BringForward));
        bus.Undo();
        bus.CanRedo.Should().BeTrue();

        // Real swap: image 1 (z=3) moves against image 2 (z=5) — an actual mutation.
        bus.Execute(new ChangeZOrderCommand(1, 0, ZOrderOperation.BringForward));

        bus.CanRedo.Should().BeFalse();
        ImageAt(doc, 1).ZOrderIndex.Should().Be(5);
        ImageAt(doc, 2).ZOrderIndex.Should().Be(3);
    }

    [Fact]
    public void BringForward_Revert_RestoresAll()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.BringForward));
        bus.Undo();

        ImageAt(doc, 0).ZOrderIndex.Should().Be(1);
        ImageAt(doc, 1).ZOrderIndex.Should().Be(3);
    }

    // ── SendBackward ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SendBackward_SwapsWithNextLowerNeighbor()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        // Target image 2 (z=5); its next-lower neighbor is image 1 (z=3). They should swap.
        bus.Execute(new ChangeZOrderCommand(2, 0, ZOrderOperation.SendBackward));

        ImageAt(doc, 2).ZOrderIndex.Should().Be(3);
        ImageAt(doc, 1).ZOrderIndex.Should().Be(5);
        ImageAt(doc, 0).ZOrderIndex.Should().Be(1); // unchanged
    }

    [Fact]
    public void SendBackward_AlreadyAtBottom_NoChange()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        // Target the bottom image (z=1) — no lower neighbor, so z stays the same.
        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.SendBackward));

        ImageAt(doc, 0).ZOrderIndex.Should().Be(1);
    }

    [Fact]
    public void SendBackward_AlreadyAtBottom_DoesNotClearPendingRedo()
    {
        // Same defect as BringForward_AlreadyAtTop_DoesNotClearPendingRedo, mirrored for the
        // SendBackward no-neighbor branch.
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);

        bus.Execute(new ChangeZOrderCommand(2, 0, ZOrderOperation.SendBackward));
        bus.Undo();
        bus.CanRedo.Should().BeTrue();

        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.SendBackward));

        bus.CanRedo.Should().BeTrue();
        ImageAt(doc, 0).ZOrderIndex.Should().Be(1);
    }

    [Fact]
    public void SendBackward_Revert_RestoresAll()
    {
        var (doc, bus) = ThreeFloatingDoc(1, 3, 5);
        bus.Execute(new ChangeZOrderCommand(2, 0, ZOrderOperation.SendBackward));
        bus.Undo();

        ImageAt(doc, 2).ZOrderIndex.Should().Be(5);
        ImageAt(doc, 1).ZOrderIndex.Should().Be(3);
    }

    // ── Inline images are not included in z-order operations ─────────────────────────────────────

    [Fact]
    public void InlineImages_AreIgnored_ByCollectFloating()
    {
        var doc = new TextDocument();
        doc.Blocks.Clear();
        var para = new Paragraph();
        // Inline image (IsFloating = false)
        para.Runs.Add(Run.FromImage(new InlineImage(Png(), 60, 60)));
        doc.Blocks.Add(para);
        var bus = new DocumentCommandBus(new TestContext(doc));

        // BringToFront on an inline image's block/run should be a no-op.
        bus.Execute(new ChangeZOrderCommand(0, 0, ZOrderOperation.BringToFront));

        ((Paragraph)doc.Blocks[0]).Runs[0].Image!.ZOrderIndex.Should().Be(0);
    }

    // ── Minimal test context ─────────────────────────────────────────────────────────────────────

    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }
}
