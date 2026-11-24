namespace FreeW.Core.Model.Tests;

public sealed class DocumentCommandScopeTests
{
    [Fact]
    public void QuietScopeRetainsLegacyGroupOwnershipAndPublishesOnlyTheCompletedGesture()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        var appliedCounts = new List<int>();
        var notifiedCounts = new List<int>();
        bus.Changed += () => notifiedCounts.Add(document.Blocks.Count);
        using var scope = bus.BeginCommandScope(() => appliedCounts.Add(document.Blocks.Count));
        bus.IsUndoGroupOpen.Should().BeFalse();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("first")));
        bus.BeginUndoGroup();
        bus.Execute(new InsertParagraphCommand(1, new Paragraph("second")));
        bus.CommitUndoGroup("Legacy group");
        notifiedCounts.Should().BeEmpty();
        appliedCounts.Should().Equal(1, 2);

        scope.Commit("Gesture").Should().BeTrue();

        notifiedCounts.Should().Equal(2);
        bus.IsUndoGroupOpen.Should().BeFalse();
        bus.Undo().Should().BeTrue();
        document.Blocks.Should().BeEmpty();
        bus.CanUndo.Should().BeFalse();
        bus.Redo().Should().BeTrue();
        document.Paragraphs.Select(p => p.PlainText).Should().Equal("first", "second");
    }

    [Fact]
    public void NestedScopesDecorateOnlyTheirTailInsideTheRealOuterGroup()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        var notifications = 0;
        bus.Changed += () => notifications++;
        bus.BeginUndoGroup(notifyOnEachExecute: true);
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("before")));
        using (var outer = bus.BeginCommandScope())
        {
            bus.IsUndoGroupOpen.Should().BeTrue();
            bus.Execute(new InsertParagraphCommand(1, new Paragraph("outer")));
            using (var inner = bus.BeginCommandScope())
            {
                bus.Execute(new InsertParagraphCommand(2, new Paragraph("inner")));
                inner.Commit("Inner", command => new ForwardingCommand(command)).Should().BeTrue();
            }
            notifications.Should().Be(1);
            outer.Commit("Gesture", command => new ForwardingCommand(command)).Should().BeTrue();
        }
        notifications.Should().Be(2);
        bus.CanUndo.Should().BeFalse();
        bus.CommitUndoGroup("Font group");
        notifications.Should().Be(3);
        bus.Undo();
        document.Blocks.Should().BeEmpty();
        bus.CanUndo.Should().BeFalse();
        bus.Redo();
        document.Paragraphs.Select(p => p.PlainText).Should().Equal("before", "outer", "inner");
    }

    [Fact]
    public void NoOpScopeDoesNotPublishOrClearRedo()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("redo")));
        bus.Undo();
        var notifications = 0;
        bus.Changed += () => notifications++;
        using var scope = bus.BeginCommandScope(() => throw new InvalidOperationException("no source command applied"));
        bus.Execute(new NoOpCommand());

        scope.HasCommands.Should().BeFalse();
        scope.Commit("No-op").Should().BeFalse();

        notifications.Should().Be(0);
        bus.CanUndo.Should().BeFalse();
        bus.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void ThrowingDecoratorRollsBackOnlyTheGestureAndPreservesOuterGroupAndRedo()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("redo")));
        bus.Undo();
        bus.BeginUndoGroup();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("before")));
        using var scope = bus.BeginCommandScope();
        bus.Execute(new InsertParagraphCommand(1, new Paragraph("gesture")));

        Action commit = () => scope.Commit("Gesture", _ => throw new InvalidOperationException("decorator"));
        commit.Should().Throw<InvalidOperationException>().WithMessage("decorator");

        scope.IsCompleted.Should().BeTrue();
        scope.IsCommitted.Should().BeFalse();
        document.Paragraphs.Select(p => p.PlainText).Should().Equal("before");
        bus.IsUndoGroupOpen.Should().BeTrue();
        bus.CanRedo.Should().BeTrue();
        bus.CommitUndoGroup("Outer");
        bus.Undo();
        document.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void ThrowingChangedObserverCannotRollbackTheCommittedGestureOnDispose()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        Action observer = () => throw new InvalidOperationException("observer");
        bus.Changed += observer;
        using (var scope = bus.BeginCommandScope())
        {
            bus.Execute(new InsertParagraphCommand(0, new Paragraph("edit")));
            Action commit = () => scope.Commit("Gesture");
            commit.Should().Throw<InvalidOperationException>().WithMessage("observer");
            scope.IsCommitted.Should().BeTrue();
        }

        document.PlainText.Should().Be("edit");
        bus.CanUndo.Should().BeTrue();
        bus.Changed -= observer;
        bus.Undo();
        document.Blocks.Should().BeEmpty();
        bus.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void AnAbortedLegacyGroupCannotLeaveAnAppliedUntrackedGesture()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        using var scope = bus.BeginCommandScope();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("first")));
        bus.BeginUndoGroup();
        bus.Execute(new InsertParagraphCommand(1, new Paragraph("aborted")));
        bus.AbortUndoGroup();

        Action commit = () => scope.Commit("Gesture");
        commit.Should().Throw<InvalidOperationException>().WithMessage("An aborted edit cannot be committed.");

        document.Blocks.Should().BeEmpty();
        bus.CanUndo.Should().BeFalse();
        bus.IsUndoGroupOpen.Should().BeFalse();
    }

    [Fact]
    public void AbortingTheGenuineOuterGroupInsideAScopeCannotRevertItsEarlierCommands()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        bus.BeginUndoGroup();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("outer")));
        using (var scope = bus.BeginCommandScope())
        {
            bus.Execute(new InsertParagraphCommand(1, new Paragraph("gesture")));
            bus.AbortUndoGroup();
            scope.Rollback().Should().BeEmpty();
        }

        document.PlainText.Should().Be("outer");
        bus.IsUndoGroupOpen.Should().BeTrue();
        bus.CommitUndoGroup("Outer");
        bus.Undo();
        document.Blocks.Should().BeEmpty();
        bus.Redo();
        document.PlainText.Should().Be("outer");
    }

    [Fact]
    public void SuccessfulRollbackRefreshesSourceCachesWithoutPublishingAChange()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        var cachedCount = -1;
        var notifications = 0;
        bus.Changed += () => notifications++;
        using var scope = bus.BeginCommandScope(() => cachedCount = document.Blocks.Count);
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("gesture")));
        cachedCount.Should().Be(1);

        scope.Rollback().Should().BeEmpty();

        cachedCount.Should().Be(0);
        notifications.Should().Be(0);
    }

    [Fact]
    public void FailedApplyAndTheEarlierSuccessfulSourceCommandsCanBothBeRestored()
    {
        var document = new TextDocument();
        var bus = new DocumentCommandBus(new Context(document));
        using (var scope = bus.BeginCommandScope())
        {
            bus.Execute(new InsertParagraphCommand(0, new Paragraph("first")));
            Action execute = () => bus.Execute(new PartialFailureCommand());
            execute.Should().Throw<InvalidOperationException>().WithMessage("apply");
            document.PlainText.Should().Be("first");
        }
        document.Blocks.Should().BeEmpty();
        bus.CanUndo.Should().BeFalse();
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private sealed class NoOpCommand : IDocumentCommand
    {
        public string Label => "No-op";
        public bool HasEffect(IDocumentCommandContext context) => false;
        public void Apply(IDocumentCommandContext context) => throw new InvalidOperationException();
        public void Revert(IDocumentCommandContext context) => throw new InvalidOperationException();
    }

    private sealed class ForwardingCommand(IDocumentCommand source) : IDocumentCommand
    {
        public string Label => source.Label;
        public int EstimatedBytes => source.EstimatedBytes;
        public DocumentCommandMutationKind MutationKind => source.MutationKind;
        public void Apply(IDocumentCommandContext context) => source.Apply(context);
        public void Revert(IDocumentCommandContext context) => source.Revert(context);
    }

    private sealed class PartialFailureCommand : IDocumentCommand
    {
        public string Label => "Partial failure";
        public void Apply(IDocumentCommandContext context)
        {
            context.Document.Blocks.Add(new Paragraph("partial"));
            throw new InvalidOperationException("apply");
        }
        public void Revert(IDocumentCommandContext context) => context.Document.Blocks.RemoveAt(context.Document.Blocks.Count - 1);
    }
}
