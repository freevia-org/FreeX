using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageCapturePlannerTests
{
    [Fact]
    public void WpfCatalog_PreservesNativeRailCaptureOptions()
    {
        var plans = FreeXBackstageCapturePlanner.Build(FreeXBackstageCaptureHost.Wpf);

        plans.Select(plan => plan.SurfaceId).Should().Equal(
            "backstage.Info",
            "backstage.Export",
            "backstage.Account");
        plans.Should().OnlyContain(plan =>
            plan.Width == FreeXBackstageCapturePlanner.CaptureWidth &&
            plan.Height == FreeXBackstageCapturePlanner.CaptureHeight &&
            plan.PngFileName == plan.SurfaceId + ".png");
        plans.Single(plan => plan.Pane == FreeXBackstageCapturePane.Export)
            .WpfFocusEntryId.Should().Be("BackstageExportButton");
        plans.Single(plan => plan.Pane == FreeXBackstageCapturePane.Account)
            .UsesCaptureOnlyAccountPane.Should().BeTrue();
    }

    [Fact]
    public void AvaloniaCatalog_PreservesRendererCaptureOrder()
    {
        FreeXBackstageCapturePlanner.Build(FreeXBackstageCaptureHost.Avalonia)
            .Select(plan => plan.SurfaceId)
            .Should().Equal("backstage.Export", "backstage.Info", "backstage.Account");
    }
}
