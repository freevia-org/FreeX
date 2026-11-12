using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class UserTestPublishScriptTests
{
    [Fact]
    public void PublishScript_BuildsSmallFrameworkDependentSingleFileArtifactByDefault()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Publish-UserTestBuild.ps1");

        script.Should().Contain("[string]$OutputRoot = \"artifacts\\releases\"");
        script.Should().Contain("[string]$Version = \"\"");
        script.Should().Contain("[ValidateSet(\"SingleFile\", \"Folder\", \"Msix\", \"Velopack\")]");
        script.Should().Contain("[string]$PublishMode = \"SingleFile\"");
        script.Should().Contain("function Get-MsBuildPropertyValue");
        script.Should().Contain("InformationalVersion");
        script.Should().Contain("function ConvertTo-MsBuildVersion");
        script.Should().Contain("function ConvertTo-MsixPackageVersion");
        script.Should().Contain("function Assert-SafeArtifactToken");
        script.Should().Contain("function Assert-SafeTimestampUrl");
        script.Should().Contain("function Assert-MsixCertificatePath");
        script.Should().Contain("function Assert-MsixSigningOptions");
        script.Should().Contain("function Assert-MsixPublishSigningMode");
        script.Should().Contain("Assert-SafeArtifactToken -Value $RuntimeIdentifier -Label \"RuntimeIdentifier\"");
        script.Should().Contain("Assert-SafeTimestampUrl -Value $MsixTimestampUrl");
        script.Should().Contain("Assert-MsixCertificatePath -Value $MsixCertificatePath");
        script.Should().Contain("Assert-MsixSigningOptions -CertificatePath $MsixCertificatePath -CertificatePassword $MsixCertificatePassword -TimestampUrl $MsixTimestampUrl");
        script.Should().Contain("Assert-MsixPublishSigningMode -PublishMode $PublishMode -CertificatePath $MsixCertificatePath -AllowUnsigned ([bool]$AllowUnsignedMsix)");
        script.Should().Contain("rev-parse --short=8 HEAD");
        script.Should().Contain("$assemblyVersion = ConvertTo-MsBuildVersion -DisplayVersion $Version");
        script.Should().Contain("$informationalVersion = \"$assemblyVersion+$commitId\"");
        script.Should().Contain("$buildStamp = Get-Date -Format \"yyyyMMdd-HHmmss\"");
        script.Should().Contain("freex-$versionSlug-$buildStamp-$commitId-$RuntimeIdentifier-$modeSlug");
        script.Should().Contain("$launchExeName = \"$artifactName.exe\"");
        script.Should().Contain("Move-Item -LiteralPath $defaultExePath -Destination $artifactExePath");
        script.Should().Contain("IsPathRooted");
        script.Should().Contain("\"--self-contained\", \"false\"");
        script.Should().Contain("-p:Version=$assemblyVersion");
        script.Should().Contain("-p:InformationalVersion=$informationalVersion");
        script.Should().NotContain("--disable-build-servers");
        script.Should().NotContain("-p:UseSharedCompilation=false");
        script.Should().NotContain("-p:NodeReuse=false");
        script.Should().NotContain("/nr:false");
        script.Should().Contain("-p:PublishSingleFile=true");
        script.Should().Contain("-p:FreeXTesterReleaseEnglishOnly=true");
        script.Should().NotContain("-p:EnableCompressionInSingleFile=true");
        script.Should().Contain("-p:IncludeAllContentForSelfExtract=true");
        script.Should().Contain("[string]$RuntimeIdentifier = \"win-x64\"");
        script.Should().Contain("\"-r\", $RuntimeIdentifier");
        script.Should().Contain("$LASTEXITCODE");
        script.Should().Contain("--tester-release-smoke");
        script.Should().Contain("$smokeReport.BorderPixelSnapPassed");
        script.Should().Contain("Published app smoke passed:");
        script.Should().Contain("$artifactExePath = Join-Path $artifactRoot \"$artifactName.exe\"");
        script.Should().Contain("$artifactExeHashPath = \"$artifactExePath.sha256\"");
        script.Should().Contain("Remove-Item -LiteralPath $artifactExeHashPath -Force");
        script.Should().Contain("Remove-Item -LiteralPath $publishDir -Recurse -Force");
        script.Should().Contain("Write-Host \"Created $artifactExePath\"");
        script.Should().Contain("Write-Host \"Created $artifactExeHashPath\"");
        script.Should().Contain("Local diagnostics:");
        script.Should().Contain("%LOCALAPPDATA%\\FreeX\\Diagnostics");
        script.Should().Contain("FREEX_DIAGNOSTICS=0");
        script.Should().Contain("FreeX is not affiliated with, endorsed by, or sponsored by Microsoft.");
        script.Should().Contain("Microsoft Excel is a trademark of Microsoft Corporation.");
        script.Should().Contain("In the app: Help > Legal Notices");
        script.Should().Contain("docs/legal/privacy.md");
        script.Should().Contain("THIRD_PARTY_NOTICES.md");
    }

    [Fact]
    public void PublishScript_EnglishOnlyTesterExeExcludesLocalizedSatelliteResources()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Publish-UserTestBuild.ps1");
        var project = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "FreeX.App.Host.csproj");

        script.Should().Contain("if ($PublishMode -eq \"SingleFile\")");
        script.Should().Contain("-p:FreeXTesterReleaseEnglishOnly=true");
        project.Should().Contain("Condition=\"'$(FreeXTesterReleaseEnglishOnly)' == 'true'\"");
        project.Should().Contain("EmbeddedResource Remove=\"Resources/Strings.*.resx\"");
        project.Should().NotContain("EmbeddedResource Remove=\"Resources/Strings.resx\"");
    }

    [Fact]
    public void PublishScript_RejectsUnsignedMsixUnlessExplicitlyAllowedBeforePublishing()
    {
        using var temp = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Publish-UserTestBuild.ps1",
            $"-PublishMode Msix -Version 0.8.0 -OutputRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("MSIX packages require MsixCertificatePath; pass -AllowUnsignedMsix only for local packaging validation.");
        Directory.GetFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void PublishScript_RejectsMsixSigningOptionsWithoutCertificatePathBeforePublishing()
    {
        using var temp = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Publish-UserTestBuild.ps1",
            $"-PublishMode Msix -MsixCertificatePassword \"placeholder\" -Version 0.8.0 -OutputRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("MSIX signing options require MsixCertificatePath");
        Directory.GetFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void PublishScript_RejectsDirectoryMsixCertificatePathBeforePublishing()
    {
        using var temp = new TestTemporaryDirectory();
        var outputDirectory = Path.Combine(temp.Path, "out");
        var certificateDirectory = Path.Combine(temp.Path, "certificate");
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(certificateDirectory);

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Publish-UserTestBuild.ps1",
            $"-PublishMode Msix -MsixCertificatePath \"{certificateDirectory}\" -Version 0.8.0 -OutputRoot \"{outputDirectory}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("MsixCertificatePath must reference an existing certificate file");
        Directory.GetFileSystemEntries(outputDirectory).Should().BeEmpty();
    }

    [Fact]
    public void PublishScript_RejectsUnsafeMsixTimestampUrlBeforePublishing()
    {
        using var temp = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Publish-UserTestBuild.ps1",
            $"-PublishMode Msix -MsixTimestampUrl \"file:///local/timestamp\" -Version 0.8.0 -OutputRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("MsixTimestampUrl must be an absolute http or https URL");
        Directory.GetFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void PublishScript_RejectsRuntimeIdentifierPathSegmentsBeforePublishing()
    {
        using var temp = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Publish-UserTestBuild.ps1",
            $"-RuntimeIdentifier \"..\\outside\" -Version 0.8.0 -OutputRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("RuntimeIdentifier must contain only letters, numbers, dots, and hyphens");
        result.CombinedOutput.Should().Contain("path separators");
        Directory.GetFileSystemEntries(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public void PublishScript_KeepsFrameworkDependentFolderModeAvailable()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Publish-UserTestBuild.ps1");

        script.Should().Contain("if ($PublishMode -eq \"SingleFile\")");
        script.Should().Contain("-p:PublishSingleFile=false");
        script.Should().Contain("FreeX.cmd");
        script.Should().Contain("Move-Item -LiteralPath $defaultExePath -Destination $launchExePath");
        script.Should().Contain("set \"APP_EXE=%APP_DIR%$launchExeName\"");
        script.Should().Contain("Compress-Archive");
        script.Should().Contain("Test-Path -LiteralPath $zipPath");
        script.Should().Contain("$zipHashPath = \"$zipPath.sha256\"");
        script.Should().Contain("Remove-Item -LiteralPath $zipHashPath -Force");
        script.Should().Contain("Get-FileHash");
    }

    [Fact]
    public void PublishScript_NormalizesMsixVersionsWhenRunNumberExceedsPackagePartLimit()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Publish-UserTestBuild.ps1");

        script.Should().Contain("$numericParts = [regex]::Matches($DisplayVersion, '\\d+') | ForEach-Object { [int64]$_.Value }");
        script.Should().Contain("$msixParts = @(0L, 0L, 0L, 0L)");
        script.Should().Contain("for ($i = 3; $i -gt 0; $i--)");
        script.Should().Contain("$carry = [Math]::Floor($msixParts[$i] / 65536)");
        script.Should().Contain("$msixParts[$i] = $msixParts[$i] % 65536");
        script.Should().Contain("$msixParts[$i - 1] += $carry");
        script.Should().Contain("throw \"MSIX version part '$($msixParts[0])' is outside the 0-65535 range.\"");
        script.Should().Contain("$msixVersion = ConvertTo-MsixPackageVersion -DisplayVersion $Version");
        script.Should().Contain("$artifactMsixHashPath = \"$artifactMsixPath.sha256\"");
        script.Should().Contain("Remove-Item -LiteralPath $artifactMsixHashPath -Force");
    }

    [Fact]
    public void PublishScript_WritesLauncherThatGuidesDesktopRuntimeInstall()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Publish-UserTestBuild.ps1");

        script.Should().Contain("Microsoft.WindowsDesktop.App");
        script.Should().Contain("https://dotnet.microsoft.com/download/dotnet/10.0");
        script.Should().Contain("FreeX.cmd");
    }

}
