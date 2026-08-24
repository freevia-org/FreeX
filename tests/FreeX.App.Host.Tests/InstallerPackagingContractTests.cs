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
        workflow.Should().Contain("tools/New-ReleaseArtifactManifest.ps1");
        workflow.Should().Contain("tools/New-ReleaseSbom.ps1");
        workflow.Should().Contain("tools/Test-ReleaseInstallation.ps1");
        workflow.Should().Contain("Microsoft.Sbom.DotNetTool --version 4.1.5");
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
        packager.Should().Contain("ResultCode <> 0");
        packager.Should().Contain("RaiseException(DisplayName + '' installation failed with exit code ''");
        packager.Should().Contain("$inputName = if ($Suite) { \"$app-v$Version-$Runtime-installer.zip\"");
        packager.Should().Contain("Find-UniqueInput \"$app-v$Version-$Runtime-apps.zip\"");
        packager.Should().Contain("Uninstallable=no");
    }

    [Fact]
    public void ReleaseWorkflow_EnforcesImmutableCommitAndFullInventory()
    {
        var workflow = WorkspaceFileLocator.ReadAllText(".github", "workflows", "app-tester-release.yml");
        var manifest = WorkspaceFileLocator.ReadAllText("tools", "New-ReleaseArtifactManifest.ps1");

        workflow.Should().Contain("Tag '$tag' is immutable at $tagSha; refusing to replace it with assets from $env:GITHUB_SHA");
        workflow.Should().Contain("contents: write");
        workflow.Should().Contain("contents: read");
        manifest.Should().Contain("ValidatePattern('^[0-9a-fA-F]{40}$')");
        manifest.Should().Contain("RequireRuntimeManifests");
        manifest.Should().Contain("StageLegalBundle");
        manifest.Should().Contain("kind = 'sbom'");
        manifest.Should().Contain("Checksum mismatch or non-canonical checksum content");
    }
}
