using Free.Shared.AppServices;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Presentation.Tests.Options;

public sealed class FreeWOptionsPlannerTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new FreeWOptions();

        options.RecentFilesCap.Should().Be(FreeWOptions.DefaultRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        options.UiLanguage.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_ClampsRecentFilesAndDefaultsFormat()
    {
        var options = new FreeWOptions
        {
            RecentFilesCap = 9999,
            DefaultSaveFormat = " ",
            UiLanguage = "  en-GB  ",
        };

        options.Normalize();

        options.RecentFilesCap.Should().Be(ApplicationOptionsNormalizer.MaxRecentFilesCap);
        options.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        options.UiLanguage.Should().Be("en-GB");
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("15", 15)]
    [InlineData(" 7 ", 7)]
    public void TryParseRecentFilesCap_AcceptsInRange(string text, int expected)
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(text, out var cap).Should().BeTrue();
        cap.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("99999")]
    [InlineData("3.5")]
    public void TryParseRecentFilesCap_RejectsInvalidOrOutOfRange(string text)
    {
        OptionsDialogPlanner.TryParseRecentFilesCap(text, out _).Should().BeFalse();
    }

    [Fact]
    public void BuildResult_NormalizesAndPreservesAutoCorrectObjects()
    {
        var autoFormat = AutoFormatOptions.Default with { Hyperlinks = false };
        var autoCorrect = new AutoCorrectOptions { ReplaceText = false };

        var result = OptionsDialogPlanner.BuildResult(
            recentFilesCap: 4,
            format: " ",
            uiLanguage: "  uk-UA  ",
            autoCorrectEnabled: false,
            autoFormat: autoFormat,
            autoCorrect: autoCorrect);

        result.RecentFilesCap.Should().Be(4);
        result.DefaultSaveFormat.Should().Be(FreeWOptions.DocxDefaultFormat);
        result.UiLanguage.Should().Be("uk-UA");
        result.AutoCorrectEnabled.Should().BeFalse();
        result.AutoFormat.Hyperlinks.Should().BeFalse();
        result.AutoCorrect.ReplaceText.Should().BeFalse();
    }

    [Fact]
    public void OptionsModelAndPlanner_LiveInPresentationNotWpfHost()
    {
        var repoRoot = FindRepoRoot();

        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Presentation", "Options", "FreeWOptions.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Presentation", "Options", "OptionsDialogPlanner.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Host", "FreeWOptions.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(repoRoot, "freew", "FreeW.App.Host", "OptionsDialogPlanner.cs"))
            .Should().BeFalse();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
