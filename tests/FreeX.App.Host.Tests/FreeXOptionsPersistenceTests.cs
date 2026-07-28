using System.IO;
using System.Text.Json;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

public sealed class FreeXOptionsPersistenceTests : IDisposable
{
    private readonly TestTemporaryDirectory _temp = new();

    [Fact]
    public void LoadFromPath_WhenJsonIsInvalid_ReturnsDefaultsWithObservableError()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, "{ not-json");

        var options = FreeXOptions.LoadFromPath(path);

        options.DefaultFormat.Should().Be(".xlsx");
        options.LastPersistenceError.Should().Contain("Failed to load options");
        options.LastPersistenceError.Should().Contain(path);
    }

    [Fact]
    public void SaveToPath_WhenTargetCannotBeWritten_ReturnsFalseWithObservableError()
    {
        var options = new FreeXOptions();

        var saved = options.SaveToPath(_temp.Path);

        saved.Should().BeFalse();
        options.LastPersistenceError.Should().Contain("Failed to save options");
        options.LastPersistenceError.Should().Contain(_temp.Path);
    }

    [Fact]
    public void LoadFromPath_NormalizesLegacyJsonDefaultFormatToFreexWorkbook()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, """{ "DefaultFormat": ".json" }""");

        var options = FreeXOptions.LoadFromPath(path);

        options.DefaultFormat.Should().Be(FreeXOptions.FreeXWorkbookDefaultFormat);
    }

    [Fact]
    public void FillHandleAndCellDragAndDrop_RoundTripsThroughWpfOptionsBridge()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        var options = new FreeXOptions { EnableFillHandleAndCellDragAndDrop = false };

        options.SaveToPath(path).Should().BeTrue();
        FreeXOptions.LoadFromPath(path).EnableFillHandleAndCellDragAndDrop.Should().BeFalse();
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

        var options = FreeXOptions.LoadFromPath(path);

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

        var options = FreeXOptions.LoadFromPath(path);

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

        var options = FreeXOptions.LoadFromPath(path);

        options.DefaultFontSize.Should().Be(expectedFontSize);
    }

    [Fact]
    public void LoadFromPath_NormalizesUserName()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        File.WriteAllText(path, """{ "UserName": "  Analyst  " }""");

        var options = FreeXOptions.LoadFromPath(path);

        options.UserName.Should().Be("Analyst");
    }

    [Fact]
    public void SaveToPath_NormalizesDefaultFontOptions()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        var options = new FreeXOptions
        {
            DefaultFontName = "  Aptos  ",
            DefaultFontSize = 500
        };

        options.SaveToPath(path).Should().BeTrue();

        options.DefaultFontName.Should().Be("Aptos");
        options.DefaultFontSize.Should().Be(409);
        var reloaded = FreeXOptions.LoadFromPath(path);
        reloaded.DefaultFontName.Should().Be("Aptos");
        reloaded.DefaultFontSize.Should().Be(409);
    }

    [Fact]
    public void Save_WhenStorePathCannotBeWritten_ReturnsFalseWithObservableError()
    {
        using var optionsPath = TestEnvironmentVariableScope.Set(FreeXOptions.OptionsPathEnvironmentVariable, _temp.Path);
        var options = new FreeXOptions();

        options.Save().Should().BeFalse();

        options.LastPersistenceError.Should().Contain("Failed to save options");
        options.LastPersistenceError.Should().Contain(_temp.Path);
    }

    [Fact]
    public void ResolveStorePath_UsesApplicationDataPathProviderWhenOverrideIsMissing()
    {
        var provider = new TestApplicationDataPathProvider(_temp.Path);

        var path = FreeXOptions.ResolveStorePath(provider);

        path.Should().Be(Path.Combine(_temp.Path, "FreeX", "options.json"));
    }

    [Fact]
    public void ResolveStorePath_UsesEnvironmentOverrideWhenProvided()
    {
        var provider = new TestApplicationDataPathProvider(Path.Combine(_temp.Path, "ignored"));
        var overridePath = Path.Combine(_temp.Path, "custom-options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(FreeXOptions.OptionsPathEnvironmentVariable, overridePath);

        var path = FreeXOptions.ResolveStorePath(provider);

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

        var options = FreeXOptions.LoadFromPath(path);

        options.DefaultFormat.Should().Be(FreeXOptions.FreeXWorkbookDefaultFormat);
        options.DefaultFontName.Should().Be("Aptos");
        options.DefaultFontSize.Should().Be(FreeXOptions.MaxDefaultFontSize);
        options.DefaultSheetCount.Should().Be(FreeXOptions.MaxDefaultSheetCount);
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

        options.SaveToPath(_temp.Path).Should().BeFalse();
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
        Directory.EnumerateFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void SaveToPath_RoundTripsStatusBarOptions()
    {
        var path = Path.Combine(_temp.Path, "options.json");
        var options = new FreeXOptions
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

        options.SaveToPath(path).Should().BeTrue();

        var reloaded = FreeXOptions.LoadFromPath(path);
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
    public void FreeXOptions_CurrentDefaultsMatchPortableAppOptions()
    {
        var hostOptions = new FreeXOptions();
        var appOptions = new AppOptions();

        hostOptions.ToAppOptions().Should().BeEquivalentTo(appOptions);
        hostOptions.DefaultFontName.Should().Be(FreeXOptions.DefaultFontNameFallback);
        hostOptions.DefaultFontSize.Should().Be(FreeXOptions.DefaultFontSizeFallback);
        hostOptions.DefaultSheetCount.Should().Be(1);
        hostOptions.UserName.Should().Be(Environment.UserName);
        hostOptions.QuickAccessToolbarCommands.Should().Equal(QuickAccessToolbarCatalog.DefaultCommandIds);
        hostOptions.PdfExportLanguage.Should().Be(ExportPlanner.DefaultPdfLanguage);
        hostOptions.LastPersistenceError.Should().BeNull();
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
        var source = DialogSourceTestSupport.ReadHostSources("FreeXOptions.cs");

        source.Should().NotContain("Debug.WriteLine");
        source.Should().Contain(nameof(FreeXOptions.LastPersistenceError));
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
