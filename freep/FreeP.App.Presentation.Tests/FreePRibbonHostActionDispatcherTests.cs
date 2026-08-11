using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePRibbonHostActionDispatcherTests
{
    [Fact]
    public void Dispatch_InvokesTypedEndpoint()
    {
        HeaderFooterCommandFocus? received = null;
        var endpoints = new FreePRibbonHostActionEndpoints
        {
            OpenHeaderFooter = focus => received = focus,
        };

        var handled = FreePRibbonHostActionDispatcher.Dispatch(
            new FreePRibbonHostAction(
                FreePRibbonHostActionKind.OpenHeaderFooter,
                HeaderFooterCommandFocus.Footer),
            endpoints);

        handled.Should().BeTrue();
        received.Should().Be(HeaderFooterCommandFocus.Footer);
    }

    [Fact]
    public void Dispatch_RejectsMissingOrMismatchedEndpoints()
    {
        var calls = 0;
        var endpoints = new FreePRibbonHostActionEndpoints
        {
            OpenHeaderFooter = _ => calls++,
        };

        FreePRibbonHostActionDispatcher.Dispatch(
                new FreePRibbonHostAction(FreePRibbonHostActionKind.Copy),
                endpoints)
            .Should().BeFalse();
        FreePRibbonHostActionDispatcher.Dispatch(
                new FreePRibbonHostAction(FreePRibbonHostActionKind.OpenHeaderFooter, "Footer"),
                endpoints)
            .Should().BeFalse();
        calls.Should().Be(0);
    }

    [Fact]
    public void EndpointCatalog_RemainsExhaustiveForHostActionKinds()
    {
        var endpointNames = typeof(FreePRibbonHostActionEndpoints)
            .GetProperties()
            .Select(property => property.Name);

        endpointNames.Should().BeEquivalentTo(
            Enum.GetNames<FreePRibbonHostActionKind>()
                .Except([nameof(FreePRibbonHostActionKind.DesignRequest)]));
        typeof(FreePRibbonDesignCommandEndpoints).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("OpenCustomSlideSize", "OpenLayoutPicker");
    }

    [Fact]
    public void RouterUsesBuiltInTableInsertionWhenNoPickerEndpointExists()
    {
        var editor = MakeEditor();

        FreePRibbonHostActionRouter.Dispatch(
                editor,
                new FreePRibbonHostAction(FreePRibbonHostActionKind.OpenTablePicker),
                new FreePRibbonHostActionEndpoints(),
                new FreePRibbonDesignCommandEndpoints())
            .Should().BeTrue();

        var table = editor.Presentation.Slides[0].Shapes.Should().ContainSingle().Subject.Table;
        table.Should().NotBeNull();
        table!.Rows.Should().HaveCount(3);
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 3);
    }

    [Fact]
    public void RouterUsesBuiltInHeaderFooterApplicationWhenNoDialogEndpointExists()
    {
        var editor = MakeEditor();

        FreePRibbonHostActionRouter.Dispatch(
                editor,
                new FreePRibbonHostAction(
                    FreePRibbonHostActionKind.OpenHeaderFooter,
                    HeaderFooterCommandFocus.Footer),
                new FreePRibbonHostActionEndpoints(),
                new FreePRibbonDesignCommandEndpoints())
            .Should().BeTrue();

        editor.Presentation.Slides[0].HfVisibility.Should().NotBeNull();
        editor.Presentation.Slides[0].HfVisibility!.ShowFooter.Should().BeTrue();
    }

    [Fact]
    public void RouterSelectsDesignSurfaceFromPortableIntent()
    {
        var editor = MakeEditor();
        var requests = new List<string>();
        var endpoints = new FreePRibbonDesignCommandEndpoints
        {
            OpenCustomSlideSize = plan => requests.Add($"custom:{plan.CommandId}"),
            OpenLayoutPicker = plan => requests.Add($"layout:{plan.CommandId}"),
        };
        var custom = PresentationDesignCommandPlanner.BuiltInPlans.Single(
            plan => plan.Intent == PresentationDesignCommandIntentKind.RequestCustomSlideSize);

        FreePRibbonHostActionRouter.Dispatch(
                editor,
                new FreePRibbonHostAction(FreePRibbonHostActionKind.DesignRequest, custom),
                new FreePRibbonHostActionEndpoints(),
                endpoints)
            .Should().BeTrue();
        FreePRibbonHostActionRouter.Dispatch(
                editor,
                new FreePRibbonHostAction(
                    FreePRibbonHostActionKind.DesignRequest,
                    PresentationDesignCommandPlanner.LayoutPlan),
                new FreePRibbonHostActionEndpoints(),
                endpoints)
            .Should().BeTrue();

        requests.Should().Equal(
            $"custom:{custom.CommandId}",
            $"layout:{PresentationDesignCommandPlanner.LayoutCommandId}");
    }

    private static EditingSession MakeEditor()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
