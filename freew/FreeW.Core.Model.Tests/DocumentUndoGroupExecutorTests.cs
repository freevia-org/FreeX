using System.Reflection;
using Free.Shared.Commands;

namespace FreeW.Core.Model.Tests;

public sealed class DocumentUndoGroupExecutorTests
{
    [Fact]
    public void Execute_MultipleCommandsCommitsOneDescribedUndoEntry()
    {
        var (document, commandBus) = Create();
        var changed = 0;
        commandBus.Changed += () => changed++;

        DocumentUndoGroupExecutor.Execute(
            commandBus,
            [
                new InsertParagraphCommand(0, new Paragraph("first")),
                new InsertParagraphCommand(1, new Paragraph("second")),
            ],
            "Insert Pair");

        document.PlainText.Should().Be("first\nsecond");
        changed.Should().Be(1);
        UndoDescription(commandBus).Should().Be("Insert Pair");

        commandBus.Undo().Should().BeTrue();
        document.Blocks.Should().BeEmpty();
        changed.Should().Be(2);
        commandBus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Execute_SingleCommandPreservesCommandDescriptionAndNotification()
    {
        var (document, commandBus) = Create();
        var changed = 0;
        commandBus.Changed += () => changed++;

        DocumentUndoGroupExecutor.Execute(
            commandBus,
            [new InsertParagraphCommand(0, new Paragraph("only"))],
            "Unused Group Description");

        document.PlainText.Should().Be("only");
        changed.Should().Be(1);
        UndoDescription(commandBus).Should().Be("Insert Paragraph");
    }

    [Fact]
    public void Execute_FailureRollsBackOwnedGroupInReverseOrderAndRethrows()
    {
        var (_, commandBus) = Create();
        var events = new List<string>();
        var failure = new InvalidOperationException("apply failed");

        Action act = () => DocumentUndoGroupExecutor.Execute(
            commandBus,
            [
                new RecordingCommand("first", events),
                new RecordingCommand("second", events),
                new RecordingCommand("third", events, failure),
            ],
            "Failing Group");

        act.Should().Throw<InvalidOperationException>()
            .Which.Should().BeSameAs(failure);
        events.Should().Equal(
            "apply:first",
            "apply:second",
            "apply:third",
            "revert:second",
            "revert:first");
        commandBus.IsUndoGroupOpen.Should().BeFalse();
        commandBus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Execute_InsideOuterGroupLeavesCommitAndDescriptionToOuterOwner()
    {
        var (document, commandBus) = Create();
        var changed = 0;
        commandBus.Changed += () => changed++;
        commandBus.BeginUndoGroup();

        DocumentUndoGroupExecutor.Execute(
            commandBus,
            [
                new InsertParagraphCommand(0, new Paragraph("first")),
                new InsertParagraphCommand(1, new Paragraph("second")),
            ],
            "Inner Description");

        commandBus.IsUndoGroupOpen.Should().BeTrue();
        changed.Should().Be(0);
        commandBus.CommitUndoGroup("Outer Description");
        changed.Should().Be(1);
        UndoDescription(commandBus).Should().Be("Outer Description");

        commandBus.Undo().Should().BeTrue();
        document.Blocks.Should().BeEmpty();
    }

    [Fact]
    public void Execute_NestedFailureLeavesRollbackToOuterOwner()
    {
        var (document, commandBus) = Create();
        var failure = new InvalidOperationException("nested failure");
        commandBus.BeginUndoGroup();

        Action act = () => DocumentUndoGroupExecutor.Execute(
            commandBus,
            [
                new InsertParagraphCommand(0, new Paragraph("temporary")),
                new RecordingCommand("failure", [], failure),
            ],
            "Nested Group");

        act.Should().Throw<InvalidOperationException>()
            .Which.Should().BeSameAs(failure);
        commandBus.IsUndoGroupOpen.Should().BeTrue();
        document.PlainText.Should().Be("temporary");

        commandBus.RollbackUndoGroup();
        document.Blocks.Should().BeEmpty();
        commandBus.IsUndoGroupOpen.Should().BeFalse();
        commandBus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Execute_NoEffectCommandsCreateNoHistoryOrNotification()
    {
        var (_, commandBus) = Create();
        var changed = 0;
        commandBus.Changed += () => changed++;

        DocumentUndoGroupExecutor.Execute(
            commandBus,
            [new NoEffectCommand(), new NoEffectCommand()],
            "No Change");

        changed.Should().Be(0);
        commandBus.IsUndoGroupOpen.Should().BeFalse();
        commandBus.CanUndo.Should().BeFalse();
    }

    private static (TextDocument Document, DocumentCommandBus CommandBus) Create()
    {
        var document = new TextDocument();
        return (document, new DocumentCommandBus(new Context(document)));
    }

    private static string UndoDescription(DocumentCommandBus commandBus)
    {
        var field = typeof(DocumentCommandBus).GetField("_stack", BindingFlags.Instance | BindingFlags.NonPublic);
        var stack = field!.GetValue(commandBus)
            .Should().BeOfType<UndoRedoStack<IDocumentCommand, object?>>()
            .Subject;
        return stack.GetUndoHistory(1).Single().Label;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private sealed class RecordingCommand(
        string name,
        ICollection<string> events,
        Exception? failure = null) : IDocumentCommand
    {
        public string Label => name;

        public void Apply(IDocumentCommandContext context)
        {
            events.Add($"apply:{name}");
            if (failure is not null)
                throw failure;
        }

        public void Revert(IDocumentCommandContext context) => events.Add($"revert:{name}");
    }

    private sealed class NoEffectCommand : IDocumentCommand
    {
        public string Label => "No effect";
        public bool HasEffect(IDocumentCommandContext context) => false;
        public void Apply(IDocumentCommandContext context) => throw new InvalidOperationException();
        public void Revert(IDocumentCommandContext context) => throw new InvalidOperationException();
    }
}
