using System.Diagnostics;
using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DotNetProjectReferencesPreflightTests
{
    [Fact]
    public void DotNetProjectReferencesPreflight_ValidatesProjectReferenceTargets()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-DotNetProjectReferences.ps1"));

        script.Should().Contain("Get-ChildItem -LiteralPath $resolvedProjectRoot -Filter \"*.csproj\" -File -Recurse");
        script.Should().Contain("*_wpftmp.csproj");
        script.Should().Contain("$segments -contains \".worktrees\"");
        script.Should().Contain("ProjectReference");
        script.Should().Contain("ProjectReference target escapes project root");
        script.Should().Contain("Missing ProjectReference target");
        script.Should().Contain("Validated ProjectReference targets for $($projectFiles.Count) .NET project file(s).");
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-DotNetProjectReferences.ps1");

        var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ProjectReference targets for ");
        result.Output.Should().Contain(".NET project file(s).");
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_IgnoresTransientAndNestedWorktreeProjects()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-project-reference-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, "src", "FreeX.App.Host"));
        Directory.CreateDirectory(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch"));

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "src", "FreeX.App.Host", "FreeX.App.Host.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(tempDirectory, "src", "FreeX.App.Host", "FreeX.App.Host_abc123_wpftmp.csproj"), "<Project><ItemGroup><ProjectReference Include=\"Missing.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(tempDirectory, ".worktrees", "agent", "src", "Scratch", "Scratch.csproj"), "<Project><ItemGroup><ProjectReference Include=\"Missing.csproj\" /></ItemGroup></Project>");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-DotNetProjectReferences.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\"");

            result.ExitCode.Should().Be(0, result.Error);
            result.Output.Should().Contain("Validated ProjectReference targets for 1 .NET project file(s).");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_FailsForReferenceOutsideProjectRoot()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-project-reference-preflight-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(tempDirectory, "repo");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(tempDirectory, "external"));

        try
        {
            File.WriteAllText(
                Path.Combine(projectRoot, "Broken.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\external\Outside.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(tempDirectory, "external", "Outside.csproj"), "<Project />");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-DotNetProjectReferences.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{projectRoot}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("target escapes project root");
            (result.Output + result.Error).Should().Contain("..\\external\\Outside.csproj");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void DotNetProjectReferencesPreflight_FailsForMissingProjectReferenceTarget()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-project-reference-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "Broken.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="Missing.csproj" />
                  </ItemGroup>
                </Project>
                """);
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-DotNetProjectReferences.ps1");

            var result = RunPowerShellScript(scriptPath, Path.GetTempPath(), $"-ProjectRoot \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("Project reference validation failed");
            (result.Output + result.Error).Should().Contain("Missing.csproj");
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
