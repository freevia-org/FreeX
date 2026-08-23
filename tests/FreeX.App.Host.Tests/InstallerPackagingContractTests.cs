using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class InstallerPackagingContractTests
{
    [Fact]
    public void CanonicalWorkflow_PreservesPortableAssetsAndBuildsIndividualAndSuiteInstallers()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "app-tester-release.yml");

        workflow.Should().Contain("-WindowsPackageMode SingleFile");
        workflow.Should().Contain("tools/packaging/New-AppInstallers.ps1");
        workflow.Should().Contain("-Apps \"${{ matrix.app }}\"");
        workflow.Should().Contain("-Apps FreeX,FreeW,FreeP");
        workflow.Should().Contain("-Suite");
        workflow.Should().Contain("free-suite-v$version");
        workflow.Should().Contain("unsigned/unnotarized `.app` bundle");
        workflow.Should().Contain("FreeFamilySentryDsn: ${{ secrets.FREE_FAMILY_SENTRY_DSN }}");
    }

    [Fact]
    public void SuitePackages_DelegateToIndividualInstallerIdentities()
    {
        var packager = WorkspaceFileLocator.ReadAllText("tools", "packaging", "New-AppInstallers.ps1");

        packager.Should().Contain("The suite is deliberately a non-owning bootstrapper");
        packager.Should().Contain("$childName = \"$app-v$Version-$Runtime-setup.exe\"");
        packager.Should().Contain("Parameters: `\"/SILENT /CURRENTUSER /NORESTART`\"");
        packager.Should().Contain("$inputName = if ($Suite) { \"$app-v$Version-$Runtime-installer.zip\"");
        packager.Should().Contain("Find-UniqueInput \"$app-v$Version-$Runtime-apps.zip\"");
        packager.Should().Contain("Uninstallable=no");
    }
}
