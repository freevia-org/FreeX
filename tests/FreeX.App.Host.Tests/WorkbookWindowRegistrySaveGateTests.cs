using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R115-app-host-save-race: unit coverage for <see cref="WorkbookWindowRegistry.BroadcastSaveInProgress"/>,
/// the pure scoping logic MainWindow.Backstage.cs's <c>SaveWorkbookToTargetAsync</c> relies on to
/// extend its save-input gate to "New Window" siblings sharing the document being saved -- without
/// reaching windows over an unrelated document, and without re-applying to the originating window
/// itself (which already gated its own input directly).
/// </summary>
public sealed class WorkbookWindowRegistrySaveGateTests
{
    [Fact]
    public void BroadcastSaveInProgress_AppliesOnlyToOtherWindowsOfTheSameDocument()
    {
        var registry = new WorkbookWindowRegistry();
        var origin = new TestWorkbookWindow { DocumentId = new WorkbookId(Guid.NewGuid()) };
        var sameDocumentSibling = new TestWorkbookWindow { DocumentId = origin.DocumentId };
        var otherDocumentWindow = new TestWorkbookWindow { DocumentId = new WorkbookId(Guid.NewGuid()) };
        registry.Register(origin);
        registry.Register(sameDocumentSibling);
        registry.Register(otherDocumentWindow);

        registry.BroadcastSaveInProgress(origin, inProgress: true);

        origin.SaveInProgressAppliedCount.Should().Be(0,
            "the originating window applies the gate to its own input directly, not through the broadcast");
        sameDocumentSibling.SaveInProgressAppliedCount.Should().Be(1);
        sameDocumentSibling.LastAppliedSaveInProgress.Should().BeTrue();
        otherDocumentWindow.SaveInProgressAppliedCount.Should().Be(0,
            "a window over an unrelated document must not be gated by another document's save");

        registry.BroadcastSaveInProgress(origin, inProgress: false);

        sameDocumentSibling.SaveInProgressAppliedCount.Should().Be(2);
        sameDocumentSibling.LastAppliedSaveInProgress.Should().BeFalse();
        otherDocumentWindow.SaveInProgressAppliedCount.Should().Be(0);
    }

    [Fact]
    public void BroadcastSaveInProgress_SingleWindow_DoesNothing()
    {
        var registry = new WorkbookWindowRegistry();
        var origin = new TestWorkbookWindow();
        registry.Register(origin);

        registry.BroadcastSaveInProgress(origin, inProgress: true);

        origin.SaveInProgressAppliedCount.Should().Be(0);
    }
}
