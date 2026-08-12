using FluentAssertions;
using FreeX.App.Presentation.Backstage;
using FreeX.ParityCapture.Avalonia;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class BackstageInfoParityProjectionTests
{
    [Fact]
    public void Build_DerivesWpfPlanAndKeepsExactCapturedDetailSubset()
    {
        var request = new FreeXBackstageInfoPaneRequest(
            "Budget.xlsx",
            @"C:\Work\Budget.xlsx",
            "3",
            ".xlsx",
            "12 KB",
            "6/30/2026 10:00 AM",
            "Local only",
            "Ready",
            "Workbook protected",
            "Sheet unprotected",
            "3 sheets",
            "No issues",
            "No errors");

        var capturePlan = BackstageInfoParityProjection.Build(request);
        var wpfPlan = FreeXBackstageInfoPanePlanner.Build(FreeXBackstageInfoSurface.WpfInfoPane, request);

        capturePlan.Actions.Should().Equal(wpfPlan.Actions);
        capturePlan.Details.Select(detail => detail.Id).Should().Equal(
            FreeXBackstageInfoDetailId.WorkbookName,
            FreeXBackstageInfoDetailId.FilePath,
            FreeXBackstageInfoDetailId.SheetCount,
            FreeXBackstageInfoDetailId.Format,
            FreeXBackstageInfoDetailId.FileSize,
            FreeXBackstageInfoDetailId.LastModified,
            FreeXBackstageInfoDetailId.Share,
            FreeXBackstageInfoDetailId.Export,
            FreeXBackstageInfoDetailId.WorkbookProtection,
            FreeXBackstageInfoDetailId.ActiveSheetProtection);
        capturePlan.Details.Should().Equal(wpfPlan.Details.Take(10));
    }

    [Fact]
    public void ProductionSurfaceEnum_HasNoCaptureOnlyMember()
    {
        Enum.GetNames<FreeXBackstageInfoSurface>()
            .Should().Equal("WpfInfoPane", "AvaloniaInfoDialog", "AvaloniaLivePane");
    }
}
