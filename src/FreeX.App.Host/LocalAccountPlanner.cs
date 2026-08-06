using FreeX.App.Services;
using FreeX.App.Presentation.Backstage;
using FreeX.Core.Model;

namespace FreeX.App.Host;

// Host adapter: supplies localized labels, app-version text, and AppOptions persistence
// boundaries, while LocalAccountWorkflowPlanner owns the portable account/workbook status shape.
public static class LocalAccountPlanner
{
    public static LocalAccountPlan Create(
        AppOptions options,
        string? currentFilePath,
        string? workbookName,
        Func<string>? userNameProvider = null,
        Func<string>? userDomainProvider = null,
        Func<string>? machineNameProvider = null,
        Func<string>? optionsPathProvider = null,
        Func<string, bool>? fileExists = null,
        Workbook? workbook = null,
        bool hasSelection = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        userNameProvider ??= () => Environment.UserName;
        userDomainProvider ??= () => Environment.UserDomainName;
        machineNameProvider ??= () => Environment.MachineName;
        optionsPathProvider ??= () => AppOptionsStore.StorePath;

        var workflowPlan = LocalAccountWorkflowPlanner.Create(
            new LocalAccountPlannerInput(
                UiText.Get("DeferredCommand_LocalAccount_Title"),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                options.UserName,
                userNameProvider(),
                userDomainProvider(),
                machineNameProvider(),
                AppInfo.ExactVersionText,
                optionsPathProvider(),
                workbookName ?? "",
                currentFilePath),
            fileExists,
            workbook,
            hasSelection);

        return ProjectBackstageAccountPlan(workflowPlan);
    }

    public static string FormatMessageBody(LocalAccountPlan plan)
    {
        return LocalAccountWorkflowPlanner.FormatMessageBody(plan, UiText.Get("DeferredCommand_LocalAccount_Body"));
    }

    private static LocalAccountPlan ProjectBackstageAccountPlan(LocalAccountPlan workflowPlan)
    {
        var pane = FreeXBackstageAccountPanePlanner.Build(new FreeXBackstageAccountPaneRequest(
            workflowPlan.UserName,
            workflowPlan.DeviceName,
            workflowPlan.AppVersionText,
            OptionsAvailable: true,
            CurrentWorkbookPath: null,
            CurrentWorkbookName: workflowPlan.WorkbookStatus,
            TrademarkNotice: AppHelpInfo.TrademarkNotice,
            LicenseNotice: AppHelpInfo.ProjectLicenseNotice,
            PrivacyNotice: AppHelpInfo.PrivacyNotice,
            LocalOsAccount: workflowPlan.LocalAccount,
            OptionsFile: workflowPlan.OptionsPath,
            SharingStatus: workflowPlan.SharingStatus,
            ExportStatus: workflowPlan.ExportStatus));

        var details = pane.Details
            .Select(detail => new LocalAccountDetail(
                UiText.Get(detail.LabelKey),
                ResolveBackstageTextValue(detail.Value)))
            .ToArray();

        return workflowPlan with
        {
            Title = UiText.Get(pane.TitleKey),
            Details = details,
        };
    }

    private static string ResolveBackstageTextValue(FreeXBackstageTextValue value) =>
        value.TextKey is { } key
            ? UiText.Get(key)
            : value.Text ?? string.Empty;
}
