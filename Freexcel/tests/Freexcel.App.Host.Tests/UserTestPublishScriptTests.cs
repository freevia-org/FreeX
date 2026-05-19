using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class UserTestPublishScriptTests
{
    [Fact]
    public void PublishScript_BuildsSmallFrameworkDependentFolderArtifact()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Publish-UserTestBuild.ps1");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("[string]$OutputRoot = \"artifacts\\releases\"");
        script.Should().Contain("[string]$Version = \"\"");
        script.Should().Contain("AppInfo.cs");
        script.Should().Contain("rev-parse --short=8 HEAD");
        script.Should().Contain("$buildStamp = Get-Date -Format \"yyyyMMdd-HHmmss\"");
        script.Should().Contain("freexcel-$versionSlug-$buildStamp-$commitId-$RuntimeIdentifier");
        script.Should().Contain("$launchExeName = \"$artifactName.exe\"");
        script.Should().Contain("Move-Item -LiteralPath $defaultExePath -Destination $launchExePath");
        script.Should().Contain("set \"APP_EXE=%APP_DIR%$launchExeName\"");
        script.Should().Contain("IsPathRooted");
        script.Should().Contain("--self-contained false");
        script.Should().Contain("[string]$RuntimeIdentifier = \"win-x64\"");
        script.Should().Contain("-r $RuntimeIdentifier");
        script.Should().NotContain("PublishSingleFile=true");
        script.Should().NotContain("EnableCompressionInSingleFile=true");
        script.Should().Contain("$LASTEXITCODE");
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
