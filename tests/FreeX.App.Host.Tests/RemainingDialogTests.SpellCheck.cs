using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void SpellCheckDialog_CreateReplaceResult_CapturesReplacement()
    {
        SpellCheckDialog.CreateReplaceResult("mispelled", "misspelled")
            .Should()
            .Be(new SpellCheckSessionDecision(SpellCheckSessionAction.Change, "misspelled"));
    }

    [Fact]
    public void SpellCheckDialog_CreateReplaceAllResult_CapturesReplacement()
    {
        SpellCheckDialog.CreateReplaceAllResult("mispelled", "misspelled")
            .Should()
            .Be(new SpellCheckSessionDecision(SpellCheckSessionAction.ChangeAll, "misspelled"));
    }

    [Fact]
    public void SpellCheckDialog_CreateIgnoreAllResult_UsesDistinctAction()
    {
        SpellCheckDialog.CreateIgnoreAllResult()
            .Should()
            .Be(new SpellCheckSessionDecision(SpellCheckSessionAction.IgnoreAll));
    }

    [Fact]
    public void SpellCheckDialog_UsesExcelLikeNotInDictionarySuggestionsAndChangeToLayout()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("private readonly TextBox _notInDictionaryBox");
        source.Should().Contain("private readonly ListBox _suggestionsBox");
        source.Should().Contain("UiText.Get(\"SpellCheck_NotInDictionary\")");
        source.Should().Contain("UiText.Get(\"SpellCheck_Suggestions\")");
        source.Should().Contain("_suggestionsBox.Items.Add(suggestion)");
        source.Should().Contain("_suggestionsBox.SelectionChanged");
        source.Should().Contain("new Label { Content = UiText.Get(\"SpellCheck_ChangeTo\"), Target = _replacementBox");
        source.Should().Contain("Grid.SetColumn(actionButtons");
    }

    [Fact]
    public void SpellCheckDialog_FieldControlsExposeAutomationNames()
    {
        var source = ReadClassSource("SpellCheckDialog.cs", "public sealed class SpellCheckDialog", "public sealed class __NoNextSpellCheckDialog");

        source.Should().Contain("AutomationProperties.SetName(_notInDictionaryBox, UiText.Get(\"SpellCheck_NotInDictionary2\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_notInDictionaryBox, \"SpellCheckNotInDictionaryBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_notInDictionaryBox, UiText.Get(\"SpellCheck_ShowsTheWordThatWasNotFoundInTheDictionary\"));");
        source.Should().Contain("AutomationProperties.SetName(_suggestionsBox, UiText.Get(\"SpellCheck_Suggestions2\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_suggestionsBox, \"SpellCheckSuggestionsList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_suggestionsBox, UiText.Get(\"SpellCheck_ChooseASuggestedSpellingReplacement\"));");
        source.Should().Contain("AutomationProperties.SetName(_replacementBox, UiText.Get(\"SpellCheck_ChangeTo2\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_replacementBox, \"SpellCheckReplacementBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_replacementBox, UiText.Get(\"SpellCheck_EnterTheReplacementTextForTheMisspelledWord\"));");
    }

    [Fact]
    public void SpellCheckDialog_ExposesExcelLikeIgnoreChangeAndAddActions()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("SpellCheckSessionAction.AddToDictionary");
        source.Should().Contain("Content = UiText.Get(\"SpellCheck_IgnoreOnce\")");
        source.Should().Contain("Content = UiText.Get(\"SpellCheck_Change\")");
        source.Should().Contain("Content = UiText.Get(\"SpellCheck_Change\"), Width = 90, IsDefault = true");
        source.Should().Contain("Content = UiText.Get(\"SpellCheck_AddToDictionary\")");
        source.Should().Contain("CreateIgnoreAllResult");
        source.Should().Contain("CreateReplaceAllResult(word, _replacementBox.Text)");
        source.Should().Contain("CreateAddResult");
        source.Should().Contain("RefreshChangeButtonState");
    }

    [Fact]
    public void SpellCheckDialog_DisablesChangeActionsUntilReplacementTextExists()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SpellCheckDialog("mispelled", "");
            dialog.Show();
            try
            {
                var replacementBox = GetField<TextBox>(dialog, "_replacementBox");
                var changeButton = GetField<Button>(dialog, "_changeButton");
                var changeAllButton = GetField<Button>(dialog, "_changeAllButton");

                changeButton.IsEnabled.Should().BeFalse();
                changeAllButton.IsEnabled.Should().BeFalse();

                replacementBox.Text = "misspelled";

                changeButton.IsEnabled.Should().BeTrue();
                changeAllButton.IsEnabled.Should().BeTrue();

                replacementBox.Text = " ";

                changeButton.IsEnabled.Should().BeFalse();
                changeAllButton.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SpellCheckDialog_ActionButtonsUseUniqueExcelLikeAccessKeys()
    {
        var labels = new[]
        {
            UiText.Get("SpellCheck_IgnoreOnce"),
            UiText.Get("SpellCheck_IgnoreAll"),
            UiText.Get("SpellCheck_Change"),
            UiText.Get("SpellCheck_ChangeAll"),
            UiText.Get("SpellCheck_AddToDictionary"),
            UiText.Get("SpellCheck_Cancel")
        };

        labels.Select(GetAccessKey).Should().OnlyHaveUniqueItems();

        var source = ReadClassSource("SpellCheckDialog.cs", "public sealed class SpellCheckDialog", "public sealed class __NoNextSpellCheckDialog");
        foreach (var key in new[]
        {
            "SpellCheck_IgnoreOnce",
            "SpellCheck_IgnoreAll",
            "SpellCheck_Change",
            "SpellCheck_ChangeAll",
            "SpellCheck_AddToDictionary",
            "SpellCheck_Cancel"
        })
        {
            source.Should().Contain($"Content = UiText.Get(\"{key}\")");
        }

        source.Should().Contain("AutomationProperties.SetAutomationId(button, automationId);");
        source.Should().Contain("AutomationProperties.SetHelpText(button, helpText);");
        source.Should().Contain("SpellCheckIgnoreOnceButton");
        source.Should().Contain("SpellCheckIgnoreAllButton");
        source.Should().Contain("SpellCheckChangeButton");
        source.Should().Contain("SpellCheckChangeAllButton");
        source.Should().Contain("SpellCheckAddToDictionaryButton");
        source.Should().Contain("SpellCheckCancelButton");
    }

    [Fact]
    public void SpellCheckDialogOpenedFromKeyboard_FocusesSuggestionListOrReplacementBox()
    {
        var source = ReadClassSource("SpellCheckDialog.cs", "public sealed class SpellCheckDialog", "public sealed class __NoNextSpellCheckDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_suggestionsBox.Items.Count > 0");
        source.Should().Contain("_suggestionsBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_suggestionsBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(_replacementBox);");
    }

    [Fact]
    public void SpellCheckDialogSuggestionsList_DoubleClickAcceptsSelectedSuggestion()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SpellCheckDialog("mispelled", "misspelled");
            var suggestionsBox = GetField<ListBox>(dialog, "_suggestionsBox");

            dialog.Dispatcher.BeginInvoke(() =>
            {
                suggestionsBox.SelectedItem = "misspelled";
                suggestionsBox.RaiseEvent(DialogSourceTestSupport.CreateMouseDoubleClickEvent());

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(new SpellCheckSessionDecision(SpellCheckSessionAction.Change, "misspelled"));
        });
    }

    [Fact]
    public void SpellCheckDialogSuggestionsList_DoubleClickWithoutSelectionDoesNotAccept()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SpellCheckDialog("mispelled", "misspelled");
            var suggestionsBox = GetField<ListBox>(dialog, "_suggestionsBox");

            suggestionsBox.SelectedItem = null;
            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            suggestionsBox.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.DialogResult.Should().BeNull();
            dialog.Result.Should().Be(new SpellCheckSessionDecision(SpellCheckSessionAction.Change, "misspelled"));
        });
    }
}
