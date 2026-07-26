using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R90-app-window-arrange-freeze-ui-5-4: Excel's Arrange Windows dialog has
/// a "Windows of active workbook" checkbox that restricts Arrange All to just the windows of the
/// currently active document, leaving every other open document's windows untouched. Before this,
/// <see cref="WorkbookWindowRegistry.ArrangeVisibleWindows"/> had no way to scope by document at
/// all -- it always tiled every visible window across every open workbook.
/// </summary>
public sealed class R90_ArrangeVisibleWindowsDocumentRestrictionTests
{
    private static (WorkbookWindowRegistry Registry, TestWorkbookWindow[] BookOneWindows, TestWorkbookWindow[] BookTwoWindows) CreateTwoWorkbooksEachWithTwoWindows()
    {
        var registry = new WorkbookWindowRegistry();
        var bookOneId = new WorkbookId(Guid.NewGuid());
        var bookTwoId = new WorkbookId(Guid.NewGuid());

        var bookOneWindows = new[]
        {
            new TestWorkbookWindow { DocumentId = bookOneId },
            new TestWorkbookWindow { DocumentId = bookOneId }
        };
        var bookTwoWindows = new[]
        {
            new TestWorkbookWindow { DocumentId = bookTwoId },
            new TestWorkbookWindow { DocumentId = bookTwoId }
        };

        // Register sequentially per document, so Book2's pair are adjacent in the switch-cycle
        // order EnableSideBySide's NextVisibleWindow walks (it pairs the invoking window with the
        // NEXT visible window in registration order) -- document-scoped arranging, not
        // registration-order coincidence, is what this fixture is meant to isolate.
        registry.Register(bookOneWindows[0]);
        registry.Register(bookOneWindows[1]);
        registry.Register(bookTwoWindows[0]);
        registry.Register(bookTwoWindows[1]);

        return (registry, bookOneWindows, bookTwoWindows);
    }

    [Fact]
    public void ArrangeVisibleWindows_RestrictedToDocument_OnlyTilesThatDocumentsWindows()
    {
        var (registry, bookOneWindows, bookTwoWindows) = CreateTwoWorkbooksEachWithTwoWindows();

        registry.ArrangeVisibleWindows(
            WorkbookWindowArrangement.Vertical,
            900,
            600,
            restrictToDocumentId: bookOneWindows[0].DocumentId).Should().BeTrue();

        bookOneWindows[0].ArrangedBounds.Should().ContainSingle();
        bookOneWindows[1].ArrangedBounds.Should().ContainSingle();
        bookTwoWindows[0].ArrangedBounds.Should().BeEmpty("Book2's windows must be left exactly where they are");
        bookTwoWindows[1].ArrangedBounds.Should().BeEmpty();
    }

    [Fact]
    public void ArrangeVisibleWindows_RestrictedToDocument_DoesNotBreakAnUnrelatedSideBySidePair()
    {
        var (registry, bookOneWindows, bookTwoWindows) = CreateTwoWorkbooksEachWithTwoWindows();
        registry.EnableSideBySide(bookTwoWindows[0], 900, 600).Should().BeTrue();

        registry.ArrangeVisibleWindows(
            WorkbookWindowArrangement.Vertical,
            900,
            600,
            restrictToDocumentId: bookOneWindows[0].DocumentId).Should().BeTrue();

        registry.IsSideBySideActive.Should().BeTrue(
            "Arranging Book1's windows only must not un-pair Book2's unrelated side-by-side comparison");
    }

    /// <summary>No-regression sibling: the default (unrestricted) call keeps arranging every open document's windows.</summary>
    [Fact]
    public void ArrangeVisibleWindows_WithoutRestriction_StillTilesEveryDocumentsWindows()
    {
        var (registry, bookOneWindows, bookTwoWindows) = CreateTwoWorkbooksEachWithTwoWindows();

        registry.ArrangeVisibleWindows(WorkbookWindowArrangement.Vertical, 800, 600).Should().BeTrue();

        bookOneWindows[0].ArrangedBounds.Should().ContainSingle();
        bookOneWindows[1].ArrangedBounds.Should().ContainSingle();
        bookTwoWindows[0].ArrangedBounds.Should().ContainSingle();
        bookTwoWindows[1].ArrangedBounds.Should().ContainSingle();
    }
}
