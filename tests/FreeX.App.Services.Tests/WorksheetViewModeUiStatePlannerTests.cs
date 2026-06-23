using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorksheetViewModeUiStatePlannerTests
{
    [Theory]
    [InlineData(WorksheetViewMode.Normal, StatusBarViewMode.Normal, true, false, false, false)]
    [InlineData(WorksheetViewMode.PageLayout, StatusBarViewMode.PageLayout, false, true, false, true)]
    [InlineData(WorksheetViewMode.PageBreakPreview, StatusBarViewMode.PageBreak, false, false, true, true)]
    public void Build_MapsWorksheetModeToStatusModeToggleStateAndOverlay(
        WorksheetViewMode worksheetViewMode,
        StatusBarViewMode statusBarViewMode,
        bool normalChecked,
        bool pageLayoutChecked,
        bool pageBreakPreviewChecked,
        bool usesPageBreakPreviewOverlay)
    {
        var state = WorksheetViewModeUiStatePlanner.Build(worksheetViewMode);

        Assert.Equal(worksheetViewMode, state.ViewMode);
        Assert.Equal(statusBarViewMode, state.StatusBarViewMode);
        Assert.Equal(normalChecked, state.NormalChecked);
        Assert.Equal(pageLayoutChecked, state.PageLayoutChecked);
        Assert.Equal(pageBreakPreviewChecked, state.PageBreakPreviewChecked);
        Assert.Equal(usesPageBreakPreviewOverlay, state.UsesPageBreakPreviewOverlay);
    }

    [Theory]
    [InlineData(WorksheetViewMode.Normal, StatusBarViewMode.Normal)]
    [InlineData(WorksheetViewMode.PageLayout, StatusBarViewMode.PageLayout)]
    [InlineData(WorksheetViewMode.PageBreakPreview, StatusBarViewMode.PageBreak)]
    public void ToStatusBarViewMode_MapsDomainViewModeToNeutralStatusMode(
        WorksheetViewMode worksheetViewMode,
        StatusBarViewMode expectedStatusMode) =>
        Assert.Equal(expectedStatusMode, WorksheetViewModeUiStatePlanner.ToStatusBarViewMode(worksheetViewMode));
}
