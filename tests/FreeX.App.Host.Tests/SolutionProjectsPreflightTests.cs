using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SolutionProjectsPreflightTests
{
    [Fact]
    public void SolutionProjectsPreflight_ValidatesSolutionMembership()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1"));

        script.Should().Contain("FreeX.slnx");
        script.Should().Contain("Project missing from solution");
        script.Should().Contain("Solution references missing project");
        script.Should().Contain("Validated $($solutionProjectPaths.Count) solution project entry(s).");
    }

    [Fact]
    public void SolutionProjectsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

        var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("solution project entry(s).");
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenProjectIsMissingFromSolution()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-solution-project-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Missing"));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "FreeX.slnx"),
                """
                <Solution>
                  <Folder Name="/src/">
                    <Project Path="src/Included/Included.csproj" />
                  </Folder>
                </Solution>
                """);
            File.WriteAllText(Path.Combine(tempDirectory, "src", "Included", "Included.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(tempDirectory, "src", "Missing", "Missing.csproj"), "<Project />");

            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("Project missing from solution");
            (result.Output + result.Error).Should().Contain("src/Missing/Missing.csproj");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void SolutionProjectsPreflight_FailsWhenSolutionReferencesMissingProject()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-solution-project-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "FreeX.slnx"),
                """
                <Solution>
                  <Folder Name="/src/">
                    <Project Path="src/Included/Included.csproj" />
                    <Project Path="src/Missing/Missing.csproj" />
                  </Folder>
                </Solution>
                """);
            File.WriteAllText(Path.Combine(tempDirectory, "src", "Included", "Included.csproj"), "<Project />");

            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("Solution references missing project");
            (result.Output + result.Error).Should().Contain("src/Missing/Missing.csproj");
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
