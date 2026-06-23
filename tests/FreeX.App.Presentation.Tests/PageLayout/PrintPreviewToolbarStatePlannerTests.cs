using FluentAssertions;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewToolbarStatePlannerTests
{
    [Theory]
    [InlineData(0, PrintPreviewSidesMode.OneSided)]
    [InlineData(1, PrintPreviewSidesMode.TwoSidedLongEdge)]
    [InlineData(2, PrintPreviewSidesMode.TwoSidedShortEdge)]
    [InlineData(99, PrintPreviewSidesMode.OneSided)]
    public void SidesIndexToMode_MapsToolbarSelection(int index, PrintPreviewSidesMode expected)
    {
        PrintPreviewToolbarStatePlanner.SidesIndexToMode(index).Should().Be(expected);
    }

    [Theory]
    [InlineData(PrintPreviewSidesMode.OneSided, 0)]
    [InlineData(PrintPreviewSidesMode.TwoSidedLongEdge, 1)]
    [InlineData(PrintPreviewSidesMode.TwoSidedShortEdge, 2)]
    public void SidesModeToIndex_MapsToolbarSelection(PrintPreviewSidesMode mode, int expected)
    {
        PrintPreviewToolbarStatePlanner.SidesModeToIndex(mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 1, 1, "Ready: Windows print dialog; 1 copy; 1 page")]
    [InlineData("", 2, 3, "Ready: Windows print dialog; 2 copies; 3 pages")]
    [InlineData("Office Printer", null, 4, "Ready: Office Printer; invalid copies; 4 pages")]
    public void CreateStatusText_UsesSharedPrintPreviewSummary(
        string? printerName,
        int? copies,
        int totalPages,
        string expected)
    {
        PrintPreviewToolbarStatePlanner.CreateStatusText(printerName, copies, totalPages).Should().Be(expected);
    }

    [Fact]
    public void CreateNavigationState_DelegatesToSharedNavigationState()
    {
        var state = PrintPreviewToolbarStatePlanner.CreateNavigationState(2, 3);

        state.CurrentPage.Should().Be(2);
        state.TotalPages.Should().Be(3);
        state.StatusText.Should().Be("Page 2 of 3");
        state.CanGoFirst.Should().BeTrue();
        state.CanGoLast.Should().BeTrue();
    }
}
