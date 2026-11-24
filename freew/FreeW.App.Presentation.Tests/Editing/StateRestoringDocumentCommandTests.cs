using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class StateRestoringDocumentCommandTests
{
    [Fact]
    public void AlreadyAppliedScopeCommitDoesNotReapplyTheSourceAndRestoresBothHistoryEndpoints()
    {
        var context = new Context();
        var bus = new DocumentCommandBus(context);
        var trace = new List<string>();
        var state = "before";
        using (var scope = bus.BeginCommandScope())
        {
            bus.Execute(new SourceCommand(trace));
            state = "after";
            scope.Commit("Gesture", command => StateRestoringDocumentCommand<string>.FromApplied(
                command, "before", state, saved => state = saved, 256));
        }
        trace.Should().Equal("apply");
        state = "later navigation";
        bus.Undo();
        state.Should().Be("before");
        bus.Redo();
        state.Should().Be("after");
        trace.Should().Equal("apply", "revert", "apply");
    }

    [Fact]
    public void ApplyUndoRedoPublishCoherentModelAndStateWithoutRepeatingInitialPlacement()
    {
        var context = new Context();
        var bus = new DocumentCommandBus(context);
        var trace = new List<string>();
        var state = "before";
        var source = new SourceCommand(trace);
        var wrapper = new StateRestoringDocumentCommand<string>(source, state,
            () => { trace.Add("capture"); return state; },
            saved => { trace.Add("restore " + saved); state = saved; },
            () => { trace.Add("place"); state = "after"; }, estimatedStateBytes: 256);
        var observed = new List<(int Blocks, string State)>();
        bus.Changed += () =>
        {
            trace.Add("changed");
            observed.Add((context.Document.Blocks.Count, state));
        };

        bus.Execute(wrapper);
        state = "later user navigation";
        bus.Undo().Should().BeTrue();
        state = "different user navigation";
        bus.Redo().Should().BeTrue();

        observed.Should().Equal((1, "after"), (0, "before"), (1, "after"));
        trace.Should().Equal("apply", "place", "capture", "changed",
            "revert", "restore before", "changed", "apply", "restore after", "changed");
        bus.CanUndo.Should().BeTrue();
        bus.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void DescriptorAndContextDependentEffectAreDelegatedForEveryMutationKind()
    {
        var context = new Context();
        foreach (var kind in Enum.GetValues<DocumentCommandMutationKind>())
        {
            var source = new SourceCommand([], kind) { EffectContext = context };
            var wrapper = new StateRestoringDocumentCommand<int>(source, 0, () => 1, _ => { }, () => { }, 37);
            wrapper.Label.Should().Be(source.Label);
            wrapper.MutationKind.Should().Be(kind);
            wrapper.EstimatedBytes.Should().Be(110);
            wrapper.HasEffect(context).Should().BeTrue();
            wrapper.HasEffect(new Context()).Should().BeFalse();
        }
    }

    [Fact]
    public void NoOpSkipsPlacementAndStateCallbacksAndPreservesExistingRedo()
    {
        var context = new Context();
        var bus = new DocumentCommandBus(context);
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("existing")));
        bus.Undo().Should().BeTrue();
        var changes = 0;
        bus.Changed += () => changes++;
        var trace = new List<string>();
        var source = new SourceCommand(trace) { EffectContext = new Context() };
        var wrapper = new StateRestoringDocumentCommand<int>(source, 0,
            () => throw new InvalidOperationException("capture must not run"),
            _ => throw new InvalidOperationException("restore must not run"),
            () => throw new InvalidOperationException("place must not run"), 256);

        bus.Execute(wrapper);

        trace.Should().BeEmpty();
        changes.Should().Be(0);
        context.Document.Blocks.Should().BeEmpty();
        bus.CanUndo.Should().BeFalse();
        bus.CanRedo.Should().BeTrue();
        bus.Redo().Should().BeTrue();
        context.Document.PlainText.Should().Be("existing");
    }

    [Fact]
    public void AValueStateCanRestoreTheDefaultValueOnRedo()
    {
        var context = new Context();
        var bus = new DocumentCommandBus(context);
        var state = 7;
        var placements = 0;
        var wrapper = new StateRestoringDocumentCommand<int>(new SourceCommand([]), state, () => state,
            saved => state = saved, () => { state = 0; placements++; }, 16);

        bus.Execute(wrapper);
        bus.Undo();
        state.Should().Be(7);
        bus.Redo();
        state.Should().Be(0);
        placements.Should().Be(1);
    }

    [Fact]
    public void AddingStateToAMaximumSourceEstimateNeverWrapsTheUndoBudgetNegative()
    {
        var source = new SourceCommand([], estimatedBytes: int.MaxValue);
        var wrapper = new StateRestoringDocumentCommand<int>(source, 0, () => 1, _ => { }, () => { }, 256);

        wrapper.EstimatedBytes.Should().Be(int.MaxValue);
    }

    private sealed class Context : IDocumentCommandContext
    {
        public TextDocument Document { get; } = new();
    }

    private sealed class SourceCommand(List<string> trace,
        DocumentCommandMutationKind mutationKind = DocumentCommandMutationKind.BodyText,
        int estimatedBytes = 73) : IDocumentCommand
    {
        public IDocumentCommandContext? EffectContext { get; init; }
        public string Label => "Source edit";
        public int EstimatedBytes => estimatedBytes;
        public DocumentCommandMutationKind MutationKind => mutationKind;
        public bool HasEffect(IDocumentCommandContext context) => EffectContext is null || ReferenceEquals(context, EffectContext);

        public void Apply(IDocumentCommandContext context)
        {
            trace.Add("apply");
            context.Document.Blocks.Add(new Paragraph("after"));
        }

        public void Revert(IDocumentCommandContext context)
        {
            trace.Add("revert");
            context.Document.Blocks.RemoveAt(context.Document.Blocks.Count - 1);
        }
    }
}
