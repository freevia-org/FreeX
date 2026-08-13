using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Document-scoped registry semantics (H39): several windows may view the same document
/// (Excel "New Window"), while windows over different documents coexist independently.
/// Content refreshes, dirty-state broadcasts, and Excel-style title numbering must stay
/// within one document's windows and never leak into another document's.
/// </summary>
public sealed class WorkbookWindowRegistryDocumentScopeTests
{
    private static WorkbookId NewDocumentId() => new(Guid.NewGuid());

    [Fact]
    public void NotifyWorkbookChanged_RefreshesOnlyOtherWindowsOfTheSameDocument()
    {
        var registry = new WorkbookWindowRegistry();
        var documentA = NewDocumentId();
        var documentB = NewDocumentId();
        var origin = new TestWorkbookWindow { DocumentId = documentA };
        var siblingView = new TestWorkbookWindow { DocumentId = documentA };
        var otherDocument = new TestWorkbookWindow { DocumentId = documentB };
        registry.Register(origin);
        registry.Register(siblingView);
        registry.Register(otherDocument);

        registry.NotifyWorkbookChanged(origin);

        origin.RefreshCount.Should().Be(0, "the originating window already reflects its own change");
        siblingView.RefreshCount.Should().Be(1, "a 'New Window' sibling views the same document");
        otherDocument.RefreshCount.Should().Be(0, "a window over another document must never be rebound (H39)");
    }

    [Fact]
    public void NotifyDocumentStateChanged_RefreshesTitleBarsOfTheSameDocumentOnly()
    {
        var registry = new WorkbookWindowRegistry();
        var documentA = NewDocumentId();
        var origin = new TestWorkbookWindow { DocumentId = documentA };
        var siblingView = new TestWorkbookWindow { DocumentId = documentA };
        var otherDocument = new TestWorkbookWindow { DocumentId = NewDocumentId() };
        registry.Register(origin);
        registry.Register(siblingView);
        registry.Register(otherDocument);

        registry.NotifyDocumentStateChanged(origin);

        siblingView.RefreshTitleBarCount.Should().Be(1, "the sibling shares the document's dirty state");
        otherDocument.RefreshTitleBarCount.Should().Be(0, "another document's dirty state is unrelated");
    }

    [Fact]
    public void Register_WindowsOverDifferentDocuments_AreNotNumberedTogether()
    {
        var registry = new WorkbookWindowRegistry();
        var documentA = NewDocumentId();
        var viewA1 = new TestWorkbookWindow { DocumentId = documentA };
        var viewA2 = new TestWorkbookWindow { DocumentId = documentA };
        var loneB = new TestWorkbookWindow { DocumentId = NewDocumentId() };
        registry.Register(viewA1);
        registry.Register(viewA2);
        registry.Register(loneB);

        viewA1.Suffix.Should().Be(":1");
        viewA2.Suffix.Should().Be(":2");
        loneB.Suffix.Should().BeEmpty("a document's lone window carries no number, like Excel");
    }

    [Fact]
    public void Unregister_LastSiblingOfADocument_DropsTheSurvivorsNumberWithoutTouchingOtherDocuments()
    {
        var registry = new WorkbookWindowRegistry();
        var documentA = NewDocumentId();
        var documentB = NewDocumentId();
        var viewA1 = new TestWorkbookWindow { DocumentId = documentA };
        var viewA2 = new TestWorkbookWindow { DocumentId = documentA };
        var viewB1 = new TestWorkbookWindow { DocumentId = documentB };
        var viewB2 = new TestWorkbookWindow { DocumentId = documentB };
        registry.Register(viewA1);
        registry.Register(viewA2);
        registry.Register(viewB1);
        registry.Register(viewB2);

        registry.Unregister(viewA2);

        viewA1.Suffix.Should().BeEmpty("its document is down to a single view");
        viewB1.Suffix.Should().Be(":1", "the other document still has two views");
        viewB2.Suffix.Should().Be(":2");
    }

    [Fact]
    public void RefreshWindowNumbering_AfterADocumentSwap_RenumbersBothGroups()
    {
        // File > Open in a shared view detaches it into its own document without a
        // register/unregister round-trip; RefreshWindowNumbering re-derives the suffixes.
        var registry = new WorkbookWindowRegistry();
        var documentA = NewDocumentId();
        var view1 = new TestWorkbookWindow { DocumentId = documentA };
        var view2 = new TestWorkbookWindow { DocumentId = documentA };
        registry.Register(view1);
        registry.Register(view2);
        view1.Suffix.Should().Be(":1");
        view2.Suffix.Should().Be(":2");

        view2.DocumentId = NewDocumentId();
        registry.RefreshWindowNumbering();

        view1.Suffix.Should().BeEmpty("its former sibling now hosts a different document");
        view2.Suffix.Should().BeEmpty("the freshly opened document has a single view");
    }

    [Fact]
    public void HasOtherWindowsForDocument_SeesOnlySameDocumentSiblings()
    {
        var registry = new WorkbookWindowRegistry();
        var documentA = NewDocumentId();
        var viewA1 = new TestWorkbookWindow { DocumentId = documentA };
        var viewA2 = new TestWorkbookWindow { DocumentId = documentA };
        var loneB = new TestWorkbookWindow { DocumentId = NewDocumentId() };
        registry.Register(viewA1);
        registry.Register(viewA2);
        registry.Register(loneB);

        registry.HasOtherWindowsForDocument(viewA1).Should().BeTrue();
        registry.HasOtherWindowsForDocument(viewA2).Should().BeTrue();
        registry.HasOtherWindowsForDocument(loneB)
            .Should().BeFalse("windows over other documents do not keep this document alive");
    }

    [Fact]
    public void HasWindowForDocument_TracksRegistrationAndUnregistration()
    {
        var registry = new WorkbookWindowRegistry();
        var documentA = NewDocumentId();
        var view = new TestWorkbookWindow { DocumentId = documentA };

        registry.HasWindowForDocument(documentA).Should().BeFalse();
        registry.Register(view);
        registry.HasWindowForDocument(documentA).Should().BeTrue();
        registry.Unregister(view);
        registry.HasWindowForDocument(documentA).Should().BeFalse();
    }
}
