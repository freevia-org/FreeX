using System.Globalization;
using System.Threading;

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
/// delegates.
/// </remarks>
public static class AvaloniaAppLocalizationBootstrap
{
    public static void InstallSharedSeams(
        Func<string, string> get,
        Func<string, object?[], string> format,
        Func<string, string>? createAutomationName = null)
    {
        ApplicationLocalizationSeamInstaller.Install(get, format, createAutomationName);
    }

    /// <summary>
    /// Applies the user's chosen application language to the UI culture, the Avalonia counterpart of
    /// <c>Free.Shared.Shell.Wpf.WpfAppLocalizationBootstrap.ApplyAppLanguage</c>.
    ///
    /// r189 (backlog item 5): this used to be absent on purpose -- the remark here said the Avalonia
    /// shells resolve CurrentUICulture from the OS -- but the Avalonia Options dialog offers the
    /// language field, validates it, persists it, and shows a restart notice. The app was therefore
    /// telling the user a restart would apply a setting nothing ever read. Making the promise true is
    /// the smaller change: only the WPF-specific FrameworkElement.Language metadata step
    /// (ApplyCurrentCultureToWpf) is toolkit-bound; setting the UI culture is plain BCL.
    /// </summary>
    /// <param name="cultureName">
    /// The persisted setting. Null, empty, or an unrecognised name must resolve to
    /// <paramref name="fallbackCulture"/> via <paramref name="resolveCulture"/> rather than throw --
    /// a settings file naming a culture this build no longer ships must not stop the app starting.
    /// </param>
    public static void ApplyAppLanguage(
        string? cultureName,
        Func<string?, CultureInfo> resolveCulture,
        CultureInfo fallbackCulture)
    {
        ArgumentNullException.ThrowIfNull(resolveCulture);
        ArgumentNullException.ThrowIfNull(fallbackCulture);

        var uiCulture = resolveCulture(cultureName) ?? fallbackCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;
    }
}
