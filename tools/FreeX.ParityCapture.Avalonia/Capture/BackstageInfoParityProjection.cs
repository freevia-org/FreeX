using FreeX.App.Presentation.Backstage;

namespace FreeX.ParityCapture.Avalonia;

internal static class BackstageInfoParityProjection
{
    private static readonly FreeXBackstageInfoDetailId[] CapturedDetailIds =
    [
        FreeXBackstageInfoDetailId.WorkbookName,
        FreeXBackstageInfoDetailId.FilePath,
        FreeXBackstageInfoDetailId.SheetCount,
        FreeXBackstageInfoDetailId.Format,
        FreeXBackstageInfoDetailId.FileSize,
        FreeXBackstageInfoDetailId.LastModified,
        FreeXBackstageInfoDetailId.Share,
        FreeXBackstageInfoDetailId.Export,
        FreeXBackstageInfoDetailId.WorkbookProtection,
        FreeXBackstageInfoDetailId.ActiveSheetProtection,
    ];

    internal static FreeXBackstageInfoPanePlan Build(FreeXBackstageInfoPaneRequest request)
    {
        var wpfPlan = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.WpfInfoPane,
            request);
        var capturedDetails = wpfPlan.Details
            .Where(detail => CapturedDetailIds.Contains(detail.Id))
            .ToArray();

        return wpfPlan with { Details = capturedDetails };
    }
}
