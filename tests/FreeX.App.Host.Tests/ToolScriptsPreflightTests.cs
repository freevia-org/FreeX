using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ToolScriptsPreflightTests
{
    [Fact]
    public void ToolScriptsPreflight_ParsesAllPowerShellTools()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-ToolScripts.ps1"));

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
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-tool-script-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "Test-MissingFailFast.ps1"), "Write-Host \"ok\"");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ToolScripts.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ScriptDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("PowerShell fail-fast validation failed");
            (result.Output + result.Error).Should().Contain("Test-MissingFailFast.ps1");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ToolScriptsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ToolScripts.ps1");

        var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("PowerShell tool script(s).");
    }

    [Fact]
    public void ToolScriptsPreflight_FailsWhenScriptHasSyntaxError()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-tool-script-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "broken.ps1"), "param(`nif (`n");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-ToolScripts.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ScriptDirectory \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("PowerShell syntax validation failed");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static PowerShellResult RunPowerShellScript(string scriptPath, string workingDirectory, string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new PowerShellResult(process.ExitCode, output, error);
    }

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
