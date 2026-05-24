using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class UserTestPublishScriptTests
{
    [Fact]
    public void PublishScript_BuildsSmallFrameworkDependentSingleFileArtifactByDefault()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[string]$OutputRoot = \"artifacts\\releases\"");
        script.Should().Contain("[string]$Version = \"\"");
        script.Should().Contain("[ValidateSet(\"SingleFile\", \"Folder\")]");
        script.Should().Contain("[string]$PublishMode = \"SingleFile\"");
        script.Should().Contain("AppInfo.cs");
        script.Should().Contain("rev-parse --short=8 HEAD");
        script.Should().Contain("$buildStamp = Get-Date -Format \"yyyyMMdd-HHmmss\"");
        script.Should().Contain("freexcel-$versionSlug-$buildStamp-$commitId-$RuntimeIdentifier-$modeSlug");
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
        script.Should().Contain("Move-Item -LiteralPath $launchExePath -Destination $artifactExePath");
        script.Should().Contain("Remove-Item -LiteralPath $publishDir -Recurse -Force");
        script.Should().Contain("Write-Host \"Created $artifactExePath\"");
        script.Should().Contain("Local diagnostics:");
        script.Should().Contain("%LOCALAPPDATA%\\Freexcel\\Diagnostics");
        script.Should().Contain("FREEXCEL_DIAGNOSTICS=0");
    }

    [Fact]
    public void PublishScript_KeepsFrameworkDependentFolderModeAvailable()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("if ($PublishMode -eq \"SingleFile\")");
        script.Should().Contain("-p:PublishSingleFile=false");
        script.Should().Contain("Freexcel.cmd");
        script.Should().Contain("set \"APP_EXE=%APP_DIR%$launchExeName\"");
        script.Should().Contain("Compress-Archive");
        script.Should().Contain("Test-Path -LiteralPath $zipPath");
        script.Should().Contain("Get-FileHash");
    }

    [Fact]
    public void PublishScript_WritesLauncherThatGuidesDesktopRuntimeInstall()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("Microsoft.WindowsDesktop.App");
        script.Should().Contain("https://dotnet.microsoft.com/download/dotnet/10.0");
        script.Should().Contain("Freexcel.cmd");
    }
}
