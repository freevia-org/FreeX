using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationWorkareaSessionTests
{
    [Fact]
    public void ConstructorOwnsPresentationEditorAndSelectionSnapshot()
    {
        var presentation = Presentation.CreateEmpty();
        var endpoint = new RecordingEndpoint();

        using var session = new PresentationWorkareaSession(endpoint, presentation);
        session.Editor.Select(41);

        session.Presentation.Should().BeSameAs(presentation);
        session.Editor.Presentation.Should().BeSameAs(presentation);
        session.Snapshot.SelectedShapeIds.Should().Equal(41u);
    }

    [Fact]
    public void InitializeExecutesPortableBootstrapOnce()
    {
        var endpoint = new RecordingEndpoint();
        using var session = new PresentationWorkareaSession(endpoint);

        session.Initialize();
        session.Initialize();

        endpoint.Transitions.Should().OnlyContain(t => t == PresentationWorkareaTransition.Bootstrap);
        endpoint.Operations.Should().Equal(
            PresentationWorkareaOperationPlanner.BuildBootstrap().Operations);
    }

    [Fact]
    public void ReplacePresentationPublishesOldStateBeforeReplacementAndNewStateAfterward()
    {
        var original = Presentation.CreateEmpty();
        var replacement = Presentation.CreateEmpty();
        replacement.Slides.Add(new Slide { Title = "Second" });
        var endpoint = new RecordingEndpoint();
        using var session = new PresentationWorkareaSession(endpoint, original);
        var originalEditor = session.Editor;

        session.ReplacePresentation(replacement);

        endpoint.Operations[0].Should().Be(PresentationWorkareaOperation.BeforePresentationReplaced);
        endpoint.Presentations[0].Should().BeSameAs(original);
        endpoint.Operations.Skip(1).Should().Equal(
            PresentationWorkareaOperationPlanner.BuildPresentationReplaced().Operations);
        endpoint.Presentations.Skip(1).Should().OnlyContain(p => ReferenceEquals(p, replacement));
        session.Editor.Should().NotBeSameAs(originalEditor);
        session.Editor.Presentation.Should().BeSameAs(replacement);
    }

    [Fact]
    public void EditorMutationExecutesDirtyAndRefreshPlanFromRealSubscription()
    {
        var endpoint = new RecordingEndpoint { SmartArtVisible = true };
        using var session = new PresentationWorkareaSession(endpoint);

        session.Editor.SetSlideTitle(0, "Renamed").Should().BeTrue();

        endpoint.Operations.Should().Equal(
            PresentationWorkareaOperationPlanner.BuildEditorChanged(isSmartArtPaneVisible: true).Operations);
        endpoint.Transitions.Should().OnlyContain(t => t == PresentationWorkareaTransition.EditorChanged);
    }

    [Fact]
    public void CurrentSlideAndSelectionTransitionsUseCurrentPortableState()
    {
        var endpoint = new RecordingEndpoint
        {
            AltTextVisible = true,
            SmartArtVisible = false,
        };
        using var session = new PresentationWorkareaSession(endpoint);
        session.Editor.InsertSlide();
        endpoint.Clear();

        session.Editor.SelectSlide(0);

        endpoint.Operations.Should().Equal(
            PresentationWorkareaOperationPlanner.BuildCurrentSlideChanged().Operations);
        endpoint.CurrentSlideIndices.Should().OnlyContain(index => index == 0);

        endpoint.Clear();
        session.Editor.Select(73);

        endpoint.Operations.Should().Equal(
            PresentationWorkareaOperationPlanner.BuildSelectionChanged(
                isAltTextPaneVisible: true,
                isSmartArtPaneVisible: false).Operations);
        endpoint.SelectedShapeIds.Should().OnlyContain(ids => ids.SequenceEqual(new[] { 73u }));
    }

    [Fact]
    public void ReplacementDetachesTheOldEditor()
    {
        var endpoint = new RecordingEndpoint();
        using var session = new PresentationWorkareaSession(endpoint);
        var oldEditor = session.Editor;

        session.ReplacePresentation(Presentation.CreateEmpty());
        endpoint.Clear();
        oldEditor.Select(9);

        endpoint.Operations.Should().BeEmpty();
    }

    [Fact]
    public void CommandsKeepEditorMeaningPortableAndForwardOnlyNativeEdges()
    {
        var endpoint = new RecordingEndpoint();
        using var session = new PresentationWorkareaSession(endpoint);

        foreach (var command in Enum.GetValues<FreePKeyboardCommand>())
            session.ExecuteCommand(command);

        endpoint.NativeCommands.Should().Equal(
            PresentationWorkareaNativeCommand.NewPresentation,
            PresentationWorkareaNativeCommand.OpenPresentation,
            PresentationWorkareaNativeCommand.SavePresentation,
            PresentationWorkareaNativeCommand.SavePresentationAs,
            PresentationWorkareaNativeCommand.PrintPresentation,
            PresentationWorkareaNativeCommand.StartSlideShowFromBeginning,
            PresentationWorkareaNativeCommand.StartSlideShowFromCurrentSlide,
            PresentationWorkareaNativeCommand.Copy,
            PresentationWorkareaNativeCommand.Cut,
            PresentationWorkareaNativeCommand.Paste,
            PresentationWorkareaNativeCommand.Find,
            PresentationWorkareaNativeCommand.Replace);
    }

    [Fact]
    public void StatusPlanProjectsTheCurrentSlideAndDeckCount()
    {
        using var session = new PresentationWorkareaSession(new RecordingEndpoint());
        session.Editor.InsertSlide();

        var plan = session.BuildStatusPlan("Data: test");

        plan.CurrentSlideIndex.Should().Be(1);
        plan.SlideCount.Should().Be(2);
        plan.Text.Should().Contain("Slide 2 / 2").And.Contain("Data: test");
    }

    [Fact]
    public void DomainDialogAvailabilityTracksSelectionAndChartTypeInSharedWorkarea()
    {
        using var session = new PresentationWorkareaSession(new RecordingEndpoint());

        Enum.GetValues<PresentationDomainDialogKind>()
            .Should().OnlyContain(kind => !session.CanOpenDomainDialog(kind));

        var bubble = session.Editor.InsertChart(ChartType.Bubble);
        session.Editor.Select(bubble.Id);

        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartData).Should().BeTrue();
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartDisplayOptions).Should().BeTrue();
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartBubbleOptions).Should().BeTrue();
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartPieOptions).Should().BeFalse();
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartPlotStyleOptions).Should().BeFalse();
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartExSeriesLayout).Should().BeFalse();
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartProtectionOptions).Should().BeTrue();
        session.CanOpenDomainDialog(PresentationDomainDialogKind.RotationOptions).Should().BeTrue();

        var pie = session.Editor.InsertChart(ChartType.Pie);
        session.Editor.Select(pie.Id);
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartPieOptions).Should().BeTrue();

        var scatter = session.Editor.InsertChart(ChartType.Scatter);
        session.Editor.Select(scatter.Id);
        session.CanOpenDomainDialog(PresentationDomainDialogKind.ChartPlotStyleOptions).Should().BeTrue();
    }

    private sealed class RecordingEndpoint : IPresentationWorkareaEndpoint
    {
        public bool AltTextVisible { get; init; }

        public bool SmartArtVisible { get; init; }

        public List<PresentationWorkareaOperation> Operations { get; } = [];

        public List<PresentationWorkareaTransition> Transitions { get; } = [];

        public List<Presentation> Presentations { get; } = [];

        public List<int> CurrentSlideIndices { get; } = [];

        public List<IReadOnlyList<uint>> SelectedShapeIds { get; } = [];

        public List<PresentationWorkareaNativeCommand> NativeCommands { get; } = [];

        public bool IsPaneVisible(PresentationWorkareaPane pane) => pane switch
        {
            PresentationWorkareaPane.AltText => AltTextVisible,
            PresentationWorkareaPane.SmartArtText => SmartArtVisible,
            _ => false,
        };

        public void Apply(
            PresentationWorkareaOperation operation,
            PresentationWorkareaContext context)
        {
            Operations.Add(operation);
            Transitions.Add(context.Transition);
            Presentations.Add(context.Snapshot.Presentation);
            CurrentSlideIndices.Add(context.Snapshot.CurrentSlideIndex);
            SelectedShapeIds.Add(context.Snapshot.SelectedShapeIds);
        }

        public void ExecuteNativeCommand(PresentationWorkareaNativeCommand command) =>
            NativeCommands.Add(command);

        public void Clear()
        {
            Operations.Clear();
            Transitions.Clear();
            Presentations.Clear();
            CurrentSlideIndices.Clear();
            SelectedShapeIds.Clear();
            NativeCommands.Clear();
        }
    }
}
