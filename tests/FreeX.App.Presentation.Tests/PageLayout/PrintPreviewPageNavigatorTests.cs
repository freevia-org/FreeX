using FluentAssertions;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Unit tests for the pure page-enumeration / navigation math behind the print-preview window's
/// Prev/Next buttons and "Page X of N" caption. No running UI.
/// </summary>
public sealed class PrintPreviewPageNavigatorTests
{
    [Fact]
    public void Create_StartsAtFirstPage()
    {
        var nav = PrintPreviewPageNavigator.Create(3);

        nav.PageCount.Should().Be(3);
        nav.CurrentIndex.Should().Be(0);
        nav.CurrentPageNumber.Should().Be(1);
        nav.HasPages.Should().BeTrue();
        nav.Caption.Should().Be("Page 1 of 3");
    }

    [Fact]
    public void Create_NegativeCountClampsToZero()
    {
        var nav = PrintPreviewPageNavigator.Create(-5);

        nav.PageCount.Should().Be(0);
        nav.HasPages.Should().BeFalse();
        nav.CanGoPrevious.Should().BeFalse();
        nav.CanGoNext.Should().BeFalse();
        nav.CurrentPageNumber.Should().Be(1);
        nav.Caption.Should().Be("Page 1 of 1");
    }

    [Fact]
    public void Next_AdvancesUntilLastPageThenClamps()
    {
        var nav = PrintPreviewPageNavigator.Create(2);

        nav.CanGoPrevious.Should().BeFalse();
        nav.CanGoNext.Should().BeTrue();

        nav = nav.Next();
        nav.CurrentIndex.Should().Be(1);
        nav.CanGoNext.Should().BeFalse();
        nav.CanGoPrevious.Should().BeTrue();
        nav.Caption.Should().Be("Page 2 of 2");

        nav = nav.Next(); // clamped at last page
        nav.CurrentIndex.Should().Be(1);
    }

    [Fact]
    public void Previous_GoesBackUntilFirstPageThenClamps()
    {
        var nav = PrintPreviewPageNavigator.Create(3).Next().Next();
        nav.CurrentIndex.Should().Be(2);

        nav = nav.Previous();
        nav.CurrentIndex.Should().Be(1);

        nav = nav.Previous();
        nav.CurrentIndex.Should().Be(0);

        nav = nav.Previous(); // clamped at first page
        nav.CurrentIndex.Should().Be(0);
        nav.CanGoPrevious.Should().BeFalse();
    }

    [Fact]
    public void JumpTo_ClampsIntoRange()
    {
        var nav = PrintPreviewPageNavigator.Create(4);

        nav.JumpTo(2).CurrentIndex.Should().Be(2);
        nav.JumpTo(99).CurrentIndex.Should().Be(3);
        nav.JumpTo(-7).CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void JumpTo_OnEmptyStaysAtZero()
    {
        var nav = PrintPreviewPageNavigator.Create(0);

        nav.JumpTo(5).CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void Next_OnEmptyStaysAtZero()
    {
        var nav = PrintPreviewPageNavigator.Create(0);

        nav.Next().CurrentIndex.Should().Be(0);
        nav.Previous().CurrentIndex.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 3, 1, 3, false, false, true, true, "Page 1 of 3")]
    [InlineData(2, 3, 2, 3, true, true, true, true, "Page 2 of 3")]
    [InlineData(3, 3, 3, 3, true, true, false, false, "Page 3 of 3")]
    [InlineData(0, 0, 1, 1, false, false, false, false, "Page 1 of 1")]
    [InlineData(5, 3, 3, 3, true, true, false, false, "Page 3 of 3")]
    public void NavigationState_NormalizesPageStatusAndButtonStates(
        int currentPage,
        int totalPages,
        int expectedCurrentPage,
        int expectedTotalPages,
        bool canGoFirst,
        bool canGoPrevious,
        bool canGoNext,
        bool canGoLast,
        string statusText)
    {
        var state = PrintPreviewNavigationState.Create(currentPage, totalPages);

        state.CurrentPage.Should().Be(expectedCurrentPage);
        state.TotalPages.Should().Be(expectedTotalPages);
        state.CanGoFirst.Should().Be(canGoFirst);
        state.CanGoPrevious.Should().Be(canGoPrevious);
        state.CanGoNext.Should().Be(canGoNext);
        state.CanGoLast.Should().Be(canGoLast);
        state.StatusText.Should().Be(statusText);
    }
}
