using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class JsonFilesPreflightTests
{
    [Fact]
    public void JsonFilesPreflight_ValidatesTrackedJsonFiles()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-JsonFiles.ps1");

        script.Should().Contain("[string[]]$JsonRoots = @(\"global.json\", \"docs\", \"release\")");
        script.Should().Contain("JSON path was not found");
        script.Should().Contain("$rootItem -is [System.IO.FileInfo]");
        script.Should().Contain("ConvertFrom-Json");
        script.Should().Contain("JSON validation failed");
        script.Should().Contain("Validated $($jsonFiles.Count) JSON file(s).");
    }

    [Fact]
    public void JsonFilesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-JsonFiles.ps1");
        using var workingDirectory = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("JSON file(s).");
    }

    [Fact]
    public void JsonFilesPreflight_FailsWhenJsonIsMalformed()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.json"), "{ \"name\": ");
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-JsonFiles.ps1");
        using var workingDirectory = new TestTemporaryDirectory();

        var result = PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, $"-JsonRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("JSON validation failed");
        (result.Output + result.Error).Should().Contain("broken.json");
    }

}
