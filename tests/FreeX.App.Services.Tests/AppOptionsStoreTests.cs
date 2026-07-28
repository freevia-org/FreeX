using System.Text.Json;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class AppOptionsStoreTests
{
    [Fact]
    public void GetDefaultStorePath_UsesApplicationDataPathProvider()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(temp.Path);

        var path = AppOptionsStore.GetDefaultStorePath(provider);

        path.Should().Be(Path.Combine(temp.Path, "FreeX", "options.json"));
    }

    [Fact]
    public void ResolveStorePath_UsesExplicitOverrideWhenProvided()
    {
        using var temp = new TestTemporaryDirectory();
        var provider = new TestApplicationDataPathProvider(Path.Combine(temp.Path, "ignored"));
        var overridePath = Path.Combine(temp.Path, "custom-options.json");

        var path = AppOptionsStore.ResolveStorePath(provider, overridePath);

        path.Should().Be(overridePath);
    }

    [Fact]
    public void LoadFromPath_WithMissingOrFutureSchemaKeepsCurrentDefaultsAndKnownValues()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        File.WriteAllText(
            path,
            """
            {
              "DefaultFormat": ".json",
              "DefaultFontName": "  Aptos  ",
              "DefaultFontSize": 500,
              "DefaultSheetCount": 300,
              "UserName": "  Analyst  ",
              "SpellCheckCustomDictionaryWords": [ "  TeH  ", "adn", "teh", "" ],
              "QuickAccessToolbarCommands": null,
              "FutureSetting": true
            }
            """);

        var options = AppOptionsStore.LoadFromPath(path);

        options.DefaultFormat.Should().Be(AppOptions.FreeXWorkbookDefaultFormat);
        options.DefaultFontName.Should().Be("Aptos");
        options.DefaultFontSize.Should().Be(AppOptions.MaxDefaultFontSize);
        options.DefaultSheetCount.Should().Be(AppOptions.MaxDefaultSheetCount);
        options.UserName.Should().Be("Analyst");
        options.SpellCheckCustomDictionaryWords.Should().Equal("adn", "TeH");
        options.QuickAccessToolbarCommands.Should().Equal(AppOptions.DefaultQuickAccessToolbarCommands);
        options.ShowScreenTips.Should().BeTrue();
        options.AutoCalculate.Should().BeTrue();
        options.LastPersistenceError.Should().BeNull();
    }

    [Fact]
    public void LoadFromPath_WhenJsonIsInvalid_ReturnsDefaultsWithObservableError()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        File.WriteAllText(path, "{ not-json");

        var options = AppOptionsStore.LoadFromPath(path);

        options.DefaultFormat.Should().Be(AppOptions.XlsxDefaultFormat);
        options.LastPersistenceError.Should().Contain("Failed to load options");
        options.LastPersistenceError.Should().Contain(path);
    }

    [Fact]
    public void SaveToPath_WhenTargetCannotBeWritten_ReturnsFalseWithObservableErrorAndCleansTempFile()
    {
        using var temp = new TestTemporaryDirectory();
        var blockedPath = Path.Combine(temp.Path, "blocked-options");
        Directory.CreateDirectory(blockedPath);
        var options = new AppOptions();

        var saved = AppOptionsStore.SaveToPath(options, blockedPath);

        saved.Should().BeFalse();
        options.LastPersistenceError.Should().Contain("Failed to save options");
        options.LastPersistenceError.Should().Contain(blockedPath);
        Directory.EnumerateFileSystemEntries(temp.Path, ".blocked-options.*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void SaveToPath_WritesAtomicallyAndClearsPreviousError()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "nested", "options.json");
        var options = new AppOptions
        {
            DefaultFormat = ".fxl",
            AppLanguage = "uk-UA",
            CollapseRibbonAutomatically = true,
            ShowScreenTips = false,
            QuickAccessToolbarBelowRibbon = true,
            QuickAccessToolbarCommands = ["Open", "Save", "Print"],
            SpellCheckCustomDictionaryWords = ["  TeH  ", "adn", "teh", ""]
        };

        AppOptionsStore.SaveToPath(options, temp.Path).Should().BeFalse();
        AppOptionsStore.SaveToPath(options, path).Should().BeTrue();

        options.LastPersistenceError.Should().BeNull();
        JsonDocument.Parse(File.ReadAllText(path))
            .RootElement.GetProperty(nameof(AppOptions.DefaultFormat))
            .GetString()
            .Should()
            .Be(".fxl");
        Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(path);

        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.AppLanguage.Should().Be("uk-UA");
        reloaded.CollapseRibbonAutomatically.Should().BeTrue();
        reloaded.ShowScreenTips.Should().BeFalse();
        reloaded.QuickAccessToolbarBelowRibbon.Should().BeTrue();
        reloaded.QuickAccessToolbarCommands.Should().Equal("Open", "Save", "Print");
        reloaded.SpellCheckCustomDictionaryWords.Should().Equal("adn", "TeH");
    }

    [Fact]
    public void AppOptions_CurrentDefaultsMatchPersistedCompatibilitySchema()
    {
        var options = new AppOptions();

        options.DefaultFontName.Should().Be(AppOptions.DefaultFontNameFallback);
        options.DefaultFontSize.Should().Be(AppOptions.DefaultFontSizeFallback);
        options.DefaultSheetCount.Should().Be(1);
        options.UserName.Should().Be(Environment.UserName);
        options.CollapseRibbonAutomatically.Should().BeFalse();
        options.ShowScreenTips.Should().BeTrue();
        options.AppLanguage.Should().Be(AppOptions.SystemDefaultCultureName);
        options.SpellCheckCustomDictionaryWords.Should().BeEmpty();
        options.AutoCalculate.Should().BeTrue();
        options.UseR1C1ReferenceStyle.Should().BeFalse();
        options.ShowFormulaBar.Should().BeTrue();
        options.FormulaBarExpanded.Should().BeFalse();
        options.MoveSelectionAfterEnter.Should().BeTrue();
        options.AfterEnterDirection.Should().Be(AppOptionsEnterDirection.Down);
        options.EnableFillHandleAndCellDragAndDrop.Should().BeTrue();
        options.ShowGridlines.Should().BeTrue();
        options.ShowHeadings.Should().BeTrue();
        options.ObjectsDisplay.Should().Be(AppOptionsObjectDisplay.All);
        options.DefaultFormat.Should().Be(AppOptions.XlsxDefaultFormat);
        options.QuickAccessToolbarBelowRibbon.Should().BeFalse();
        options.QuickAccessToolbarCommands.Should().Equal(AppOptions.DefaultQuickAccessToolbarCommands);
        options.CrashAnalyticsEnabled.Should().BeFalse();
        options.CrashAnalyticsPrompted.Should().BeFalse();
        options.PdfExportLanguage.Should().Be(AppOptions.DefaultPdfExportLanguage);
        options.LastPersistenceError.Should().BeNull();
    }

    [Fact]
    public void FillHandleAndCellDragAndDrop_RoundTripsThroughSharedStore()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        var options = new AppOptions { EnableFillHandleAndCellDragAndDrop = false };

        AppOptionsStore.SaveToPath(options, path).Should().BeTrue();
        AppOptionsStore.LoadFromPath(path).EnableFillHandleAndCellDragAndDrop.Should().BeFalse();
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}
