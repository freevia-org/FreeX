using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R83-app-view-modes-5-2: Show Formula Bar is a genuine Excel-instance-wide display preference,
/// so toggling it in one window must be reflected live in every OTHER open window in the process
/// -- across every document, not just windows sharing this window's document -- while leaving the
/// originating window (which already applied the change to itself) untouched.
/// </summary>
public sealed class WorkbookWindowRegistryFormulaBarTests
{
    [Fact]
    public void BroadcastFormulaBarVisibility_AppliesToEveryOtherWindow_AcrossDifferentDocuments()
    {
        var registry = new WorkbookWindowRegistry();
        var origin = new TestWorkbookWindow { DocumentId = new WorkbookId(Guid.NewGuid()) };
        var sameDocumentSibling = new TestWorkbookWindow { DocumentId = origin.DocumentId };
        var otherDocumentWindow = new TestWorkbookWindow { DocumentId = new WorkbookId(Guid.NewGuid()) };
        registry.Register(origin);
        registry.Register(sameDocumentSibling);
        registry.Register(otherDocumentWindow);

        registry.BroadcastFormulaBarVisibility(origin, visible: false);

        origin.FormulaBarVisibilityAppliedCount.Should().Be(0, "the originating window already applied the change to itself");
        sameDocumentSibling.FormulaBarVisibilityAppliedCount.Should().Be(1);
        sameDocumentSibling.LastAppliedFormulaBarVisibility.Should().BeFalse();
        otherDocumentWindow.FormulaBarVisibilityAppliedCount.Should().Be(1,
            "Show Formula Bar is Excel-instance-wide, not scoped to one document");
        otherDocumentWindow.LastAppliedFormulaBarVisibility.Should().BeFalse();
    }

    [Fact]
    public void BroadcastFormulaBarVisibility_SingleWindow_DoesNothing()
    {
        var registry = new WorkbookWindowRegistry();
        var origin = new TestWorkbookWindow();
        registry.Register(origin);

        registry.BroadcastFormulaBarVisibility(origin, visible: true);

        origin.FormulaBarVisibilityAppliedCount.Should().Be(0);
    }
}
