using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaOptionsProofingSourceTests
{
    [Fact]
    public void OptionsSource_UsesWpfQuickAccessToolbarFrameAndSharedChrome()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
        var wpf = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, OptionsDialogChromeStyle);");
        source.Should().Contain("OptionsDialogPlanner.CategoryItemHorizontalPadding");
        source.Should().Contain("OptionsDialogPlanner.CategoryItemVerticalPadding");
        source.Should().Contain("BorderThickness = new Thickness(1)");
        source.Should().Contain("Brush(160, 160, 160)");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"128,10,92,10,127,10,92\")");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"Auto,Auto,180\")");
        source.Should().Contain("Margin = new Thickness(10, 0)");
        source.Should().Contain("Margin = new Thickness(10, 0, 0, 0)");
        source.Should().Contain("BorderThickness = new Thickness(0, 1, 0, 0)");
        source.Should().Contain("OptionsDialogPlanner.FooterPaddingHorizontal");
        source.Should().Contain("OptionsDialogPlanner.FooterPaddingVertical");
        source.Should().Contain("ApplyOptionsButtonChrome(okButton, OptionsDialogPlanner.FooterButtonWidth, isDefault: true);");
        source.Should().Contain("ApplyOptionsButtonChrome(cancelButton, OptionsDialogPlanner.FooterButtonWidth);");
        source.Should().Contain("listBox.FontFamily = OptionsDialogChromeStyle.FontFamily;");

        wpf.Should().Contain("Value=\"16,9\"");
        wpf.Should().Contain("BorderBrush\" Value=\"#A0A0A0\"");
        wpf.Should().Contain("<RowDefinition Height=\"180\"/>");
        wpf.Should().Contain("BorderThickness=\"0,1,0,0\"");
        wpf.Should().Contain("Padding=\"16,10\"");
        wpf.Should().Contain("Width=\"80\" Height=\"26\"");
    }

    [Fact]
    public void OptionsSource_ExposesQatAndProofingContractsWithoutRenderingIgnoreNumbers()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("QuickAccessToolbarAvailableCommandsList");
        source.Should().Contain("QuickAccessToolbarSelectedCommandsList");
        source.Should().Contain("QuickAccessToolbarCommandSearchBox");
        source.Should().Contain("QuickAccessToolbarImportCustomizationMenuItem");
        source.Should().Contain("QuickAccessToolbarExportCustomizationMenuItem");
        source.Should().Contain("args.KeyModifiers.HasFlag(KeyModifiers.Control) &&");
        source.Should().Contain("args.Key == Key.Up");
        source.Should().Contain("args.Key is Key.Delete or Key.Back");
        source.Should().Contain("ProofingCustomDictionaryWordsList");
        source.Should().Contain("ProofingCustomDictionaryAddWordButton");
        source.Should().Contain("ProofingCustomDictionaryRemoveWordButton");
        source.Should().Contain("ProofingCustomDictionaryClearWordsButton");
        source.Should().Contain("current.ProofingIgnoreUppercase");
        source.Should().Contain("current.ProofingIgnoreNumbers");
        source.Should().Contain("quickAccessSession.SetPlacement(quickAccessBelowRibbonBox.IsChecked == true);");
        source.Should().Contain("quickAccessToolbarBelowRibbon: quickAccessBelowRibbonBox.IsChecked == true");
        source.Should().Contain("optionsDialogSession.Commit(");
        source.Should().Contain("_avaloniaQuickAccessOptions = current;");
        source.Should().Contain("RebuildAvaloniaQuickAccessToolbar();");
        source.Should().Contain("DeferredCommandMessagePlanner.AutoCorrectOptions()");
        source.Should().Contain("DeferredCommandMessageResolver.Resolve(");
        source.Should().Contain("var selectedAvailableId = (quickAccessAvailableList.SelectedItem as OptionsQuickAccessCommandChoice)?.Id;");
        source.Should().Contain("var selectedCommandId = (quickAccessSelectedList.SelectedItem as OptionsQuickAccessCommandChoice)?.Id;");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.FilePickerPatterns");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.ImportMenuHeader.TrimStart('_')");
        source.Should().Contain("Options_QuickAccessAddCommandHelpText");
        source.Should().Contain("Options_QuickAccessImportExportHelpText");
        source.Should().Contain("catch (Exception ex)");
        source.Should().Contain("var customDictionaryEditor = optionsDialogSession.CustomDictionary;");
        source.Should().Contain("customDictionaryEditor.RemoveSelectedWord();");
        var initialRefresh = source.IndexOf("RefreshQuickAccessLists();", StringComparison.Ordinal);
        initialRefresh.Should().BeGreaterThanOrEqualTo(0);
        source[initialRefresh..].Should().Contain("var quickAccessPanel");
        source.Should().Contain("OptionsSectionHeader(OptionsText(\"Options_CustomDictionary\"), topMargin: 30, bottomMargin: 8)");
        source.Should().NotContain("Options_IgnoreNumbers");
    }
}
