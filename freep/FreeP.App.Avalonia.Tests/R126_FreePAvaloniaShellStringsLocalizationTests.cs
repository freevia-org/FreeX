using System.Globalization;
using System.IO;
using System.Linq;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// R126: <c>Free.Shared.Shell.ShellStrings.Current</c> — the source the shared
/// <c>AvaloniaDialogButtonRowFactory.CreateOkCancel</c> and <c>AvaloniaUserMessageDialog</c> read
/// their OK/Cancel button text and generic message-box titles from — was never wired for FreeP's
/// Avalonia shell, so it stayed pinned at the shared shell's neutral-English
/// <c>DefaultShellStrings</c> fallback regardless of the user's locale. Mirrors
/// <c>FreeX.App.Avalonia.Tests.R126_FreeXAvaloniaShellStringsLocalizationTests</c> and the WPF host's
/// <c>FreePLocalizationStartupTests</c> for the shared seams.
/// </summary>
public sealed class R126_FreePAvaloniaShellStringsLocalizationTests : IDisposable
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

    [Fact]
    public void ShellStrings_Current_StaysNeutralEnglish_UntilSharedSeamsAreInstalled()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        ShellStrings.Current.Cancel.Should().Be("_Cancel");
        ShellStrings.Current.Ok.Should().Be("_OK");
    }

    [Fact]
    public void InstallSharedSeams_RoutesSharedShellThroughFreePResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName);

        ShellStrings.Current.Cancel.Should().Be("_Annuler");
        ShellStrings.Current.Ok.Should().Be("_OK");
        ShellStrings.Current.ErrorTitle.Should().Be("Erreur");
        ShellStrings.Current.CreateAutomationName("_Open _File").Should().Be("Open File");
    }

    [Fact]
    public void InstallSharedSeams_RoutesSharedBackstageThroughFreePResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName);

        BackstageStrings.Current.Get("Backstage_GreetingMorning").Should().Be("Bonjour");
        BackstageStrings.Current.Get(FreePBackstagePaneResourceKeys.OptionsEditText)
            .Should()
            .Be("Modifier les options…");
    }

    /// <summary>
    /// Source-hygiene guard: proves the real production entry point (FreeP.App.Avalonia's
    /// <c>App.OnFrameworkInitializationCompleted</c>) actually calls the bootstrap.
    /// </summary>
    [Fact]
    public void App_InstallsResourceBackedSharedSeamsAtStartup()
    {
        var source = File.ReadAllText(RepositoryFile("freep", "FreeP.App.Avalonia", "App.cs"));

        source.Should().Contain(
            "AvaloniaAppLocalizationBootstrap.InstallSharedSeams(UiText.Get, UiText.Format, UiText.CreateAutomationName)");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
