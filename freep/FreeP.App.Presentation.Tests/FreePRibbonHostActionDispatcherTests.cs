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

        endpointNames.Should().BeEquivalentTo(Enum.GetNames<FreePRibbonHostActionKind>());
    }
}
