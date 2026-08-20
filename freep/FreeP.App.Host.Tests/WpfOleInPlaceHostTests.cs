using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using Free.Shared.Drawing;

namespace FreeP.App.Host.Tests;

public sealed class WpfOleInPlaceHostTests
{
    [StaFact]
    public void OleDoubleClick_UsesExternalRouteWhenInPlaceDeclines()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Ole,
            OleObject = new OleObjectInfo { EmbeddedBytes = [1, 2, 3], FileName = "Book.xlsx" },
        };
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(shape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var externalCalls = 0;

        using var handler = new CanvasGestureHandler(
            new SlideCanvas(),
            editor,
            tryOpenOleInPlace: _ => false,
            tryActivateOleExternally: ole =>
            {
                ole.Should().BeSameAs(shape.OleObject);
                externalCalls++;
                return true;
            });

        handler.HandleOleDoubleClickForTests(shape).Should().BeTrue();
        externalCalls.Should().Be(1);
    }

    [StaFact]
    public void OleDoubleClick_StopsAtInPlaceRoute()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.Ole,
            OleObject = new OleObjectInfo { EmbeddedBytes = [4, 5, 6] },
        };
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var externalCalls = 0;

        using var handler = new CanvasGestureHandler(
            new SlideCanvas(),
            editor,
            tryOpenOleInPlace: _ => true,
            tryActivateOleExternally: _ =>
            {
                externalCalls++;
                return true;
            });

        handler.HandleOleDoubleClickForTests(shape).Should().BeTrue();
        externalCalls.Should().Be(0);
    }

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

    [StaFact]
    public void CommitCallback_UpdatesModelAndNotifiesCaller_ForNativeInPlaceRoute()
    {
        // TryShow's native-activation branch cannot run headless (no real OLE server), so this
        // exercises the exact commit callback TryShow wires into WindowsOleInPlaceEngine -- the
        // same path CloseAndCommit invokes when a routine gesture (reselect, navigate slides)
        // closes the active in-place host per WindowsOleInPlaceEngine.CloseAndCommit.
        var oleObject = new OleObjectInfo { EmbeddedBytes = [1, 2, 3] };
        byte[]? notified = null;

        var commitCallback = WpfOleInPlaceHost.BuildCommitCallback(
            oleObject,
            bytes => notified = bytes);
        commitCallback([4, 5, 6, 7]);

        oleObject.EmbeddedBytes.Should().Equal(4, 5, 6, 7);
        notified.Should().Equal(4, 5, 6, 7);
    }

    [StaFact]
    public void CommitCallback_ToleratesNoObserver_ForNativeInPlaceRoute()
    {
        var oleObject = new OleObjectInfo { EmbeddedBytes = [1, 2, 3] };

        var commitCallback = WpfOleInPlaceHost.BuildCommitCallback(oleObject, onPayloadUpdated: null);
        Action act = () => commitCallback([9]);

        act.Should().NotThrow();
        oleObject.EmbeddedBytes.Should().Equal(9);
    }
}
