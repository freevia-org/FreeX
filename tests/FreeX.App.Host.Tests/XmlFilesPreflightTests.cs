using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class XmlFilesPreflightTests
{
    [Fact]
    public void XmlFilesPreflight_ValidatesXmlBackedRepositoryFiles()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-XmlFiles.ps1"));

        script.Should().Contain("[string[]]$XmlRoots = @(\"Directory.Build.props\", \"FreeX.slnx\", \"FreeX.DefaultTests.slnx\", \"FreeX.UiTests.slnx\", \"src\", \"tests\")");
        script.Should().Contain("\".slnx\"");
        script.Should().Contain("[System.Xml.XmlReader]::Create");
        script.Should().Contain("XML validation failed");
        script.Should().Contain("Validated $($xmlFiles.Count) XML-backed file(s).");
    }

    [Fact]
    public void XmlFilesPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-XmlFiles.ps1");

        var result = PowerShellScriptRunner.Run(scriptPath, Path.GetTempPath(), "");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated ");
        result.Output.Should().Contain("XML-backed file(s).");
    }

    [Fact]
    public void XmlFilesPreflight_FailsWhenSolutionXmlIsMalformed()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-xml-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "broken.slnx"), "<Solution><Folder></Solution>");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-XmlFiles.ps1");

            var result = PowerShellScriptRunner.Run(scriptPath, Path.GetTempPath(), $"-XmlRoots \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("XML validation failed");
            (result.Output + result.Error).Should().Contain("broken.slnx");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void XmlFilesPreflight_FailsWhenXmlIsMalformed()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "freex-xml-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, "broken.xaml"), "<Window><Grid></Window>");
            var scriptPath = WorkspaceFileLocator.Find("tools", "Test-XmlFiles.ps1");

            var result = PowerShellScriptRunner.Run(scriptPath, Path.GetTempPath(), $"-XmlRoots \"{tempDirectory}\"");

            result.ExitCode.Should().NotBe(0);
            (result.Output + result.Error).Should().Contain("XML validation failed");
            (result.Output + result.Error).Should().Contain("broken.xaml");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

}
