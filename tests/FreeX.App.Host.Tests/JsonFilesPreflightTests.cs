using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class JsonFilesPreflightTests
{
    [Fact]
    public void JsonFilesPreflight_ValidatesTrackedJsonFiles()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-JsonFiles.ps1"));

        script.Should().Contain("[string[]]$JsonRoots = @(\"docs\", \"release\")");
        script.Should().Contain("ConvertFrom-Json");
        script.Should().Contain("JSON validation failed");
        script.Should().Contain("Validated $($jsonFiles.Count) JSON file(s).");
    }

    [Fact]
    public void JsonFilesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-JsonFiles.ps1");

        var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("JSON file(s).");
    }

    [Fact]
    public void JsonFilesPreflight_FailsWhenJsonIsMalformed()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-json-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "broken.json"), "{ \"name\": ");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-JsonFiles.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-JsonRoots \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("JSON validation failed");
            (result.Output + result.Error).Should().Contain("broken.json");
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
