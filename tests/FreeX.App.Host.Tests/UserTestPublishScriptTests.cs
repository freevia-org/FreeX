using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class UserTestPublishScriptTests
{
    [Fact]
    public void PublishScript_BuildsSmallFrameworkDependentSingleFileArtifactByDefault()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[string]$OutputRoot = \"artifacts\\releases\"");
        script.Should().Contain("[string]$Version = \"\"");
        script.Should().Contain("[ValidateSet(\"SingleFile\", \"Folder\", \"Msix\")]");
        script.Should().Contain("[string]$PublishMode = \"SingleFile\"");
        script.Should().Contain("AppInfo.cs");
        script.Should().Contain("function ConvertTo-MsixPackageVersion");
        script.Should().Contain("rev-parse --short=8 HEAD");
        script.Should().Contain("$buildStamp = Get-Date -Format \"yyyyMMdd-HHmmss\"");
        script.Should().Contain("freex-$versionSlug-$buildStamp-$commitId-$RuntimeIdentifier-$modeSlug");
        script.Should().Contain("$launchExeName = \"$artifactName.exe\"");
        script.Should().Contain("Move-Item -LiteralPath $defaultExePath -Destination $launchExePath");
        script.Should().Contain("IsPathRooted");
        script.Should().Contain("\"--self-contained\", \"false\"");
        script.Should().Contain("-p:PublishSingleFile=true");
        script.Should().NotContain("-p:EnableCompressionInSingleFile=true");
        script.Should().Contain("-p:IncludeAllContentForSelfExtract=true");
        script.Should().Contain("[string]$RuntimeIdentifier = \"win-x64\"");
        script.Should().Contain("\"-r\", $RuntimeIdentifier");
        script.Should().Contain("$LASTEXITCODE");
        script.Should().Contain("$artifactExePath = Join-Path $artifactRoot \"$artifactName.exe\"");
        script.Should().Contain("$artifactExeHashPath = \"$artifactExePath.sha256\"");
        script.Should().Contain("Remove-Item -LiteralPath $artifactExeHashPath -Force");
        script.Should().Contain("Move-Item -LiteralPath $launchExePath -Destination $artifactExePath");
        script.Should().Contain("Remove-Item -LiteralPath $publishDir -Recurse -Force");
        script.Should().Contain("Write-Host \"Created $artifactExePath\"");
        script.Should().Contain("Write-Host \"Created $artifactExeHashPath\"");
        script.Should().Contain("Local diagnostics:");
        script.Should().Contain("%LOCALAPPDATA%\\FreeX\\Diagnostics");
        script.Should().Contain("FREEX_DIAGNOSTICS=0");
        script.Should().Contain("FreeX is not affiliated with, endorsed by, or sponsored by Microsoft.");
        script.Should().Contain("Microsoft Excel is a trademark of Microsoft Corporation.");
        script.Should().Contain("docs/PRIVACY.md");
        script.Should().Contain("THIRD_PARTY_NOTICES.md");
    }

    [Fact]
    public void PublishScript_KeepsFrameworkDependentFolderModeAvailable()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("if ($PublishMode -eq \"SingleFile\")");
        script.Should().Contain("-p:PublishSingleFile=false");
        script.Should().Contain("FreeX.cmd");
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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

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
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("Microsoft.WindowsDesktop.App");
        script.Should().Contain("https://dotnet.microsoft.com/download/dotnet/10.0");
        script.Should().Contain("FreeX.cmd");
    }
}
