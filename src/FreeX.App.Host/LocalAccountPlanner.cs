using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

// Host adapter: supplies localized labels, app-version text, and FreeXOptions persistence
// boundaries, while LocalAccountWorkflowPlanner owns the portable account/workbook status shape.
public static class LocalAccountPlanner
{
    public static LocalAccountPlan Create(
        FreeXOptions options,
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
        optionsPathProvider ??= () => FreeXOptions.StorePathForDisplay;

        return LocalAccountWorkflowPlanner.Create(
            new LocalAccountPlannerInput(
                UiText.Get("DeferredCommand_LocalAccount_Title"),
                "FreeX user name",
                "Local OS account",
                "Device",
                "App version",
                "Options file",
                "Current workbook",
                "Sharing",
                "Export",
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
    }

    public static string FormatMessageBody(LocalAccountPlan plan)
    {
        return LocalAccountWorkflowPlanner.FormatMessageBody(plan, UiText.Get("DeferredCommand_LocalAccount_Body"));
    }
}
