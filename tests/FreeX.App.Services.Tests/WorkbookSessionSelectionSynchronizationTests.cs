using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionSelectionSynchronizationTests
{
    [Fact]
    public void SynchronizeSelectionState_UpdatesCanonicalSelectionWithoutScrollingViewport()
    {
        using var session = new WorkbookSessionFactory().CreateNew(
            viewportHeight: 120,
            viewportWidth: 160);
        session.SetViewportOrigin(10, 8).Should().BeTrue();

        var viewportBefore = session.Viewport;
        var topRowBefore = session.ActiveSheet.ViewTopRow;
        var leftColumnBefore = session.ActiveSheet.ViewLeftCol;
        var activeCell = new CellAddress(session.ActiveSheet.Id, 40, 12);
        var range = new GridRange(activeCell, activeCell);

        session.SynchronizeSelectionState(session.ActiveSheet.Id, range, [range], activeCell);

        session.ActiveCell.Should().Be(activeCell);
        session.SelectedRange.Should().Be(range);
        session.Viewport.Should().BeSameAs(viewportBefore);
        session.ActiveSheet.ViewTopRow.Should().Be(topRowBefore);
        session.ActiveSheet.ViewLeftCol.Should().Be(leftColumnBefore);
    }
}
