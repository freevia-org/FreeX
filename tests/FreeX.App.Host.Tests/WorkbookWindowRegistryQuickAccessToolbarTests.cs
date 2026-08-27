using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// shared-window-lifecycle F1: the Quick Access Toolbar customization (command set and
/// below-ribbon placement) lives in the single process-wide AppOptions instance every MainWindow
/// shares, so a change made in one window must be reflected live in every OTHER open window in the
/// process -- across every document, not just windows sharing this window's document -- while
/// leaving the originating window (which already applied the change to itself) untouched. Mirrors
/// <see cref="WorkbookWindowRegistryFormulaBarTests"/> for the analogous, already-fixed
/// Show Formula Bar broadcast.
/// </summary>
public sealed class WorkbookWindowRegistryQuickAccessToolbarTests
{
    [Fact]
    public void BroadcastQuickAccessToolbarChanged_AppliesToEveryOtherWindow_AcrossDifferentDocuments()
    {
        var registry = new WorkbookWindowRegistry();
        var origin = new TestWorkbookWindow { DocumentId = new WorkbookId(Guid.NewGuid()) };
        var sameDocumentSibling = new TestWorkbookWindow { DocumentId = origin.DocumentId };
        var otherDocumentWindow = new TestWorkbookWindow { DocumentId = new WorkbookId(Guid.NewGuid()) };
        registry.Register(origin);
        registry.Register(sameDocumentSibling);
        registry.Register(otherDocumentWindow);

        registry.BroadcastQuickAccessToolbarChanged(origin);

        origin.QuickAccessToolbarChangedAppliedCount.Should().Be(0,
            "the originating window already rebuilt its own Quick Access Toolbar");
        sameDocumentSibling.QuickAccessToolbarChangedAppliedCount.Should().Be(1);
        otherDocumentWindow.QuickAccessToolbarChangedAppliedCount.Should().Be(1,
            "the Quick Access Toolbar customization is Excel-instance-wide, not scoped to one document");
    }

    [Fact]
    public void BroadcastQuickAccessToolbarChanged_SingleWindow_DoesNothing()
    {
        var registry = new WorkbookWindowRegistry();
        var origin = new TestWorkbookWindow();
        registry.Register(origin);

        registry.BroadcastQuickAccessToolbarChanged(origin);

        origin.QuickAccessToolbarChangedAppliedCount.Should().Be(0);
    }
}
