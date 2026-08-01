using System.Windows;
using System.Windows.Controls;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host.Tests;

public sealed class WpfOleInPlaceHostTests
{
    [StaFact]
    public void EmptyPayloadFallsBackWithoutAddingAHost()
    {
        var overlay = new Canvas();
        var result = WpfOleInPlaceHost.TryShow(
            overlay,
            new OleObjectInfo(),
            new Rect(10, 20, 100, 80),
            out var host);

        result.Should().BeFalse();
        host.Should().BeNull();
        overlay.Children.Count.Should().Be(0);
    }

    [StaFact]
    public void MissingModelDoesNotAttemptInPlaceActivation()
    {
        var overlay = new Canvas();
        WpfOleInPlaceHost.TryShow(overlay, null, new Rect(0, 0, 100, 80), out _)
            .Should().BeFalse();
        overlay.Children.Count.Should().Be(0);
    }
}
