using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class FreeXStatusBarRendererPlannerTests
{
    private static readonly IStatusBarTextProvider TextProvider =
        new ResourceKeyStatusBarTextProvider(resourceKey => resourceKey switch
        {
            StatusBarTextResourceKeys.ReadyText => "Ready",
            StatusBarTextResourceKeys.CalculateText => "Calculate",
            StatusBarTextResourceKeys.AverageFormat => "Average: {0}",
            StatusBarTextResourceKeys.CountFormat => "Count: {0}",
            StatusBarTextResourceKeys.NumericalCountFormat => "Numerical Count: {0}",
            StatusBarTextResourceKeys.SumFormat => "Sum: {0}",
            StatusBarTextResourceKeys.MinimumFormat => "Min: {0}",
            StatusBarTextResourceKeys.MaximumFormat => "Max: {0}",
            _ => resourceKey,
        });

    [Fact]
    public void BuildModelAndRendererPlan_ProducesOneCrossRendererContract()
    {
        var model = FreeXStatusBarRendererPlanner.BuildModel(
            new WorkbookSelectionStats(60, 4, 3, 20, 10, 30),
            zoomPercent: 90,
            readyText: "Ready",
            viewMode: WorksheetViewMode.PageBreakPreview,
            textProvider: TextProvider);
        var visibility = StatusBarOptionVisibility.ExcelDefaults with
        {
            NumericalCount = true,
            Sum = false,
        };

        var plan = FreeXStatusBarRendererPlanner.BuildRendererPlan(model, visibility);

        plan.ZoomPercent.Should().Be(90);
        plan.ReadyTextVisible.Should().BeFalse();
        plan.VisibleReadoutText.Should().Be("Average: 20   Count: 4   Numerical Count: 3");
        plan.IsElementVisible(StatusBarPresentationElement.StatsPanel).Should().BeTrue();
        plan.IsElementVisible(StatusBarPresentationElement.Sum).Should().BeFalse();
    }

    [Fact]
    public void NormalizeReadyText_PreservesSharedCalculatePolicy()
    {
        FreeXStatusBarRendererPlanner.NormalizeReadyText(
                "Ready",
                TextProvider,
                isManualCalculationMode: true,
                hasPendingRecalculation: true)
            .Should().Be("Calculate");
    }

    [Fact]
    public void WpfAndAvaloniaHosts_ConsumeServicesOwnedRendererPlan()
    {
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.GridStatus.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.StatusBar.cs"));

        wpfSource.Should().Contain("FreeXStatusBarRendererPlanner.BuildRendererPlan(");
        avaloniaSource.Should().Contain("FreeXStatusBarRendererPlanner.BuildRendererPlan(model, _statusBarOptionVisibility)");
        wpfSource.Should().NotContain("StatusBarPresentationPlanner.BuildRendererPlan(");
        avaloniaSource.Should().NotContain("AvaloniaStatusBarSource");
    }
}
