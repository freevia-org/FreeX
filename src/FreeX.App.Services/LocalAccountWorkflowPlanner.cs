using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record LocalAccountDetail(string Label, string Value);

public sealed record LocalAccountPlan(
    string Title,
    IReadOnlyList<LocalAccountDetail> Details,
    string WorkbookStatus,
    string SharingStatus,
    string ExportStatus)
{
    public string UserName { get; init; } = string.Empty;
    public string LocalAccount { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string AppVersionText { get; init; } = string.Empty;
    public string OptionsPath { get; init; } = string.Empty;
}

public sealed record LocalAccountPlannerInput(
    string Title,
    string UserNameLabel,
    string LocalAccountLabel,
    string DeviceLabel,
    string AppVersionLabel,
    string OptionsFileLabel,
    string CurrentWorkbookLabel,
    string SharingLabel,
    string ExportLabel,
    string UserName,
    string WindowsUserName,
    string UserDomain,
    string MachineName,
    string AppVersionText,
    string OptionsPath,
    string WorkbookDisplayName,
    string? CurrentFilePath,
    string UnknownDeviceText = "Unknown device",
    string UnknownOptionsPathText = "Unknown",
    string UnsavedWorkbookText = "Unsaved workbook");

public static class LocalAccountWorkflowPlanner
{
    public static LocalAccountPlan Create(
        LocalAccountPlannerInput input,
        Func<string, bool>? fileExists = null,
        Workbook? workbook = null,
        bool hasSelection = false)
    {
        ArgumentNullException.ThrowIfNull(input);

        fileExists ??= File.Exists;

        var userName = Normalize(input.UserName, input.WindowsUserName);
        var windowsUserName = Normalize(input.WindowsUserName, userName);
        var windowsAccount = FormatWindowsAccount(input.UserDomain, windowsUserName);
        var machineName = Normalize(input.MachineName, input.UnknownDeviceText);
        var optionsPath = Normalize(input.OptionsPath, input.UnknownOptionsPathText);
        var workbookDisplayName = Normalize(input.WorkbookDisplayName, input.UnsavedWorkbookText);
        var workbookStatus = FormatWorkbookStatus(workbookDisplayName, input.CurrentFilePath, fileExists);
        var sharingStatus = WorkbookShareReadinessPlanner.FormatStatus(WorkbookShareReadinessPlanner.CreatePlan(
            input.CurrentFilePath,
            WorkbookShareSurface.WindowsShare,
            fileExists));
        var exportStatus = workbook is null
            ? WorkbookExportReadinessPlanner.CreateForAvailableWorkbook(hasSelection).StatusText
            : WorkbookExportReadinessPlanner.Create(workbook, hasSelection).StatusText;

        var details = new List<LocalAccountDetail>
        {
            new(input.UserNameLabel, userName),
            new(input.LocalAccountLabel, windowsAccount),
            new(input.DeviceLabel, machineName),
            new(input.AppVersionLabel, input.AppVersionText),
            new(input.OptionsFileLabel, optionsPath),
            new(input.CurrentWorkbookLabel, workbookStatus),
            new(input.SharingLabel, sharingStatus),
            new(input.ExportLabel, exportStatus),
        };

        return new LocalAccountPlan(
            input.Title,
            details,
            workbookStatus,
            sharingStatus,
            exportStatus)
        {
            UserName = userName,
            LocalAccount = windowsAccount,
            DeviceName = machineName,
            AppVersionText = input.AppVersionText,
            OptionsPath = optionsPath,
        };
    }

    public static string FormatMessageBody(LocalAccountPlan plan, string bodyText)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var lines = plan.Details.Select(detail => $"{detail.Label}: {detail.Value}");
        return bodyText +
               Environment.NewLine +
               Environment.NewLine +
               string.Join(Environment.NewLine, lines);
    }

    private static string FormatWindowsAccount(string? domain, string userName)
    {
        if (string.IsNullOrWhiteSpace(domain) ||
            userName.Contains('\\', StringComparison.Ordinal))
        {
            return userName;
        }

        return $"{domain.Trim()}\\{userName}";
    }

    private static string FormatWorkbookStatus(
        string workbookDisplayName,
        string? currentFilePath,
        Func<string, bool> fileExists)
    {
        var sharePlan = WorkbookShareReadinessPlanner.CreatePlan(
            currentFilePath,
            WorkbookShareSurface.WindowsShare,
            fileExists);
        if (sharePlan.Kind == WorkbookShareReadinessPlanKind.ShareExistingFile)
            return $"{workbookDisplayName} ({sharePlan.Path})";

        return sharePlan.SaveAsReason switch
        {
            WorkbookShareReadinessSaveAsReason.MissingFile when !string.IsNullOrWhiteSpace(sharePlan.CandidatePath) =>
                $"{workbookDisplayName} (saved path missing: {sharePlan.CandidatePath})",
            WorkbookShareReadinessSaveAsReason.InvalidPath when !string.IsNullOrWhiteSpace(sharePlan.CandidatePath) =>
                $"{workbookDisplayName} (saved path is not a valid local file path: {sharePlan.CandidatePath})",
            _ =>
                $"{workbookDisplayName} (not saved yet)"
        };
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
}
