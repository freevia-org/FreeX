using Free.Shared.Ribbon;

namespace FreeX.App.Presentation.Backstage;

public sealed record FreeXBackstageInfoPaneRequest(
    string WorkbookName,
    string FilePath,
    string SheetCount,
    string Format,
    string FileSize,
    string LastModified,
    string SharingStatus,
    string ExportStatus,
    string WorkbookProtectionSummary,
    string ActiveSheetProtectionSummary,
    string StatisticsSummary,
    string AccessibilitySummary,
    string FormulaErrorSummary,
    string? UnsavedChangesNote = null)
{
    public static FreeXBackstageInfoPaneRequest Empty { get; } =
        new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
}

public sealed record FreeXBackstageInfoActionPlan(
    FreeXBackstageInfoActionId Id,
    string LabelKey,
    string AutomationId,
    RibbonCommandIconKind Icon,
    FreeXBackstageTextValue? Detail,
    string? KeyTip = null,
    string? AutomationHelpTextKey = null,
    string? TooltipTitleKey = null,
    string? TooltipDescriptionKey = null,
    bool UsesDynamicLabel = false);

public sealed record FreeXBackstageInfoDetailPlan(
    FreeXBackstageInfoDetailId Id,
    string LabelKey,
    FreeXBackstageTextValue Value,
    string ValueAutomationId);

public sealed record FreeXBackstageInfoPanePlan(
    string TitleKey,
    string ActionsHeadingKey,
    string PropertiesHeadingKey,
    string FileSectionHeaderKey,
    string ProtectionSectionHeaderKey,
    string StatisticsSectionHeaderKey,
    IReadOnlyList<FreeXBackstageInfoActionPlan> Actions,
    IReadOnlyList<FreeXBackstageInfoDetailPlan> Details,
    FreeXBackstageTextValue? UnsavedChangesNote,
    FreeXBackstageTextValue WorkbookProtectionSummary,
    FreeXBackstageTextValue ActiveSheetProtectionSummary,
    FreeXBackstageTextValue StatisticsSummary);

/// <summary>
/// Builds the renderer-neutral FreeX Backstage Info pane. Services compute workbook facts; this planner owns
/// the FreeX pane sections, action descriptors, detail ordering, and row-value mapping for each surface.
/// </summary>
public static class FreeXBackstageInfoPanePlanner
{
    public static FreeXBackstageInfoPanePlan Build(
        FreeXBackstageInfoSurface surface,
        FreeXBackstageInfoPaneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var protectionSectionHeaderKey = surface == FreeXBackstageInfoSurface.AvaloniaLivePane
            ? "Backstage_LiveInfo_ProtectionSectionHeader"
            : "Backstage_Info_ProtectionSectionHeader";
        var statisticsSectionHeaderKey = surface == FreeXBackstageInfoSurface.AvaloniaLivePane
            ? "Backstage_LiveInfo_StatisticsSectionHeader"
            : "Backstage_Info_StatisticsSectionHeader";

        return new FreeXBackstageInfoPanePlan(
            "MainWindow_Text_Info",
            "MainWindow_Text_WorkbookActions",
            "MainWindow_Text_Properties",
            "Backstage_Info_FileSectionHeader",
            protectionSectionHeaderKey,
            statisticsSectionHeaderKey,
            BuildActions(surface),
            BuildDetails(surface, request),
            request.UnsavedChangesNote is null
                ? null
                : FreeXBackstageTextValue.Literal(request.UnsavedChangesNote),
            FreeXBackstageTextValue.Literal(request.WorkbookProtectionSummary),
            FreeXBackstageTextValue.Literal(request.ActiveSheetProtectionSummary),
            FreeXBackstageTextValue.Literal(request.StatisticsSummary));
    }

    private static IReadOnlyList<FreeXBackstageInfoActionPlan> BuildActions(
        FreeXBackstageInfoSurface surface)
    {
        var definitions = FreeXBackstagePaneCatalog.BuildInfoActions(surface);
        var actions = new List<FreeXBackstageInfoActionPlan>(definitions.Count);
        foreach (var action in definitions)
        {
            actions.Add(new FreeXBackstageInfoActionPlan(
                action.Id,
                action.LabelKey,
                action.AutomationId,
                action.Icon,
                action.DetailKey is null ? null : FreeXBackstageTextValue.Key(action.DetailKey),
                action.KeyTip,
                action.AutomationHelpTextKey,
                action.TooltipTitleKey,
                action.TooltipDescriptionKey,
                action.UsesDynamicLabel));
        }

        return actions;
    }

    private static IReadOnlyList<FreeXBackstageInfoDetailPlan> BuildDetails(
        FreeXBackstageInfoSurface surface,
        FreeXBackstageInfoPaneRequest request)
    {
        var definitions = FreeXBackstagePaneCatalog.BuildInfoDetails(surface);
        var details = new List<FreeXBackstageInfoDetailPlan>(definitions.Count);
        foreach (var detail in definitions)
        {
            details.Add(new FreeXBackstageInfoDetailPlan(
                detail.Id,
                detail.LabelKey,
                ResolveDetailValue(detail.Id, request),
                detail.ValueAutomationId));
        }

        return details;
    }

    private static FreeXBackstageTextValue ResolveDetailValue(
        FreeXBackstageInfoDetailId id,
        FreeXBackstageInfoPaneRequest request) =>
        id switch
        {
            FreeXBackstageInfoDetailId.WorkbookName => FreeXBackstageTextValue.Literal(request.WorkbookName),
            FreeXBackstageInfoDetailId.FilePath => FreeXBackstageTextValue.Literal(request.FilePath),
            FreeXBackstageInfoDetailId.SheetCount => FreeXBackstageTextValue.Literal(request.SheetCount),
            FreeXBackstageInfoDetailId.Format => FreeXBackstageTextValue.Literal(request.Format),
            FreeXBackstageInfoDetailId.FileSize => FreeXBackstageTextValue.Literal(request.FileSize),
            FreeXBackstageInfoDetailId.LastModified => FreeXBackstageTextValue.Literal(request.LastModified),
            FreeXBackstageInfoDetailId.Share => FreeXBackstageTextValue.Literal(request.SharingStatus),
            FreeXBackstageInfoDetailId.Export => FreeXBackstageTextValue.Literal(request.ExportStatus),
            FreeXBackstageInfoDetailId.WorkbookProtection => FreeXBackstageTextValue.Literal(request.WorkbookProtectionSummary),
            FreeXBackstageInfoDetailId.ActiveSheetProtection => FreeXBackstageTextValue.Literal(request.ActiveSheetProtectionSummary),
            FreeXBackstageInfoDetailId.WorkbookStatistics => FreeXBackstageTextValue.Literal(request.StatisticsSummary),
            FreeXBackstageInfoDetailId.Accessibility => FreeXBackstageTextValue.Literal(request.AccessibilitySummary),
            FreeXBackstageInfoDetailId.FormulaErrors => FreeXBackstageTextValue.Literal(request.FormulaErrorSummary),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
        };
}
