using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Print and print preview shrink the body by the estimated height of the footnote region. That
/// estimate grows with the footnote text and nothing bounded it, so footnotes long enough relative
/// to the page reserved the entire content area and handed the WPF paginator a zero or negative page
/// box, which it rejects — print and print preview failed outright. PageLayout.ContentAreaDip clamps
/// the equivalent margins case, but the reserve is added afterwards and escaped it.
/// </summary>
public sealed class FootnoteReserveClampTests
{
    private static PageSettings LetterPortrait() => new();

    [Fact]
    public void ClampFootnoteReserveDip_ReserveLargerThanThePage_LeavesBodyHeight()
    {
        var page = LetterPortrait();
        var contentHeight = PageLayout.ContentAreaDip(page).Height;

        var clamped = PaginationEngine.ClampFootnoteReserveDip(contentHeight * 4, page);

        clamped.Should().BeLessThan(contentHeight);
        (contentHeight - clamped).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ClampFootnoteReserveDip_ReserveExactlyTheContentHeight_LeavesBodyHeight()
    {
        var page = LetterPortrait();
        var contentHeight = PageLayout.ContentAreaDip(page).Height;

        var clamped = PaginationEngine.ClampFootnoteReserveDip(contentHeight, page);

        (contentHeight - clamped).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ClampFootnoteReserveDip_OrdinaryReserve_IsUnchanged()
    {
        // A normal footnote must still reserve exactly what it asked for.
        var page = LetterPortrait();

        PaginationEngine.ClampFootnoteReserveDip(48, page).Should().Be(48);
    }

    [Fact]
    public void ClampFootnoteReserveDip_NoReserve_StaysZero()
    {
        PaginationEngine.ClampFootnoteReserveDip(0, LetterPortrait()).Should().Be(0);
    }
}
