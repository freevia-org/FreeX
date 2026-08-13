using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationWorkareaEndpointDispatcherTests
{
    [Fact]
    public void PaneVisibilitySessionOwnsRequestsContentPolicyAndSnapshots()
    {
        var panes = new PresentationWorkareaPaneSession();

        panes.Show(PresentationWorkareaPane.AltText).Current.IsVisible.Should().BeTrue();
        panes.IsRequested(PresentationWorkareaPane.AltText).Should().BeTrue();
        panes.Hide(PresentationWorkareaPane.AltText).Current.IsVisible.Should().BeFalse();

        var content = panes.ResolveVisibility(
            PresentationWorkareaPane.ReviewComments,
            hasContent: true,
            PresentationWorkareaPaneVisibilityPolicy.RequestedOrContent);
        content.Current.IsRequested.Should().BeFalse();
        content.Current.IsVisible.Should().BeTrue();
        panes.BuildSnapshot().Should().HaveCount(Enum.GetValues<PresentationWorkareaPane>().Length);
    }

    [Fact]
    public void OperationDispatchNormalizesEditorAndTransitionArguments()
    {
        using var session = new PresentationWorkareaSession(new NoopEndpoint());
        EditingSession? boundEditor = null;
        PresentationWorkareaTransition? statusTransition = null;
        var dirtyCount = 0;
        var endpoints = new PresentationWorkareaOperationEndpoints
        {
            BindEditor = editor => boundEditor = editor,
            MarkDirty = () => dirtyCount++,
            RefreshDocumentStatusBeforeReview = transition => statusTransition = transition,
        };
        var context = new PresentationWorkareaContext(
            PresentationWorkareaTransition.EditorChanged,
            session.Snapshot);

        PresentationWorkareaEndpointDispatcher.Dispatch(
            PresentationWorkareaOperation.BindEditor,
            context,
            endpoints).Should().BeTrue();
        PresentationWorkareaEndpointDispatcher.Dispatch(
            PresentationWorkareaOperation.MarkDirty,
            context,
            endpoints).Should().BeTrue();
        PresentationWorkareaEndpointDispatcher.Dispatch(
            PresentationWorkareaOperation.RefreshDocumentStatusBeforeReview,
            context,
            endpoints).Should().BeTrue();
        PresentationWorkareaEndpointDispatcher.Dispatch(
            PresentationWorkareaOperation.RefreshCanvas,
            context,
            endpoints).Should().BeFalse();

        boundEditor.Should().BeSameAs(session.Editor);
        dirtyCount.Should().Be(1);
        statusTransition.Should().Be(PresentationWorkareaTransition.EditorChanged);
    }

    [Fact]
    public void NativeCommandsAreClassifiedOnceAndInvokeTheMatchingEndpoint()
    {
        var invoked = new List<PresentationWorkareaNativeCommand>();
        Action Add(PresentationWorkareaNativeCommand command) => () => invoked.Add(command);
        var endpoint = new PresentationWorkareaEndpoint(new PresentationWorkareaEndpointProfile
        {
            NativeCommands = new PresentationWorkareaNativeCommandEndpoints
            {
                NewPresentation = Add(PresentationWorkareaNativeCommand.NewPresentation),
                OpenPresentation = Add(PresentationWorkareaNativeCommand.OpenPresentation),
                SavePresentation = Add(PresentationWorkareaNativeCommand.SavePresentation),
                SavePresentationAs = Add(PresentationWorkareaNativeCommand.SavePresentationAs),
                PrintPresentation = Add(PresentationWorkareaNativeCommand.PrintPresentation),
                StartSlideShowFromBeginning = Add(PresentationWorkareaNativeCommand.StartSlideShowFromBeginning),
                StartSlideShowFromCurrentSlide =
                    Add(PresentationWorkareaNativeCommand.StartSlideShowFromCurrentSlide),
                Copy = Add(PresentationWorkareaNativeCommand.Copy),
                Cut = Add(PresentationWorkareaNativeCommand.Cut),
                Paste = Add(PresentationWorkareaNativeCommand.Paste),
                Find = Add(PresentationWorkareaNativeCommand.Find),
                Replace = Add(PresentationWorkareaNativeCommand.Replace),
            },
        });

        foreach (var command in Enum.GetValues<PresentationWorkareaNativeCommand>())
            endpoint.ExecuteNativeCommand(command);

        invoked.Should().Equal(Enum.GetValues<PresentationWorkareaNativeCommand>());
    }

    [Fact]
    public void KeyboardCommandsHaveAnExhaustivePortableTargetRoute()
    {
        var routes = Enum.GetValues<FreePKeyboardCommand>()
            .Select(PresentationWorkareaCommandRoutePlanner.Build)
            .ToArray();

        routes.Should().HaveCount(17);
        routes.Count(route => route.Target == PresentationWorkareaCommandTarget.Editor).Should().Be(5);
        routes.Count(route => route.Target == PresentationWorkareaCommandTarget.NativeEndpoint).Should().Be(12);
        routes.Where(route => route.Target == PresentationWorkareaCommandTarget.Editor)
            .Should().OnlyContain(route => route.EditorCommand.HasValue && !route.NativeCommand.HasValue);
        routes.Where(route => route.Target == PresentationWorkareaCommandTarget.NativeEndpoint)
            .Should().OnlyContain(route => route.NativeCommand.HasValue && !route.EditorCommand.HasValue);
    }

    [Theory]
    [InlineData(PresentationWorkareaTransition.Bootstrap, true, false)]
    [InlineData(PresentationWorkareaTransition.PresentationReplaced, false, true)]
    [InlineData(PresentationWorkareaTransition.EditorChanged, true, true)]
    [InlineData(PresentationWorkareaTransition.SelectionChanged, false, false)]
    public void StatusRefreshPolicyIsPortable(
        PresentationWorkareaTransition transition,
        bool refreshTitle,
        bool refreshSlideCount)
    {
        var before = PresentationWorkareaStatusRefreshPlanner.BuildBeforeReview(transition);

        before.Should().Be(new PresentationWorkareaStatusRefreshPlan(refreshTitle, refreshSlideCount));
        PresentationWorkareaStatusRefreshPlanner.BuildAfterReview(transition)
            .Should().Be(new PresentationWorkareaStatusRefreshPlan(
                RefreshTitle: false,
                RefreshSlideCount: transition == PresentationWorkareaTransition.Bootstrap));
    }

    private sealed class NoopEndpoint : IPresentationWorkareaEndpoint
    {
        public void Apply(
            PresentationWorkareaOperation operation,
            PresentationWorkareaContext context)
        {
        }

        public void ExecuteNativeCommand(PresentationWorkareaNativeCommand command)
        {
        }
    }
}
