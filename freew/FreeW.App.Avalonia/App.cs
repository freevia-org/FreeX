using Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

public sealed partial class App : Application
{
    internal static Theme ActiveTheme { get; private set; } = FreeWApplicationStartup.Theme.DefaultTheme;

    internal static SisterAvaloniaStandardDesktopProfile<App, MainWindow, FreeWOptions> DesktopProfile { get; } =
        new(
            FreeWApplicationStartup.ProductIdentity,
            new SisterAvaloniaLocalizationStartupDescriptor(
                () => AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
                    UiText.Get,
                    UiText.Format,
                    UiText.CreateAutomationName)),
            new SisterAvaloniaThemeStartupDescriptor<Theme>(
                FreeWApplicationStartup.Theme,
                theme => ActiveTheme = theme,
                (application, theme, resourceKeyPrefix) =>
                    application.Resources.MergedDictionaries.Add(
                        AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix))),
            // R169: must resolve to the SAME settings.json path as the WPF host
            // (FreeW.App.Host/Program.cs, which never overrides OptionsPathProvider and so falls
            // back to PlatformApplicationDataPathProvider.Instance -- %APPDATA%, not %LOCALAPPDATA%).
            // This used to pass LocalInstance here, so a user who ran both shells on one Windows
            // machine had every preference silently revert to defaults depending on which shell
            // they launched. Passing no provider takes the same Instance default as WPF, and
            // MigrateLegacyLocalSettings (below) recovers a settings.json a pre-fix build already
            // wrote under the old (LocalInstance) path before this store loads.
            new SisterAvaloniaOptionsStartupDescriptor<FreeWOptions>(
                () =>
                {
                    MigrateLegacyLocalSettings();
                    return ApplicationOptionsStore<FreeWOptions>.Create();
                }),
            new SisterAvaloniaWindowStartupDescriptor<MainWindow, FreeWOptions>(
                (startupArguments, options, optionsStore) =>
                    new MainWindow(startupArguments, options, optionsStore)),
            onEmergencySnapshot: AutosaveAdapter.TryEmergencySnapshots);

    public override void OnFrameworkInitializationCompleted()
    {
        SisterAvaloniaStandardDesktopFactory.Initialize(this, DesktopProfile);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// R169 one-time recovery for a user who already has two settings.json files because a pre-fix
    /// build of this Avalonia shell wrote to <c>%LOCALAPPDATA%\FreeW\settings.json</c> while the WPF
    /// host always wrote to <c>%APPDATA%\FreeW\settings.json</c> (the two now-shared, canonical path).
    /// Policy, applied before every load so the two shells stay reconciled even if a stray legacy
    /// write ever recurs:
    /// <list type="bullet">
    /// <item>Only the legacy file exists (an Avalonia-only user) -&gt; it is copied to the canonical
    /// path, so their preferences survive instead of reading back as fresh defaults.</item>
    /// <item>Both exist and the legacy file was written more recently -&gt; the user's latest edits
    /// were made in this shell, so the legacy file is copied over the canonical one (last-write
    /// wins).</item>
    /// <item>Both exist and the canonical file is the same age or newer -&gt; the canonical file
    /// (the WPF host's, or an already-migrated one) is left alone.</item>
    /// </list>
    /// The legacy file itself is never deleted in any of these cases, so no preference set is ever
    /// silently discarded -- the losing file simply becomes an inert leftover on disk. Best-effort
    /// only: any I/O failure here must not block startup, so the shell falls back to loading (or
    /// creating) the canonical file exactly as it would if there were nothing to migrate.
    /// </summary>
    private static void MigrateLegacyLocalSettings()
    {
        try
        {
            var legacyPath = JsonSettingsStore<FreeWOptions>.GetProductFilePath(
                ApplicationOptionsStore<FreeWOptions>.DefaultFileName,
                PlatformApplicationDataPathProvider.LocalInstance);
            var canonicalPath = JsonSettingsStore<FreeWOptions>.GetProductFilePath(
                ApplicationOptionsStore<FreeWOptions>.DefaultFileName,
                PlatformApplicationDataPathProvider.Instance);

            ReconcileLegacySettingsFile(legacyPath, canonicalPath);
        }
        catch
        {
            // Best-effort only -- see summary above.
        }
    }

    /// <summary>
    /// The pure file-reconciliation policy described above, isolated from real path resolution so it
    /// can be exercised against temp-directory paths in <c>FreeW.App.Avalonia.Tests</c> without ever
    /// touching a real <c>%APPDATA%</c>/<c>%LOCALAPPDATA%</c>. Returns <see langword="true"/> when it
    /// copied <paramref name="legacyPath"/> onto <paramref name="canonicalPath"/>.
    /// </summary>
    internal static bool ReconcileLegacySettingsFile(string legacyPath, string canonicalPath)
    {
        // Same folder on this platform (macOS): there is nothing to reconcile.
        if (string.Equals(legacyPath, canonicalPath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!System.IO.File.Exists(legacyPath))
            return false;

        if (System.IO.File.Exists(canonicalPath) &&
            System.IO.File.GetLastWriteTimeUtc(legacyPath) <=
                System.IO.File.GetLastWriteTimeUtc(canonicalPath))
        {
            return false;
        }

        var canonicalDirectory = System.IO.Path.GetDirectoryName(canonicalPath);
        if (!string.IsNullOrEmpty(canonicalDirectory))
            System.IO.Directory.CreateDirectory(canonicalDirectory);

        System.IO.File.Copy(legacyPath, canonicalPath, overwrite: true);
        return true;
    }
}
