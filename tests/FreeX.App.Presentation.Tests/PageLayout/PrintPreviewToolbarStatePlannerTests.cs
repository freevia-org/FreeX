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

    [Fact]
    public void CreateSidesOptions_ProvidesSharedToolbarChoicesInIndexOrder()
    {
        var options = PrintPreviewToolbarStatePlanner.CreateSidesOptions();

        options.Select(option => option.Value).Should().Equal(
            PrintPreviewSidesMode.OneSided,
            PrintPreviewSidesMode.TwoSidedLongEdge,
            PrintPreviewSidesMode.TwoSidedShortEdge);
        options.Select(option => option.Text).Should().Equal(
            "Print One Sided",
            "Print on Both Sides - Flip pages on long edge",
            "Print on Both Sides - Flip pages on short edge");
    }

    [Fact]
    public void CreateCollationOptions_ProvidesSharedCollationChoices()
    {
        var options = PrintPreviewToolbarStatePlanner.CreateCollationOptions();

        options.Should().Equal(
            new PrintPreviewChoice<bool>("Collated", true),
            new PrintPreviewChoice<bool>("Uncollated", false));
    }

    [Fact]
    public void CreateToolbarCollatedText_StripsAccessKeyMarkers()
    {
        var resolver = new PrintSettingsTextResolver(
            key => key == "PrintPreview_CollatedLabel" ? "C_ollated" : key,
            (_, _) => "");

        PrintPreviewToolbarStatePlanner.CreateToolbarCollatedText(resolver).Should().Be("Collated");
    }

    [Fact]
    public void CreateZoomOptions_ProvidesSharedPreviewZoomChoices()
    {
        var options = PrintPreviewToolbarStatePlanner.CreateZoomOptions();

        options.Select(option => option.Text).Should().Equal("50%", "75%", "100%", "125%", "Page Width");
        options[PrintPreviewToolbarStatePlanner.DefaultZoomOptionIndex].Percent.Should().Be(100);
        options[^1].FitToWidth.Should().BeTrue();
    }

    [Fact]
    public void CreatePageRangeToolbarPlan_ProvidesLabelsAndNormalizedRangeText()
    {
        var plan = PrintPreviewToolbarStatePlanner.CreatePageRangeToolbarPlan(0);

        plan.Choices.Select(choice => choice.Mode).Should().Equal(
            PrintPreviewPageRangeMode.AllPages,
            PrintPreviewPageRangeMode.CurrentPage,
            PrintPreviewPageRangeMode.Pages);
        plan.Choices.Single(choice => choice.Mode == PrintPreviewPageRangeMode.AllPages).IsChecked.Should().BeTrue();
        plan.FromPageText.Should().Be("1");
        plan.ToPageText.Should().Be("1");
        plan.ToSeparatorText.Should().Be("to");
        plan.PageBoxesEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(PrintPreviewPageRangeMode.AllPages, 2, null, null, null, null)]
    [InlineData(PrintPreviewPageRangeMode.CurrentPage, 2, null, null, 2, 2)]
    [InlineData(PrintPreviewPageRangeMode.Pages, 1, 3, 5, 3, 5)]
    [InlineData(PrintPreviewPageRangeMode.Pages, 1, 3, null, null, null)]
    public void ResolvePageRange_MapsToolbarSelectionToOptionalOneBasedRange(
        PrintPreviewPageRangeMode mode,
        int currentPage,
        int? fromPage,
        int? toPage,
        int? expectedFrom,
        int? expectedTo)
    {
        var plan = PrintPreviewToolbarStatePlanner.ResolvePageRange(mode, currentPage, fromPage, toPage);

        if (expectedFrom is null || expectedTo is null)
        {
            plan.Should().BeNull();
            return;
        }

        plan.Should().Be(new PrintPreviewPageRangePlan(expectedFrom.Value, expectedTo.Value));
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
