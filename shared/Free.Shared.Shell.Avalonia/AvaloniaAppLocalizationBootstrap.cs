namespace Free.Shared.Shell.Avalonia;

/// <summary>
/// Installs the shared shell/backstage localization seams (<see cref="ShellStrings.Current"/>,
/// <see cref="BackstageStrings.Current"/>) for an Avalonia sister app, mirroring what
/// <c>Free.Shared.Shell.Wpf.WpfAppLocalizationBootstrap.InstallSharedSeams</c> does for the WPF
/// host. Without this, every Avalonia shell (FreeX, FreeW, FreeP) leaves <c>ShellStrings.Current</c>
/// at its neutral-English <c>DefaultShellStrings</c> fallback forever, so the shared
/// <c>AvaloniaDialogButtonRowFactory.CreateOkCancel</c>/<c>AvaloniaUserMessageDialog</c> OK/Cancel
/// buttons and generic message-box titles never localize even though the app's own dialogs (which
/// call <c>UiText.Get</c> directly) already do.
/// </summary>
/// <remarks>
/// Deliberately platform-agnostic (no Avalonia API is referenced) — it lives in this assembly
/// because that is what every Avalonia app project already references, giving each app's
/// <c>App</c>/<c>Program</c> a single call to make with its own <c>UiText.Get</c>/<c>UiText.Format</c>
/// delegates. Unlike the WPF bootstrap, this does not touch thread culture: the Avalonia shells
/// resolve <see cref="System.Globalization.CultureInfo.CurrentUICulture"/> from the OS the same way
/// every other <c>UiText.Get</c> call site already does.
/// </remarks>
public static class AvaloniaAppLocalizationBootstrap
{
    public static void InstallSharedSeams(
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
