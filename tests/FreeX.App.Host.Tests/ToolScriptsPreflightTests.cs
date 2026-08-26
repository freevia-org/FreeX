using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ToolScriptsPreflightTests
{
    [Fact]
    public void ToolScriptsPreflight_ParsesAllPowerShellTools()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-ToolScripts.ps1");

        script.Should().Contain("Get-ChildItem -LiteralPath $resolvedScriptDirectory -Filter \"*.ps1\" -File -Recurse");
        script.Should().Contain("Test-ToolExcludedPath");
        script.Should().Contain("[System.Management.Automation.Language.Parser]::ParseFile");
        script.Should().Contain("PowerShell syntax validation failed");
        script.Should().Contain("preflight scripts must set `$ErrorActionPreference = `\"Stop`\".");
        script.Should().Contain("PowerShell fail-fast validation failed");
        script.Should().Contain("Validated $($scripts.Count) PowerShell tool script(s).");
        script.Should().NotContain("chmod +x --");
    }

    [Fact]
    public void ToolProcessProbe_ResolvesIntermediateUnixSymlinksBeforeComparingWorkingDirectories()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-ToolScripts.ps1");

        script.Should().Contain("function Resolve-ExistingToolProcessPath");
        script.Should().Contain("$item.ResolveLinkTarget($true)");
        script.Should().Contain("New-Item -ItemType SymbolicLink -Path $workingDirectoryArgument -Target $workingRoot");
        script.Should().Contain("Resolve-ExistingToolProcessPath -Path $probe.WorkingDirectory");
        script.Should().Contain("Resolve-ExistingToolProcessPath -Path $workingDirectoryArgument");
        script.Should().Contain("$pathComparison = if ($isWindowsHost) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }");
        script.Should().Contain("$probe.First -cne \"first value\" -or $probe.Second -cne \"second value with spaces\"");
    }

    [Fact]
    public void ToolScriptsPreflight_FailsWhenPreflightScriptOmitsFailFastMode()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "Test-MissingFailFast.ps1"), "Write-Host \"ok\"");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-ToolScripts.ps1",
            $"-ScriptDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("PowerShell fail-fast validation failed");
        result.NormalizedCombinedOutput.Should().Contain("Test-MissingFailFast.ps1");
    }

    [Fact]
    public void ToolScriptsPreflight_FailsWhenNestedPreflightScriptOmitsFailFastMode()
    {
        using var temp = new TestTemporaryDirectory();
        var nestedDirectory = Path.Combine(temp.Path, "nested");
        Directory.CreateDirectory(nestedDirectory);

        File.WriteAllText(Path.Combine(nestedDirectory, "Test-NestedMissingFailFast.ps1"), "Write-Host \"ok\"");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-ToolScripts.ps1",
            $"-ScriptDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("PowerShell fail-fast validation failed");
        result.NormalizedCombinedOutput.Should().Contain("Test-NestedMissingFailFast.ps1");
    }

    [Fact]
    public void ToolScriptsPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-ToolScripts.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("PowerShell tool script(s).");
    }

    [Fact]
    public void ToolScriptsPreflight_FailsWhenScriptHasSyntaxError()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.ps1"), "param(`nif (`n");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-ToolScripts.ps1",
            $"-ScriptDirectory \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("PowerShell syntax validation failed");
    }

}
