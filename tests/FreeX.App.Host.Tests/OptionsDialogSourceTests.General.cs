using System.IO;
using System.Windows.Controls;
using System.Xml.Linq;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_RoundTripsPersistedGeneralUiOptions()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(FreeXOptions.OptionsPathEnvironmentVariable, path);

        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions
            {
                CollapseRibbonAutomatically = true,
                ShowScreenTips = false,
                SpellCheckCustomDictionaryWords = ["  TeH  ", "adn", "teh"]
            });
            dialog.Show();
            try
            {
                var collapseRibbon = GetControl<CheckBox>(dialog, "OptCollapseRibbon");
                var showScreenTips = GetControl<CheckBox>(dialog, "OptShowScreenTips");

                collapseRibbon.IsChecked.Should().BeTrue();
                showScreenTips.IsChecked.Should().BeFalse();

                collapseRibbon.IsChecked = false;
                showScreenTips.IsChecked = true;

                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.Result.CollapseRibbonAutomatically.Should().BeFalse();
                dialog.Result.ShowScreenTips.Should().BeTrue();
                dialog.Result.SpellCheckCustomDictionaryWords.Should().Equal("adn", "TeH");
            }
            finally
            {
                dialog.Close();
            }
        });

        var reloaded = FreeXOptions.LoadFromPath(path);
        reloaded.CollapseRibbonAutomatically.Should().BeFalse();
        reloaded.ShowScreenTips.Should().BeTrue();
        reloaded.SpellCheckCustomDictionaryWords.Should().Equal("adn", "TeH");
    }

    [Fact]
    public void OptionsDialog_UsesProofingEditorForCustomDictionaryWhenSavingOptions()
    {
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        source.Should().Contain("PdfExportLanguage = ExportPlanner.NormalizePdfLanguage(_opts.PdfExportLanguage)");
        source.Should().Contain("SpellCheckCustomDictionaryWords = FreeXOptions.NormalizeSpellCheckCustomDictionaryWords(_customDictionaryWords)");
        source.Should().NotContain("SpellCheckCustomDictionaryWords = FreeXOptions.NormalizeSpellCheckCustomDictionaryWords(_opts.SpellCheckCustomDictionaryWords)");
    }

    [Fact]
    public void OptionsDialog_ExposesPlainCategoryLabelsAndKeyboardAccessKeysForFieldsAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("OptionsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "ListBoxItem")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain([
                "General",
                "Formulas",
                "Proofing",
                "Save",
                "Language",
                "Ease of Access",
                "Advanced",
                "Customize Ribbon",
                "Quick Access Toolbar",
                "Add-ins",
                "Trust Center",
                "View"
            ]);

        AssertLabelTargets(document, presentation, "Default _font:", "OptDefaultFont");
        AssertLabelTargets(document, presentation, "Font _size:", "OptDefaultFontSize");
        AssertLabelTargets(document, presentation, "Include this many _sheets:", "OptSheetCount");
        AssertLabelTargets(document, presentation, "User _name:", "OptUserName");
        AssertLabelTargets(document, presentation, "Save files in this _format:", "OptDefaultFormat");
        AssertLabelTargets(document, presentation, "Recent files _location:", "OptRecentFilesPath");
        AssertLabelTargets(document, presentation, "App _language:", "OptAppLanguage");

        document.Descendants(presentation + "CheckBox")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain([
                "_Collapse the ribbon automatically",
                "Show feature descriptions in _ScreenTips",
                "Use _R1C1 reference style",
                "Enable _AutoComplete for cell values",
                "Show formula _bar",
                "Expand formula ba_r"
            ]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }

    [Fact]
    public void OptionsDialog_EaseOfAccessMatchesTheSharedWpfRowRhythm()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml");

        xaml.Should().Contain("x:Name=\"PanelEaseOfAccess\"");
        xaml.Should().Contain("Options_EaseOfAccessOptions");
        xaml.Should().Contain("Content=\"{local:Loc Key=Options_ProvideFeedbackWithSound}\"");
        xaml.Should().Contain("Content=\"{local:Loc Key=Options_ShowQuickAnalysisOptionsOnSelection}\"");
        xaml.Should().Contain("Content=\"{local:Loc Key=Options_OptimizeDisplayForAccessibility}\"");
        xaml.Should().Contain("Margin=\"0,0,0,6\" FontSize=\"12\"");
    }

    [Fact]
    public void OptionsDialog_DefaultFormatUsesNativeFreexWorkbookExtension()
    {
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        UiText.Get("Options_DefaultFormatJson").Should().Be("FreeX Workbook (.fxl)");
        source.Should().Contain("FreeXOptions.NormalizeDefaultFormat(_opts.DefaultFormat)");
        source.Should().Contain("FreeXOptions.FreeXWorkbookDefaultFormat");
        source.Should().NotContain("DefaultFormat == \".json\"");
        source.Should().NotContain("? \".json\"");
    }

    [Fact]
    public void OptionsDialogOpenedFromKeyboard_FocusesCategoryList()
    {
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        source.Should().Contain("Loaded += (_, _) =>");
        source.Should().Contain("FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("TabList.Focus();");
        source.Should().Contain("Keyboard.Focus(TabList);");
    }

    [Fact]
    public void OptionsDialog_ExposesStableAutomationMetadataForCategoriesAndActions()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");

        xaml.Should().Contain("AutomationProperties.Name=\"Options categories\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"OptionsCategoryList\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Select a FreeX Options category.\"");
        xaml.Should().Contain("x:Name=\"OkBtn\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"OptionsOkButton\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Apply FreeX Options changes.\"");
        xaml.Should().Contain("x:Name=\"CancelBtn\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"OptionsCancelButton\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Close FreeX Options without applying changes.\"");
    }

    [Fact]
    public void OptionsDialog_ExposesPersistedAppLanguageSwitcher()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var appSource = DialogSourceTestSupport.ReadHostSources("App.xaml.cs");

        xaml.Should().Contain("x:Name=\"PanelLanguage\"");
        xaml.Should().Contain("Choose display language");
        xaml.Should().Contain("x:Name=\"OptAppLanguage\"");
        xaml.Should().Contain("DisplayMemberPath=\"DisplayName\"");
        xaml.Should().Contain("SelectedValuePath=\"CultureName\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Select the display language FreeX uses for menus, dialogs, and messages.\"");
        xaml.Should().Contain("Some open windows may keep their current language until you restart FreeX.");

        source.Should().Contain("OptAppLanguage.ItemsSource = AppLanguageCatalog.GetAvailableLanguages()");
        source.Should().Contain("OptAppLanguage.SelectedValue = AppLanguageCatalog.NormalizeCultureName(_opts.AppLanguage)");
        source.Should().Contain("AppLanguage       = AppLanguageCatalog.NormalizeCultureName(OptAppLanguage.SelectedValue as string)");

        backstageSource.Should().Contain("AppLocalization.Bootstrap.ApplyAppLanguage(_options.AppLanguage)");
        backstageSource.Should().Contain("UiText.Get(\"Options_AppLanguageRestartMessage\")");
        appSource.Should().Contain("AppLocalization.Bootstrap.ApplyAppLanguage(options.AppLanguage);");
        appSource.Should().Contain("_startupOptions = options;");
        appSource.Should().Contain("ConfigureServices(serviceCollection);");
        appSource.Should().Contain("var options = _startupOptions ?? FreeXOptions.Load();");
        appSource.Should().NotContain("var options = Services.GetRequiredService<FreeXOptions>();");
    }

    [Fact]
    public void OptionsDialogInvalidGeneralInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        source.Should().Contain("OptionsInputParser.TryParseDefaultFontSize(OptDefaultFontSize.Text, out var defaultFontSize)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"Options_InvalidDefaultFontSizeMessage\"), OptDefaultFontSize);");
        source.Should().Contain("OptionsInputParser.TryParseDefaultSheetCount(OptSheetCount.Text, out var defaultSheetCount)");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"Options_InvalidSheetCountMessage\"), OptSheetCount);");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, Control target)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().NotContain("ParseDefaultFontSizeOrFallback");
        source.Should().NotContain("ParseDefaultSheetCountOrFallback");
    }

    [Fact]
    public void OptionsDialog_SurfacePersistenceFailuresInsteadOfClosingSilently()
    {
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        source.Should().Contain("if (!opts.Save())");
        source.Should().Contain("DialogMessageHelper.ShowError(this, opts.LastPersistenceError, Title);");
        source.Should().Contain("return;");
        source.Should().Contain("DialogResult = true;");
    }
}
