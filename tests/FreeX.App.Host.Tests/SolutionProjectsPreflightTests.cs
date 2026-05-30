using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SolutionProjectsPreflightTests
{
    [Fact]
    public void SolutionProjectsPreflight_ValidatesSolutionMembership()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1"));

        script.Should().Contain("FreeX.slnx");
        script.Should().Contain("SelectNodes(\"//*[local-name()='Project']\")");
        script.Should().Contain("*_wpftmp.csproj");
        script.Should().Contain("$segments -contains \".worktrees\"");
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
    public void SolutionProjectsPreflight_RecognizesNestedSolutionFolders()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-solution-project-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Nested"));

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "FreeX.slnx"),
                """
                <Solution>
                  <Folder Name="/src/">
                    <Folder Name="/src/Nested/">
                      <Project Path="src/Nested/Nested.csproj" />
                    </Folder>
                  </Folder>
                </Solution>
                """);
            File.WriteAllText(Path.Combine(tempDirectory, "src", "Nested", "Nested.csproj"), "<Project />");

            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

            result.ExitCode.Should().Be(0, result.Error);
            result.Output.Should().Contain("Validated 1 solution project entry(s).");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void SolutionProjectsPreflight_IgnoresTransientAndNestedWorktreeProjects()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-solution-project-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "Included"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "FreeX.App.Host"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch"));

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
            File.WriteAllText(Path.Combine(tempDirectory, "src", "FreeX.App.Host", "FreeX.App.Host_abc123_wpftmp.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch", "Scratch.csproj"), "<Project />");

            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-SolutionProjects.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\" -SolutionPath \"{Path.Combine(tempDirectory, "FreeX.slnx")}\"");

            result.ExitCode.Should().Be(0, result.Error);
            result.Output.Should().Contain("Validated 1 solution project entry(s).");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
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

            var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
            result.ExitCode.Should().NotBe(0);
            combinedOutput.Should().Contain("missing from solution");
            combinedOutput.Should().Contain("src/Missing/Missing.csproj");
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

            var combinedOutput = NormalizeWhitespace(result.Output + result.Error);
            result.ExitCode.Should().NotBe(0);
            combinedOutput.Should().Contain("references missing project");
            combinedOutput.Should().Contain("src/Missing/Missing.csproj");
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

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, "\\s+", " ");

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}
