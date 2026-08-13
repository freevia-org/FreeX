using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Localization.Tests;

public sealed class SatelliteOutputInventoryTests
{
    private static readonly string[] SupportedCultures =
    [
        "bg-BG", "cs-CZ", "da-DK", "de-AT", "de-CH", "de-DE", "el-GR",
        "en-AU", "en-CA", "en-GB", "en-IE", "en-NZ", "en-ZA", "es-AR",
        "es-CL", "es-CO", "es-ES", "es-MX", "et-EE", "fi-FI", "fr-CA",
        "fr-FR", "ga-IE", "hr-HR", "hu-HU", "it-IT", "lt-LT", "lv-LV",
        "mt-MT", "nb-NO", "nl-BE", "nl-NL", "pl-PL", "pt-BR", "pt-PT",
        "ro-RO", "sk-SK", "sl-SI", "sr-Cyrl-RS", "sr-Latn-RS", "sv-SE",
        "tr-TR", "uk-UA"
    ];

    [Fact]
    public void NormalBuild_ContainsMatchingAppAndSharedSatelliteCultures() =>
        AppLocalizationContractTestSupport.AssertSatelliteOutputInventory(
            AppContext.BaseDirectory,
            "FreeX.App.Localization.resources.dll",
            SupportedCultures);

    [Fact]
    public void EnglishOnlyBuild_ContainsNoAppOrSharedSatelliteAssemblies()
    {
        var projectPath = TestWorkspaceFileLocator.Find(
            "src", "FreeX.App.Localization", "FreeX.App.Localization.csproj");
        using (var temporaryDirectory = new TestTemporaryDirectory("FreeXEnglishOnlyLocalization-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("--no-restore");
            startInfo.ArgumentList.Add("-p:FreeXTesterReleaseEnglishOnly=true");
            startInfo.ArgumentList.Add($"-p:BaseOutputPath={outputDirectory}\\");

            using var process = Process.Start(startInfo);
            process.Should().NotBeNull();
            process!.WaitForExit(120_000).Should().BeTrue();
            process.ExitCode.Should().Be(0, because: process.StandardError.ReadToEnd());

            Directory.Exists(outputDirectory).Should().BeTrue();
            Directory.EnumerateFiles(outputDirectory, "*.resources.dll", SearchOption.AllDirectories)
                .Should()
                .BeEmpty();
        }
    }
}
