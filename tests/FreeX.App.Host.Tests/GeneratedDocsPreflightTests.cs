using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class GeneratedDocsPreflightTests
{
    [Fact]
    public void GeneratedDocsPreflight_RunsAllGeneratedDocumentationChecks()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-GeneratedDocs.ps1"));

        script.Should().Contain("Generate-CommandInventoryDocs.ps1");
        script.Should().Contain("& $resolvedScriptPath -Check");
        script.Should().Contain("Generated documentation checks passed.");
    }

    [Fact]
    public void GeneratedDocsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-GeneratedDocs.ps1");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(0, error);
        output.Should().Contain("Checking command inventory generated docs...");
        output.Should().Contain("Generated documentation checks passed.");
    }
}
