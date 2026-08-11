using System.IO;
using System.Windows.Controls;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_ExposesProofingCustomDictionaryControlsAndAutomationMetadata()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        xaml.Should().Contain("Custom dictionary");
        xaml.Should().Contain("x:Name=\"ProofingCustomDictionaryWordsList\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Custom dictionary words\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ProofingCustomDictionaryWordsList\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Lists words that FreeX spell check treats as correct.\"");
        xaml.Should().Contain("x:Name=\"ProofingCustomDictionaryWordBox\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Custom dictionary word\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ProofingCustomDictionaryWordBox\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Enter a word to add to the custom dictionary.\"");
        xaml.Should().Contain("x:Name=\"ProofingCustomDictionaryAddWordButton\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Add custom dictionary word\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ProofingCustomDictionaryAddWordButton\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Add the typed word to the custom dictionary.\"");
        xaml.Should().Contain("x:Name=\"ProofingCustomDictionaryRemoveWordButton\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Remove custom dictionary word\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ProofingCustomDictionaryRemoveWordButton\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Remove the selected custom dictionary word.\"");
        xaml.Should().Contain("x:Name=\"ProofingCustomDictionaryClearWordsButton\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Clear custom dictionary words\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"ProofingCustomDictionaryClearWordsButton\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Clear all custom dictionary words.\"");

        source.Should().Contain("PopulateProofingCustomDictionaryWords();");
        source.Should().Contain("private readonly CustomDictionaryEditorSession _customDictionaryEditor");
        source.Should().Contain("_customDictionaryEditor.SetPendingWord(ProofingCustomDictionaryWordBox.Text)");
        source.Should().Contain("_customDictionaryEditor.AddPendingWord();");
        source.Should().Contain("_customDictionaryEditor = _dialogSession.CustomDictionary;");
        source.Should().Contain("var saveResult = _dialogSession.Commit(");
        source.Should().Contain("OptProofingIgnoreUppercase.IsChecked = _opts.ProofingIgnoreUppercase;");
    }

    [Fact]
    public void OptionsDialog_RoundTripsProofingCustomDictionaryEditorWords()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, path);

        StaTestRunner.Run(() =>
        {
            var initial = new AppOptions
            {
                SpellCheckCustomDictionaryWords = ["  TeH  ", "adn", "teh"]
            };
            AppOptionsStore.SaveToPath(initial, path).Should().BeTrue();
            var dialog = new OptionsDialog(initial);
            dialog.Show();
            try
            {
                var wordsList = GetControl<ListBox>(dialog, "ProofingCustomDictionaryWordsList");
                var wordBox = GetControl<TextBox>(dialog, "ProofingCustomDictionaryWordBox");
                var addButton = GetControl<Button>(dialog, "ProofingCustomDictionaryAddWordButton");
                var removeButton = GetControl<Button>(dialog, "ProofingCustomDictionaryRemoveWordButton");
                var clearButton = GetControl<Button>(dialog, "ProofingCustomDictionaryClearWordsButton");

                GetWords(wordsList).Should().Equal("adn", "TeH");

                wordBox.Text = "  Recieve  ";
                DialogSourceTestSupport.ClickButton(addButton);
                GetWords(wordsList).Should().Equal("adn", "Recieve", "TeH");

                wordBox.Text = "recieve";
                DialogSourceTestSupport.ClickButton(addButton);
                GetWords(wordsList).Should().Equal("adn", "Recieve", "TeH");

                wordsList.SelectedItem = "adn";
                DialogSourceTestSupport.ClickButton(removeButton);
                GetWords(wordsList).Should().Equal("Recieve", "TeH");

                DialogSourceTestSupport.ClickButton(clearButton);
                GetWords(wordsList).Should().BeEmpty();

                wordBox.Text = "  Final  ";
                DialogSourceTestSupport.ClickButton(addButton);
                GetWords(wordsList).Should().Equal("Final");

                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.Result.SpellCheckCustomDictionaryWords.Should().Equal("Final");
            }
            finally
            {
                dialog.Close();
            }
        });

        AppOptionsStore.LoadFromPath(path)
            .SpellCheckCustomDictionaryWords
            .Should()
            .Equal("Final");
    }

    [Fact]
    public void OptionsDialog_CancelDoesNotMutateOriginalProofingCustomDictionaryWords()
    {
        var options = new AppOptions
        {
            SpellCheckCustomDictionaryWords = [" keep ", "Keep", "also"]
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(options);
            dialog.Show();
            try
            {
                var wordBox = GetControl<TextBox>(dialog, "ProofingCustomDictionaryWordBox");
                var addButton = GetControl<Button>(dialog, "ProofingCustomDictionaryAddWordButton");
                var clearButton = GetControl<Button>(dialog, "ProofingCustomDictionaryClearWordsButton");
                var cancelButton = GetControl<Button>(dialog, "CancelBtn");

                DialogSourceTestSupport.ClickButton(clearButton);
                wordBox.Text = "cancelled";
                DialogSourceTestSupport.ClickButton(addButton);
                DialogSourceTestSupport.ClickButtonAllowingNonModalDialogResult(cancelButton);
            }
            finally
            {
                dialog.Close();
            }
        });

        options.SpellCheckCustomDictionaryWords.Should().Equal(" keep ", "Keep", "also");
    }

    private static IReadOnlyList<string> GetWords(ListBox listBox) =>
        listBox.Items.Cast<string>().ToArray();

}
