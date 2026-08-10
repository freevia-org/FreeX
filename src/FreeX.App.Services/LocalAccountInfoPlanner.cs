using System.Reflection;
using FreeX.App.Presentation.Backstage;

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
    string FeedbackUrl);

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
        bool optionsAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(appAssembly);

        return new LocalAccountInfoPlan(
            ProductName: AppHelpInfo.ProductName,
            VersionText: AppHelpInfo.GetBuildVersionText(appAssembly),
            DeviceName: NormalizeOrUnknown(deviceName),
            UserName: NormalizeOrUnknown(userName),
            OptionsAvailable: optionsAvailable,
            TrademarkNotice: AppHelpInfo.TrademarkNotice,
            LicenseNotice: AppHelpInfo.ProjectLicenseNotice,
            PrivacyNotice: AppHelpInfo.PrivacyNotice,
            HelpUrl: AppHelpInfo.HelpUrl,
            FeedbackUrl: AppHelpInfo.FeedbackUrl);
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
            currentWorkbookPath,
            currentWorkbookName,
            plan.TrademarkNotice,
            plan.LicenseNotice,
            plan.PrivacyNotice);
    }

    private static string NormalizeOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
