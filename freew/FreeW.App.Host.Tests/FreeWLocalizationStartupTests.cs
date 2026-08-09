using System.Globalization;
using System.IO;
using Free.Shared.Shell;
using FreeW.App.Localization;

namespace FreeW.App.Host.Tests;

public sealed class FreeWLocalizationStartupTests : IDisposable
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
    public void InstallSharedSeams_RoutesSharedShellThroughFreeWResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AppLocalization.Bootstrap.InstallSharedSeams();

        ShellStrings.Current.Cancel.Should().Be("_Annuler");
        ShellStrings.Current.Ok.Should().Be("_OK");
        ShellStrings.Current.ErrorTitle.Should().Be("Erreur");
        ShellStrings.Current.CreateAutomationName("_Open _File").Should().Be("Open File");
    }

    [Fact]
    public void InstallSharedSeams_RoutesSharedBackstageThroughFreeWResources()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        AppLocalization.Bootstrap.InstallSharedSeams();

        BackstageStrings.Current.Get("Backstage_GreetingMorning").Should().Be("Bonjour");
        BackstageStrings.Current.Format("Backstage_Recent_OpenRecentFileAutomationName", "Roadmap.docx")
            .Should()
            .Be("Open recent document Roadmap.docx");
        BackstageStrings.Current.Get("Backstage_Recent_RemoveAutomationHelpText")
            .Should()
            .Contain("document");
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
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Host", "Program.cs"));

        source.Should().Contain("InstallSharedSeams = AppLocalization.Bootstrap.InstallSharedSeams");
        source.Should().Contain("ApplyUiLanguage: AppLocalization.Bootstrap.ApplyAppLanguage");
        source.Should().Contain("ApplyCurrentCultureToWpf: AppLocalization.Bootstrap.ApplyCurrentCultureToWpf");
        source.Should().NotContain("DefaultShellStrings.Instance");
        source.Should().NotContain("DefaultBackstageStrings.Instance");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
