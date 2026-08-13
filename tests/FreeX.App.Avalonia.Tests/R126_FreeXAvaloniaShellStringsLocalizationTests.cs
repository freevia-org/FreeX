using System.Globalization;
using System.IO;
using System.Linq;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R126: <c>Free.Shared.Shell.ShellStrings.Current</c> — the source the shared
/// <c>AvaloniaDialogButtonRowFactory.CreateOkCancel</c> and <c>AvaloniaUserMessageDialog</c> read
/// their OK/Cancel button text and generic message-box titles from — was never wired for any
/// Avalonia shell (FreeX, FreeW, FreeP), so it stayed pinned at the shared shell's neutral-English
/// <c>DefaultShellStrings</c> fallback regardless of the user's locale, even though the app's own
/// hand-rolled dialogs (which call <c>UiText.Get</c> directly, e.g. <c>MainWindow.PivotName.cs</c>'s
/// "Common_Ok"/"Common_Cancel" buttons) already localized correctly. Mirrors the WPF host's
/// <c>FreeWLocalizationStartupTests</c>/<c>FreePLocalizationStartupTests</c> pattern for the shared
/// seams the WPF-side <c>WpfAppLocalizationBootstrap.InstallSharedSeams</c> already installs.
/// </summary>
public sealed class R126_FreeXAvaloniaShellStringsLocalizationTests : IDisposable
{
    private readonly CultureInfo _originalCurrentUiCulture = CultureInfo.CurrentUICulture;
    private readonly IShellStrings _originalShellStrings = ShellStrings.Current;
    private readonly IBackstageStrings _originalBackstageStrings = BackstageStrings.Current;

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
        ShellStrings.Current = _originalShellStrings;
        BackstageStrings.Current = _originalBackstageStrings;
    }

    /// <summary>
    /// Reproduces the defect directly: with nothing having installed FreeX's shared seams yet
    /// (the state every Avalonia shell was permanently stuck in before this fix), ShellStrings.Current
    /// stays at its neutral-English default no matter what the UI culture is set to.
    /// </summary>
    [Fact]
    public void ShellStrings_Current_StaysNeutralEnglish_UntilSharedSeamsAreInstalled()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        ShellStrings.Current.Cancel.Should().Be("_Cancel");
        ShellStrings.Current.Ok.Should().Be("_OK");
        ShellStrings.Current.ErrorTitle.Should().Be("Error");
    }

    [Fact]
    public void InstallSharedSeams_RoutesSharedShellThroughFreeXResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName);

        ShellStrings.Current.Cancel.Should().Be("_Annuler");
        ShellStrings.Current.Ok.Should().Be("_OK");
        ShellStrings.Current.ErrorTitle.Should().Be("Erreur");
        ShellStrings.Current.WarningTitle.Should().Be("Avertissement");
        ShellStrings.Current.InformationTitle.Should().Be("Informations");
        ShellStrings.Current.ConfirmTitle.Should().Be("Confirmation");
        ShellStrings.Current.CreateAutomationName("_Open _File").Should().Be("Open File");
    }

    [Fact]
    public void InstallSharedSeams_RoutesSharedBackstageThroughFreeXResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName);

        BackstageStrings.Current.Get("Backstage_GreetingMorning").Should().Be("Bonjour");
    }

    /// <summary>
    /// No-regression sibling: switching back to a default/English UI culture after installing the
    /// seams must not leave stale French text behind — the installed <see cref="ResourceShellStrings"/>
    /// re-resolves against <see cref="CultureInfo.CurrentUICulture"/> on every access, exactly like
    /// every existing <c>UiText.Get</c> call site the app's hand-rolled Avalonia dialogs already use.
    /// </summary>
    [Fact]
    public void InstallSharedSeams_ShellStrings_TrackCultureChangesAfterInstall()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName);
        ShellStrings.Current.Cancel.Should().Be("_Annuler");

        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        ShellStrings.Current.Cancel.Should().Be("_Cancel");
    }

    /// <summary>
    /// Source-hygiene guard: proves the real production entry point (FreeX.App.Avalonia's
    /// <c>App.OnFrameworkInitializationCompleted</c>) actually calls the bootstrap, not just that the
    /// bootstrap class works in isolation — a future edit that silently drops the call would otherwise
    /// leave every other test in this file passing while the shipped app regresses back to the defect.
    /// </summary>
    [Fact]
    public void App_InstallsResourceBackedSharedSeamsAtStartup()
    {
        var source = File.ReadAllText(RepositoryFile("src", "FreeX.App.Avalonia", "App.cs"));

        source.Should().Contain(
            "AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName)");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
