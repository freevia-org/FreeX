using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FreeXOptionsPersistenceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "FreeXOptionsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadFromPath_WhenJsonIsInvalid_ReturnsDefaultsWithObservableError()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "options.json");
        File.WriteAllText(path, "{ not-json");

        var options = FreeXOptions.LoadFromPath(path);

        options.DefaultFormat.Should().Be(".xlsx");
        options.LastPersistenceError.Should().Contain("Failed to load options");
        options.LastPersistenceError.Should().Contain(path);
    }

    [Fact]
    public void SaveToPath_WhenTargetCannotBeWritten_ReturnsFalseWithObservableError()
    {
        Directory.CreateDirectory(_tempDirectory);
        var options = new FreeXOptions();

        var saved = options.SaveToPath(_tempDirectory);

        saved.Should().BeFalse();
        options.LastPersistenceError.Should().Contain("Failed to save options");
        options.LastPersistenceError.Should().Contain(_tempDirectory);
    }

    [Fact]
    public void Save_WhenStorePathCannotBeWritten_ReturnsFalseWithObservableError()
    {
        Directory.CreateDirectory(_tempDirectory);
        var previousPath = Environment.GetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable);
        Environment.SetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable, _tempDirectory);
        try
        {
            var options = new FreeXOptions();

            options.Save().Should().BeFalse();

            options.LastPersistenceError.Should().Contain("Failed to save options");
            options.LastPersistenceError.Should().Contain(_tempDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable, previousPath);
        }
    }

    [Fact]
    public void SaveToPath_WritesAtomicallyAndClearsPreviousError()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "options.json");
        var options = new FreeXOptions
        {
            DefaultFormat = ".fxl",
            AppLanguage = "uk-UA",
            CollapseRibbonAutomatically = true,
            ShowScreenTips = false,
            QuickAccessToolbarBelowRibbon = true,
            QuickAccessToolbarCommands =
            [
                QuickAccessToolbarCommandIds.Open,
                QuickAccessToolbarCommandIds.Save,
                QuickAccessToolbarCommandIds.Print
            ],
            SpellCheckCustomDictionaryWords = ["  TeH  ", "adn", "teh", ""]
        };

        options.SaveToPath(_tempDirectory).Should().BeFalse();
        options.SaveToPath(path).Should().BeTrue();

        options.LastPersistenceError.Should().BeNull();
        JsonDocument.Parse(File.ReadAllText(path))
            .RootElement.GetProperty(nameof(FreeXOptions.DefaultFormat))
            .GetString()
            .Should()
            .Be(".fxl");
        FreeXOptions.LoadFromPath(path)
            .AppLanguage
            .Should()
            .Be("uk-UA");
        var reloaded = FreeXOptions.LoadFromPath(path);
        reloaded.CollapseRibbonAutomatically.Should().BeTrue();
        reloaded.ShowScreenTips.Should().BeFalse();
        reloaded.QuickAccessToolbarBelowRibbon.Should().BeTrue();
        reloaded.QuickAccessToolbarCommands.Should().Equal(
            QuickAccessToolbarCommandIds.Open,
            QuickAccessToolbarCommandIds.Save,
            QuickAccessToolbarCommandIds.Print);
        reloaded.SpellCheckCustomDictionaryWords.Should().Equal("adn", "TeH");
        Directory.EnumerateFiles(_tempDirectory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void QuickAccessToolbarCatalog_NormalizesDuplicatesUnknownCommandsAndEmptyLists()
    {
        QuickAccessToolbarCatalog.NormalizeCommandIds(
        [
            QuickAccessToolbarCommandIds.Save,
            "missing-command",
            QuickAccessToolbarCommandIds.Save,
            QuickAccessToolbarCommandIds.Print
        ]).Should().Equal(
            QuickAccessToolbarCommandIds.Save,
            QuickAccessToolbarCommandIds.Print);

        QuickAccessToolbarCatalog.NormalizeCommandIds([])
            .Should()
            .Equal(QuickAccessToolbarCatalog.DefaultCommandIds);
    }

    [Fact]
    public void FreeXOptions_DoesNotUseDebugWriteLineForPersistenceFailures()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FreeXOptions.cs"));

        source.Should().NotContain("Debug.WriteLine");
        source.Should().Contain(nameof(FreeXOptions.LastPersistenceError));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
