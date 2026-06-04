using System.IO;
using System.Windows;
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

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
        source.Should().Contain("FreeXOptions.NormalizeSpellCheckCustomDictionaryWord(ProofingCustomDictionaryWordBox.Text)");
        source.Should().Contain("FreeXOptions.NormalizeSpellCheckCustomDictionaryWords(_customDictionaryWords.Append(word))");
        source.Should().Contain("SpellCheckCustomDictionaryWords = FreeXOptions.NormalizeSpellCheckCustomDictionaryWords(_customDictionaryWords)");
    }

    [Fact]
    public void OptionsDialog_RoundTripsProofingCustomDictionaryEditorWords()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "FreeXOptionsDialogProofingTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(tempDirectory, "options.json");
        var previousPath = Environment.GetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable);
        Environment.SetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable, path);

        try
        {
            StaTestRunner.Run(() =>
            {
                var dialog = new OptionsDialog(new FreeXOptions
                {
                    SpellCheckCustomDictionaryWords = ["  TeH  ", "adn", "teh"]
                });
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
                    Click(addButton);
                    GetWords(wordsList).Should().Equal("adn", "Recieve", "TeH");

                    wordBox.Text = "recieve";
                    Click(addButton);
                    GetWords(wordsList).Should().Equal("adn", "Recieve", "TeH");

                    wordsList.SelectedItem = "adn";
                    Click(removeButton);
                    GetWords(wordsList).Should().Equal("Recieve", "TeH");

                    Click(clearButton);
                    GetWords(wordsList).Should().BeEmpty();

                    wordBox.Text = "  Final  ";
                    Click(addButton);
                    GetWords(wordsList).Should().Equal("Final");

                    ClickOkAllowingNonModalDialogResult(dialog);

                    dialog.Result.SpellCheckCustomDictionaryWords.Should().Equal("Final");
                }
                finally
                {
                    dialog.Close();
                }
            });

            FreeXOptions.LoadFromPath(path)
                .SpellCheckCustomDictionaryWords
                .Should()
                .Equal("Final");
        }
        finally
        {
            Environment.SetEnvironmentVariable(FreeXOptions.OptionsPathEnvironmentVariable, previousPath);
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void OptionsDialog_CancelDoesNotMutateOriginalProofingCustomDictionaryWords()
    {
        var options = new FreeXOptions
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

                Click(clearButton);
                wordBox.Text = "cancelled";
                Click(addButton);
                ClickAllowingNonModalDialogResult(cancelButton);
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

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickAllowingNonModalDialogResult(Button button)
    {
        try
        {
            Click(button);
        }
        catch (InvalidOperationException invalidOperation)
            when (invalidOperation.Message.Contains("DialogResult", StringComparison.Ordinal))
        {
        }
    }
}
