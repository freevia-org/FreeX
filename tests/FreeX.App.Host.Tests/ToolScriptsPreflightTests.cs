using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ToolScriptsPreflightTests
{
    [Fact]
    public void ToolScriptsPreflight_ParsesAllPowerShellTools()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-ToolScripts.ps1");

        script.Should().Contain("Get-ChildItem -LiteralPath $resolvedScriptDirectory -Filter \"*.ps1\" -File");
        script.Should().Contain("[System.Management.Automation.Language.Parser]::ParseFile");
        script.Should().Contain("PowerShell syntax validation failed");
        script.Should().Contain("preflight scripts must set `$ErrorActionPreference = `\"Stop`\".");
        script.Should().Contain("PowerShell fail-fast validation failed");
        script.Should().Contain("Validated $($scripts.Count) PowerShell tool script(s).");
    }

    [Fact]
    public void ToolScriptsPreflight_FailsWhenPreflightScriptOmitsFailFastMode()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "Test-MissingFailFast.ps1"), "Write-Host \"ok\"");
        var scriptPath = WorkspaceFileLocator.FindToolScript("Test-ToolScripts.ps1");
        using var workingDirectory = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, $"-ScriptDirectory \"{temp.Path}\"");
        var combinedOutput = (result.Output + result.Error)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("PowerShell fail-fast validation failed");
        combinedOutput.Should().Contain("Test-MissingFailFast.ps1");
    }

    [Fact]
    public void ToolScriptsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.FindToolScript("Test-ToolScripts.ps1");
        using var workingDirectory = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("PowerShell tool script(s).");
    }

    [Fact]
    public void ToolScriptsPreflight_FailsWhenScriptHasSyntaxError()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.ps1"), "param(`nif (`n");
        var scriptPath = WorkspaceFileLocator.FindToolScript("Test-ToolScripts.ps1");
        using var workingDirectory = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, $"-ScriptDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("PowerShell syntax validation failed");
    }

}
