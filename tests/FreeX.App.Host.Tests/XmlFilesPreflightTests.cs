using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class XmlFilesPreflightTests
{
    [Fact]
    public void XmlFilesPreflight_ValidatesXmlBackedRepositoryFiles()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Test-XmlFiles.ps1");

        script.Should().Contain("[string[]]$XmlRoots = @(\"Directory.Build.props\", \"FreeX.slnx\", \"FreeX.DefaultTests.slnx\", \"FreeX.UiTests.slnx\", \"src\", \"tests\")");
        script.Should().Contain("\".slnx\"");
        script.Should().Contain("[System.Xml.XmlReader]::Create");
        script.Should().Contain("XML validation failed");
        script.Should().Contain("Validated $($xmlFiles.Count) XML-backed file(s).");
    }

    [Fact]
    public void XmlFilesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory("Test-XmlFiles.ps1");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("XML-backed file(s).");
    }

    [Fact]
    public void XmlFilesPreflight_FailsWhenSolutionXmlIsMalformed()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.slnx"), "<Solution><Folder></Solution>");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-XmlFiles.ps1",
            $"-XmlRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("XML validation failed");
        result.CombinedOutput.Should().Contain("broken.slnx");
    }

    [Fact]
    public void XmlFilesPreflight_FailsWhenXmlIsMalformed()
    {
        using var temp = new TestTemporaryDirectory();

        File.WriteAllText(Path.Combine(temp.Path, "broken.xaml"), "<Window><Grid></Window>");

        var result = PowerShellScriptRunner.RunToolScriptFromTemporaryWorkingDirectory(
            "Test-XmlFiles.ps1",
            $"-XmlRoots \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        result.CombinedOutput.Should().Contain("XML validation failed");
        result.CombinedOutput.Should().Contain("broken.xaml");
    }

}
