using System.Globalization;
using System.IO;
using Free.Shared.Shell;
using FreeP.App.Compositor;
using FreeP.App.Localization;

namespace FreeP.App.Host.Tests;

public sealed class FreePLocalizationStartupTests : IDisposable
{
    private readonly CultureInfo _originalCurrentCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalCurrentUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _originalDefaultThreadCurrentUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly IShellStrings _originalShellStrings = ShellStrings.Current;
    private readonly IBackstageStrings _originalBackstageStrings = BackstageStrings.Current;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCurrentCulture;
        CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultThreadCurrentUiCulture;
        ShellStrings.Current = _originalShellStrings;
        BackstageStrings.Current = _originalBackstageStrings;
    }

    [Fact]
    public void InstallSharedSeams_RoutesSharedShellThroughFreePResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AppLocalization.Bootstrap.InstallSharedSeams();

        ShellStrings.Current.Cancel.Should().Be("_Annuler");
        ShellStrings.Current.Ok.Should().Be("_OK");
        ShellStrings.Current.ErrorTitle.Should().Be("Erreur");
        ShellStrings.Current.CreateAutomationName("_Open _File").Should().Be("Open File");
    }

    [Fact]
    public void InstallSharedSeams_RoutesSharedBackstageThroughFreePResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AppLocalization.Bootstrap.InstallSharedSeams();

        BackstageStrings.Current.Get("Backstage_GreetingMorning").Should().Be("Bonjour");
        BackstageStrings.Current.Format("Backstage_Recent_OpenRecentFileAutomationName", "Roadmap.pptx")
            .Should()
            .Be("Open recent presentation Roadmap.pptx");
        BackstageStrings.Current.Get("Backstage_Recent_RemoveAutomationHelpText")
            .Should()
            .Contain("presentation");
        BackstageStrings.Current.Get(FreePBackstagePaneResourceKeys.OptionsEditText)
            .Should()
            .Be("Modifier les options…");
    }

    [Fact]
    public void ApplyAppLanguage_UsesPersistedUiLanguageForResourceLookup()
    {
        AppLocalization.Bootstrap.InstallSharedSeams();

        AppLocalization.Bootstrap.ApplyAppLanguage(AppLanguageCatalog.PseudoLocalizationCultureName);

        string.Equals(
            CultureInfo.CurrentUICulture.Name,
            AppLanguageCatalog.PseudoLocalizationCultureName,
            StringComparison.OrdinalIgnoreCase)
            .Should()
            .BeTrue();
        ShellStrings.Current.Cancel.Should().Contain("CCaanncceell");
        BackstageStrings.Current.Get("Backstage_GreetingMorning").Should().Contain("GGoooodd");
    }

    [Fact]
    public void Program_InstallsResourceBackedSeamsInsteadOfNeutralDefaults()
    {
        var program = File.ReadAllText(RepositoryFile("freep", "FreeP.App.Host", "Program.cs"));
        var composition = File.ReadAllText(RepositoryFile("freep", "FreeP.App.Host", "AppComposition.cs"));

        program.Should().Contain("InstallSharedSeams = AppComposition.InstallSharedSeams");
        program.Should().Contain("ApplyUiLanguage: AppLocalization.Bootstrap.ApplyAppLanguage");
        program.Should().Contain("ApplyCurrentCultureToWpf: AppLocalization.Bootstrap.ApplyCurrentCultureToWpf");
        composition.Should().Contain("AppLocalization.Bootstrap.InstallSharedSeams();");
        composition.Should().NotContain("StaticShellStrings.ForProductTitle");
        composition.Should().NotContain("DefaultBackstageStrings.Instance");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
