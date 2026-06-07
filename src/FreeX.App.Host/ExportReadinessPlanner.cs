using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ExportReadinessPlan(
    bool IsReady,
    string StatusText);

public static class ExportReadinessPlanner
{
    public static ExportReadinessPlan Create(Workbook workbook, bool hasSelection = false) =>
        ToHostPlan(WorkbookExportReadinessPlanner.Create(workbook, hasSelection));

    public static ExportReadinessPlan CreateForAvailableWorkbook(bool hasSelection = false) =>
        ToHostPlan(WorkbookExportReadinessPlanner.CreateForAvailableWorkbook(hasSelection));

    private static ExportReadinessPlan ToHostPlan(WorkbookExportReadinessPlan plan) =>
        new(plan.IsReady, plan.StatusText);
}
