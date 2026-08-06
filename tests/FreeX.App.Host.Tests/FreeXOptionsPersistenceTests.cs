using System.IO;
using System.Text.Json;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

public sealed class AppOptionsPersistenceTests : IDisposable
{
    private readonly TestTemporaryDirectory _temp = new();

    [Fact]
    public void LoadFromPath_WhenJsonIsInvalid_ReturnsDefaultsWithObservableError()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, "{ not-json");

        var options = AppOptionsStore.LoadFromPath(path);

        options.DefaultFormat.Should().Be(".xlsx");
        options.LastPersistenceError.Should().Contain("Failed to load options");
        options.LastPersistenceError.Should().Contain(path);
    }

    [Fact]
    public void SaveToPath_WhenTargetCannotBeWritten_ReturnsFalseWithObservableError()
    {
        var options = new AppOptions();

        var saved = AppOptionsStore.SaveToPath(options, _temp.Path);

        saved.Should().BeFalse();
        options.LastPersistenceError.Should().Contain("Failed to save options");
        options.LastPersistenceError.Should().Contain(_temp.Path);
    }

    [Fact]
    public void LoadFromPath_NormalizesLegacyJsonDefaultFormatToFreexWorkbook()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, """{ "DefaultFormat": ".json" }""");

        var options = AppOptionsStore.LoadFromPath(path);

        options.DefaultFormat.Should().Be(AppOptions.FreeXWorkbookDefaultFormat);
    }

    [Fact]
    public void FillHandleAndCellDragAndDrop_RoundTripsThroughWpfOptionsBridge()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        var options = new AppOptions { EnableFillHandleAndCellDragAndDrop = false };

        AppOptionsStore.SaveToPath(options, path).Should().BeTrue();
        AppOptionsStore.LoadFromPath(path).EnableFillHandleAndCellDragAndDrop.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(12, 12)]
    [InlineData(300, 255)]
    public void LoadFromPath_NormalizesDefaultSheetCountToExcelOptionsRange(
        int persistedSheetCount,
        int expectedSheetCount)
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, $$"""{ "DefaultSheetCount": {{persistedSheetCount}} }""");

        var options = AppOptionsStore.LoadFromPath(path);

        options.DefaultSheetCount.Should().Be(expectedSheetCount);
    }

    [Theory]
    [InlineData("", "Calibri")]
    [InlineData("  Aptos  ", "Aptos")]
    public void LoadFromPath_NormalizesDefaultFontName(
        string persistedFontName,
        string expectedFontName)
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, $$"""{ "DefaultFontName": "{{persistedFontName}}" }""");

        var options = AppOptionsStore.LoadFromPath(path);

        options.DefaultFontName.Should().Be(expectedFontName);
    }

    [Theory]
    [InlineData(0, 11)]
    [InlineData(14, 14)]
    [InlineData(500, 409)]
    public void LoadFromPath_NormalizesDefaultFontSizeToSupportedRange(
        int persistedFontSize,
        int expectedFontSize)
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, $$"""{ "DefaultFontSize": {{persistedFontSize}} }""");

        var options = AppOptionsStore.LoadFromPath(path);

        options.DefaultFontSize.Should().Be(expectedFontSize);
    }

    [Fact]
    public void LoadFromPath_NormalizesUserName()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, """{ "UserName": "  Analyst  " }""");

        var options = AppOptionsStore.LoadFromPath(path);

        options.UserName.Should().Be("Analyst");
    }

    [Fact]
    public void SaveToPath_NormalizesDefaultFontOptions()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        var options = new AppOptions
        {
            DefaultFontName = "  Aptos  ",
            DefaultFontSize = 500
        };

        AppOptionsStore.SaveToPath(options, path).Should().BeTrue();

        options.DefaultFontName.Should().Be("Aptos");
        options.DefaultFontSize.Should().Be(409);
        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.DefaultFontName.Should().Be("Aptos");
        reloaded.DefaultFontSize.Should().Be(409);
    }

    [Fact]
    public void Save_WhenStorePathCannotBeWritten_ReturnsFalseWithObservableError()
    {
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, _temp.Path);
        var options = new AppOptions();

        AppOptionsStore.Save(options).Should().BeFalse();

        options.LastPersistenceError.Should().Contain("Failed to save options");
        options.LastPersistenceError.Should().Contain(_temp.Path);
    }

    [Fact]
    public void ResolveStorePath_UsesApplicationDataPathProviderWhenOverrideIsMissing()
    {
        var provider = new TestApplicationDataPathProvider(_temp.Path);

        var path = AppOptionsStore.ResolveStorePath(provider);

        path.Should().Be(Path.Combine(_temp.Path, "FreeX", "options.json"));
    }

    [Fact]
    public void ResolveStorePath_UsesEnvironmentOverrideWhenProvided()
    {
        var provider = new TestApplicationDataPathProvider(Path.Combine(_temp.Path, "ignored"));
        var overridePath = Path.Combine(_temp.Path, "custom-options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, overridePath);

        var path = AppOptionsStore.ResolveStorePath(provider);

        path.Should().Be(overridePath);
    }

    [Fact]
    public void LoadFromPath_WithMissingOrFutureSchemaKeepsCurrentDefaultsAndKnownValues()
    {
        var path = Path.Combine(_temp.Path, "options.json");
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
        options.QuickAccessToolbarCommands.Should().Equal(
            QuickAccessToolbarCommandIds.Save,
            QuickAccessToolbarCommandIds.Undo,
            QuickAccessToolbarCommandIds.Redo);
        options.ShowScreenTips.Should().BeTrue();
        options.AutoCalculate.Should().BeTrue();
        options.LastPersistenceError.Should().BeNull();
    }

    [Fact]
    public void SaveToPath_WritesAtomicallyAndClearsPreviousError()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        var options = new AppOptions
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

        AppOptionsStore.SaveToPath(options, _temp.Path).Should().BeFalse();
        AppOptionsStore.SaveToPath(options, path).Should().BeTrue();

        options.LastPersistenceError.Should().BeNull();
        JsonDocument.Parse(File.ReadAllText(path))
            .RootElement.GetProperty(nameof(AppOptions.DefaultFormat))
            .GetString()
            .Should()
            .Be(".fxl");
        AppOptionsStore.LoadFromPath(path)
            .AppLanguage
            .Should()
            .Be("uk-UA");
        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.CollapseRibbonAutomatically.Should().BeTrue();
        reloaded.ShowScreenTips.Should().BeFalse();
        reloaded.QuickAccessToolbarBelowRibbon.Should().BeTrue();
        reloaded.QuickAccessToolbarCommands.Should().Equal(
            QuickAccessToolbarCommandIds.Open,
            QuickAccessToolbarCommandIds.Save,
            QuickAccessToolbarCommandIds.Print);
        reloaded.SpellCheckCustomDictionaryWords.Should().Equal("adn", "TeH");
        Directory.EnumerateFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void SaveToPath_RoundTripsStatusBarOptions()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        var options = new AppOptions
        {
            StatusBarShowCellMode = false,
            StatusBarShowEndMode = true,
            StatusBarShowSelectionMode = true,
            StatusBarShowPageNumber = true,
            StatusBarShowAverage = false,
            StatusBarShowCount = false,
            StatusBarShowNumericalCount = true,
            StatusBarShowMinimum = true,
            StatusBarShowMaximum = true,
            StatusBarShowSum = false,
            StatusBarShowViewShortcuts = false,
            StatusBarShowZoom = false,
            StatusBarShowZoomSlider = false
        };

        AppOptionsStore.SaveToPath(options, path).Should().BeTrue();

        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.StatusBarShowCellMode.Should().BeFalse();
        reloaded.StatusBarShowEndMode.Should().BeTrue();
        reloaded.StatusBarShowSelectionMode.Should().BeTrue();
        reloaded.StatusBarShowPageNumber.Should().BeTrue();
        reloaded.StatusBarShowAverage.Should().BeFalse();
        reloaded.StatusBarShowCount.Should().BeFalse();
        reloaded.StatusBarShowNumericalCount.Should().BeTrue();
        reloaded.StatusBarShowMinimum.Should().BeTrue();
        reloaded.StatusBarShowMaximum.Should().BeTrue();
        reloaded.StatusBarShowSum.Should().BeFalse();
        reloaded.StatusBarShowViewShortcuts.Should().BeFalse();
        reloaded.StatusBarShowZoom.Should().BeFalse();
        reloaded.StatusBarShowZoomSlider.Should().BeFalse();
    }

    [Fact]
    public void AppOptions_CurrentDefaultsAreOwnedByPortableModel()
    {
        var options = new AppOptions();

        options.DefaultFontName.Should().Be(AppOptions.DefaultFontNameFallback);
        options.DefaultFontSize.Should().Be(AppOptions.DefaultFontSizeFallback);
        options.DefaultSheetCount.Should().Be(1);
        options.UserName.Should().Be(Environment.UserName);
        options.QuickAccessToolbarCommands.Should().Equal(QuickAccessToolbarCatalog.DefaultCommandIds);
        options.PdfExportLanguage.Should().Be(ExportPlanner.DefaultPdfLanguage);
        options.LastPersistenceError.Should().BeNull();

        File.Exists(Path.Combine(
                WorkspaceFileLocator.FindWorkspaceRoot(),
                "src",
                "FreeX.App.Host",
                "FreeXOptions.cs"))
            .Should().BeFalse();
        DialogSourceTestSupport.ReadHostSources("App.xaml.cs", "MainWindow.xaml.cs")
            .Should().NotContain("FreeXOptions");
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
    public void AppOptions_DoesNotUseDebugWriteLineForPersistenceFailures()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "AppOptions.cs");

        source.Should().NotContain("Debug.WriteLine");
        source.Should().Contain(nameof(AppOptions.LastPersistenceError));
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private sealed class TestApplicationDataPathProvider(string path) : IApplicationDataPathProvider
    {
        public string GetApplicationDataDirectory() => path;
    }
}
