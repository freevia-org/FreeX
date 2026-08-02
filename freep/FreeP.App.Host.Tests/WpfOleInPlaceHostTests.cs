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

    [StaFact]
    public void InlineAttachmentRejectsEmptyPayloadWithoutReplacingFallback()
    {
        var fallback = new TextBlock { Text = "OLE" };
        var container = new Border { Child = fallback };

        WpfOleInPlaceHost.AttachInline(
                container,
                new InlineOleObjectInfo(),
                width: 42,
                height: 20)
            .Should().BeFalse();
        container.Child.Should().BeSameAs(fallback);
    }

    [StaFact]
    public void InlineAttachmentDefersNativeHostUntilContainerLoads()
    {
        var fallback = new TextBlock { Text = "OLE" };
        var container = new Border { Child = fallback };
        var inline = new InlineOleObjectInfo
        {
            EmbeddedBytes = [1, 2, 3],
            FileName = "embedded.bin",
        };

        WpfOleInPlaceHost.AttachInline(container, inline, width: 42, height: 20)
            .Should().BeTrue();
        container.Child.Should().BeSameAs(fallback);
    }
}
