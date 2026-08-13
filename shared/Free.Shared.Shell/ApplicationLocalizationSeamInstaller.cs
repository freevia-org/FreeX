namespace Free.Shared.Shell;

/// <summary>
/// Installs resource-backed shell and Backstage text adapters for any desktop renderer.
/// </summary>
public static class ApplicationLocalizationSeamInstaller
{
    public static void Install(
        Func<string, string> get,
        Func<string, object?[], string> format,
        Func<string, string>? createAutomationName = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(format);

        ShellStrings.Current = new ResourceShellStrings(
            () => get("Common_Ok"),
            () => get("Common_Cancel"),
            () => get("Common_ErrorTitle"),
            () => get("Common_WarningTitle"),
            () => get("Common_InformationTitle"),
            () => get("Common_ConfirmTitle"),
            createAutomationName);
        BackstageStrings.Current = new ResourceBackstageStrings(get, format);
    }
}
