using System.Reflection;
using FreeX.App.Presentation.Backstage;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Framework-neutral, NON-localized snapshot of the local app/account info shown by the backstage
/// Account pane. FreeX is an offline desktop app: there is no cloud account, so this is local-only
/// (product, version, device/user, license + privacy notices, options availability). The rendering
/// shell localizes labels and lays the values out; macOS inherits this shaping unchanged.
/// </summary>
public sealed record LocalAccountInfoPlan(
    string ProductName,
    string VersionText,
    string DeviceName,
    string UserName,
    bool OptionsAvailable,
    string TrademarkNotice,
    string LicenseNotice,
    string PrivacyNotice,
    string HelpUrl,
    string FeedbackUrl,
    string? LocalOsAccount = null,
    string? OptionsFile = null,
    string? WorkbookStatus = null,
    string? SharingStatus = null,
    string? ExportStatus = null);

public sealed record LocalAccountInfoRequest(
    Assembly AppAssembly,
    string? DeviceName = null,
    string? UserName = null,
    bool OptionsAvailable = true,
    string? LocalOsUserName = null,
    string? LocalOsUserDomain = null,
    string? OptionsFile = null,
    string? CurrentWorkbookPath = null,
    string? CurrentWorkbookName = null,
    Workbook? Workbook = null,
    bool HasSelection = false,
    Func<string, bool>? FileExists = null,
    string DeviceFallback = "Unknown device",
    string OptionsFileFallback = "Unknown",
    string WorkbookNameFallback = "Unsaved workbook");

/// <summary>
/// Builds a <see cref="LocalAccountInfoPlan"/> from the running assembly plus host-supplied device/user
/// identity. Pure data shaping — no UI, no platform calls — so the Avalonia shell only renders/localizes.
/// </summary>
public static class LocalAccountInfoPlanner
{
    public static LocalAccountInfoPlan Build(
        Assembly appAssembly,
        string? deviceName = null,
        string? userName = null,
        bool optionsAvailable = true) =>
        Build(new LocalAccountInfoRequest(
            appAssembly,
            deviceName,
            userName,
            optionsAvailable,
            DeviceFallback: string.Empty));

    public static LocalAccountInfoPlan Build(LocalAccountInfoRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AppAssembly);

        var localOsUserName = NormalizeOrFallback(request.LocalOsUserName, string.Empty);
        var userName = NormalizeOrFallback(request.UserName, localOsUserName);
        localOsUserName = NormalizeOrFallback(request.LocalOsUserName, userName);

        var hasLocalOsAccount = request.LocalOsUserName is not null || request.LocalOsUserDomain is not null;
        var hasWorkbookContext = request.CurrentWorkbookPath is not null ||
            request.CurrentWorkbookName is not null ||
            request.Workbook is not null;
        var fileExists = request.FileExists ?? File.Exists;
        var workbookStatus = hasWorkbookContext
            ? FormatWorkbookStatus(
                NormalizeOrFallback(request.CurrentWorkbookName, request.WorkbookNameFallback),
                request.CurrentWorkbookPath,
                fileExists)
            : null;
        var sharingStatus = hasWorkbookContext
            ? WorkbookShareReadinessPlanner.FormatStatus(WorkbookShareReadinessPlanner.CreatePlan(
                request.CurrentWorkbookPath,
                WorkbookShareSurface.WindowsShare,
                fileExists))
            : null;
        var exportStatus = hasWorkbookContext
            ? request.Workbook is null
                ? WorkbookExportReadinessPlanner.CreateForAvailableWorkbook(request.HasSelection).StatusText
                : WorkbookExportReadinessPlanner.Create(request.Workbook, request.HasSelection).StatusText
            : null;

        return new LocalAccountInfoPlan(
            ProductName: AppHelpInfo.ProductName,
            VersionText: AppHelpInfo.GetBuildVersionText(request.AppAssembly),
            DeviceName: NormalizeOrFallback(request.DeviceName, request.DeviceFallback),
            UserName: userName,
            OptionsAvailable: request.OptionsAvailable,
            TrademarkNotice: AppHelpInfo.TrademarkNotice,
            LicenseNotice: AppHelpInfo.ProjectLicenseNotice,
            PrivacyNotice: AppHelpInfo.PrivacyNotice,
            HelpUrl: AppHelpInfo.HelpUrl,
            FeedbackUrl: AppHelpInfo.FeedbackUrl,
            LocalOsAccount: hasLocalOsAccount
                ? FormatLocalOsAccount(request.LocalOsUserDomain, localOsUserName)
                : null,
            OptionsFile: request.OptionsFile is null
                ? null
                : NormalizeOrFallback(request.OptionsFile, request.OptionsFileFallback),
            WorkbookStatus: workbookStatus,
            SharingStatus: sharingStatus,
            ExportStatus: exportStatus);
    }

    public static FreeXBackstageAccountPaneRequest CreateBackstageAccountPaneRequest(
        LocalAccountInfoPlan plan,
        string? currentWorkbookPath,
        string? currentWorkbookName)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new FreeXBackstageAccountPaneRequest(
            plan.UserName,
            plan.DeviceName,
            plan.VersionText,
            plan.OptionsAvailable,
            plan.WorkbookStatus is null ? currentWorkbookPath : null,
            plan.WorkbookStatus ?? currentWorkbookName,
            plan.TrademarkNotice,
            plan.LicenseNotice,
            plan.PrivacyNotice,
            plan.LocalOsAccount,
            plan.OptionsFile,
            plan.SharingStatus,
            plan.ExportStatus);
    }

    private static string FormatLocalOsAccount(string? domain, string userName)
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
            _ => $"{workbookDisplayName} (not saved yet)"
        };
    }

    private static string NormalizeOrFallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
