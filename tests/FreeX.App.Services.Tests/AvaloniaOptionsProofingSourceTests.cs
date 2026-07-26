using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaOptionsProofingSourceTests
{
    [Fact]
    public void OptionsSource_ExposesQatAndProofingContractsWithoutRenderingIgnoreNumbers()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("QuickAccessToolbarAvailableCommandsList");
        source.Should().Contain("QuickAccessToolbarSelectedCommandsList");
        source.Should().Contain("QuickAccessToolbarCommandSearchBox");
        source.Should().Contain("QuickAccessToolbarImportCustomizationMenuItem");
        source.Should().Contain("QuickAccessToolbarExportCustomizationMenuItem");
        source.Should().Contain("KeyModifiers.Control) && args.Key == Key.Up");
        source.Should().Contain("args.Key is Key.Delete or Key.Back");
        source.Should().Contain("ProofingCustomDictionaryWordsList");
        source.Should().Contain("ProofingCustomDictionaryAddWordButton");
        source.Should().Contain("ProofingCustomDictionaryRemoveWordButton");
        source.Should().Contain("ProofingCustomDictionaryClearWordsButton");
        source.Should().Contain("current.ProofingIgnoreUppercase");
        source.Should().Contain("current.ProofingIgnoreNumbers");
        source.Should().Contain("projected.QuickAccessToolbarBelowRibbon");
        source.Should().Contain("projected.QuickAccessToolbarCommands");
        source.Should().Contain("projected.SpellCheckCustomDictionaryWords");
        source.Should().Contain("_avaloniaQuickAccessOptions = AppOptionsStore.Load();");
        source.Should().Contain("RebuildAvaloniaQuickAccessToolbar();");
        source.Should().Contain("UiText.Get(\"DeferredCommand_AutoCorrectOptions_Body\")");
        source.Should().Contain("var selectedAvailableId = (quickAccessAvailableList.SelectedItem as OptionsQuickAccessCommandChoice)?.Id;");
        source.Should().Contain("var selectedCommandId = (quickAccessSelectedList.SelectedItem as OptionsQuickAccessCommandChoice)?.Id;");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.FilePickerPatterns");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.ImportMenuHeader.TrimStart('_')");
        source.Should().Contain("Options_QuickAccessAddCommandHelpText");
        source.Should().Contain("Options_QuickAccessImportExportHelpText");
        source.Should().Contain("catch (Exception ex)");
        source.Should().Contain("SpellCheckWorkflowPlanner.RemoveCustomDictionaryWordAndSelectNext");
        var initialRefresh = source.IndexOf("RefreshQuickAccessLists();", StringComparison.Ordinal);
        initialRefresh.Should().BeGreaterThanOrEqualTo(0);
        source[initialRefresh..].Should().Contain("var quickAccessPanel");
        source.Should().Contain("OptionsSectionHeader(OptionsText(\"Options_CustomDictionary\"), topMargin: 26)");
        source.Should().NotContain("Options_IgnoreNumbers");
    }
}
