using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class MsBuildLocalizationContractTests
{
    [Theory]
    [InlineData("C:/synthetic/freew/FreeW.App.Localization", "FreeW")]
    [InlineData("C:\\synthetic\\freew\\FreeW.App.Localization", "FreeW")]
    public void ProductDetection_NormalizesSlashAndBackslashPaths(string projectDirectory, string expectedProduct)
    {
        var properties = EvaluateProperties(projectDirectory);

        properties.GetProperty("FreeSharedLocalizationProduct").GetString()
            .Should().Be(expectedProduct);
        properties.GetProperty("FreeSharedLocalizationSupportedCultures").GetString()
            .Should().Be("fr-FR");
    }

    [Fact]
    public void SyntheticSupportedCultures_BuildDeclaredCulturePattern()
    {
        var properties = EvaluateProperties(
            "C:/synthetic/freew/FreeW.App.Localization",
            "fr-FR%3Bde-DE");

        properties.GetProperty("FreeSharedLocalizationSupportedCultures").GetString()
            .Should().Be("fr-FR;de-DE");
        properties.GetProperty("FreeSharedLocalizationSupportedCulturePattern").GetString()
            .Should().Be("fr-FR|de-DE");
    }

    [Fact]
    public void SourceContract_UsesNormalizedDirectoryAndDeclaredCulturePattern()
    {
        var props = TestWorkspaceFileLocator.ReadAllText("Directory.Build.props");
        var targets = TestWorkspaceFileLocator.ReadAllText("Directory.Build.targets");

        props.Should().Contain("FreeSharedLocalizationProjectDirectory");
        props.Should().Contain("Replace('\\','/')");
        props.Should().Contain("Contains('/freew/')");
        props.Should().Contain("Contains('/freep/')");
        props.Should().Contain("Contains('/src/')");
        props.Should().Contain("Contains('/tests/')");
        targets.Should().Contain("FreeSharedLocalizationSupportedCulturePattern");
        targets.Should().NotContain("DestinationSubDirectory)' == 'fr-FR\\");
        targets.Should().NotContain("fr-FR");
    }

    private static JsonElement EvaluateProperties(
        string projectDirectory,
        string? supportedCultures = null)
    {
        var projectPath = TestWorkspaceFileLocator.Find(
            "src", "FreeX.App.Localization", "FreeX.App.Localization.csproj");
        var arguments = new List<string>
        {
            "msbuild",
            projectPath,
            "-getProperty:FreeSharedLocalizationProduct,FreeSharedLocalizationSupportedCultures,FreeSharedLocalizationSupportedCulturePattern",
            $"-p:FreeSharedLocalizationProjectDirectory={projectDirectory}"
        };
        if (supportedCultures is not null)
            arguments.Add($"-p:FreeSharedLocalizationSupportedCultures={supportedCultures}");

        var result = RunDotnet(arguments);

        result.ExitCode.Should().Be(0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        return document.RootElement.GetProperty("Properties").Clone();
    }

    private static DotnetResult RunDotnet(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        process.Should().NotBeNull();
        process!.WaitForExit(120_000).Should().BeTrue();
        return new DotnetResult(
            process.ExitCode,
            process.StandardOutput.ReadToEnd(),
            process.StandardError.ReadToEnd());
    }

    private sealed record DotnetResult(int ExitCode, string StandardOutput, string StandardError);
}
