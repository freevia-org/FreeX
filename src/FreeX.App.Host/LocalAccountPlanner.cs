using System.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record LocalAccountDetail(string Label, string Value);

public sealed record LocalAccountPlan(
    string Title,
    IReadOnlyList<LocalAccountDetail> Details,
    string WorkbookStatus,
    string SharingStatus,
    string ExportStatus);

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
        fileExists ??= File.Exists;

        var userName = Normalize(options.UserName, userNameProvider());
        var windowsUserName = Normalize(userNameProvider(), userName);
        var windowsAccount = FormatWindowsAccount(userDomainProvider(), windowsUserName);
        var machineName = Normalize(machineNameProvider(), "Unknown device");
        var optionsPath = Normalize(optionsPathProvider(), "Unknown");
        var workbookDisplayName = Normalize(workbookName, "Unsaved workbook");
        var workbookStatus = FormatWorkbookStatus(workbookDisplayName, currentFilePath, fileExists);
        var sharingStatus = FormatSharingStatus(ShareWorkbookPlanner.CreatePlan(currentFilePath, fileExists));
        var exportStatus = workbook is null
            ? ExportReadinessPlanner.CreateForAvailableWorkbook(hasSelection).StatusText
            : ExportReadinessPlanner.Create(workbook, hasSelection).StatusText;

        var details = new List<LocalAccountDetail>
        {
            new("FreeX user name", userName),
            new("Windows account", windowsAccount),
            new("Device", machineName),
            new("App version", AppInfo.VersionText),
            new("Options file", optionsPath),
            new("Current workbook", workbookStatus),
            new("Sharing", sharingStatus),
            new("Export", exportStatus),
            new("Microsoft 365 services", "Not connected; account sign-in, cloud links, and coauthoring are not implemented.")
        };

        return new LocalAccountPlan(
            UiText.Get("DeferredCommand_LocalAccount_Title"),
            details,
            workbookStatus,
            sharingStatus,
            exportStatus);
    }

    public static string FormatMessageBody(LocalAccountPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var lines = plan.Details.Select(detail => $"{detail.Label}: {detail.Value}");
        return UiText.Get("DeferredCommand_LocalAccount_Body") +
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
        if (string.IsNullOrWhiteSpace(currentFilePath))
            return $"{workbookDisplayName} (not saved yet)";

        var path = currentFilePath.Trim();
        return fileExists(path)
            ? $"{workbookDisplayName} ({path})"
            : $"{workbookDisplayName} (saved path missing: {path})";
    }

    private static string FormatSharingStatus(ShareWorkbookPlan plan) =>
        ShareWorkbookPlanner.FormatStatus(plan);

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
}
