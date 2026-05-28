using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ReviewDialogFocusAccessibilityTests
{
    [Fact]
    public void SpellCheckDialog_WithSuggestion_FocusesSuggestionsAndExposesUiaMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SpellCheckDialog("mispelled", "misspelled");
            try
            {
                ShowAndPump(dialog);

                var suggestions = GetField<ListBox>(dialog, "_suggestionsBox");
                var replacement = GetField<TextBox>(dialog, "_replacementBox");
                suggestions.SelectedItem.Should().Be("misspelled");
                Keyboard.FocusedElement.Should().BeSameAs(suggestions);
                AutomationProperties.GetAutomationId(suggestions).Should().Be("SpellCheckSuggestionsList");
                AutomationProperties.GetHelpText(suggestions).Should().Be("Choose a suggested spelling replacement.");
                AutomationProperties.GetAutomationId(replacement).Should().Be("SpellCheckReplacementBox");

                FindButton(dialog, "Change").Should().Match<Button>(button =>
                    button.IsDefault &&
                    AutomationProperties.GetAutomationId(button) == "SpellCheckChangeButton" &&
                    AutomationProperties.GetHelpText(button) == "Replace this occurrence.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SpellCheckDialog_WithoutSuggestion_FocusesReplacementBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SpellCheckDialog("mispelled", "");
            try
            {
                ShowAndPump(dialog);

                var replacement = GetField<TextBox>(dialog, "_replacementBox");
                Keyboard.FocusedElement.Should().BeSameAs(replacement);
                replacement.SelectionLength.Should().Be(replacement.Text.Length);
                AutomationProperties.GetHelpText(replacement).Should().Be("Enter the replacement text for the misspelled word.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AccessibilityCheckerDialog_WithIssue_FocusesIssueListAndExposesGoToMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var dialog = new AccessibilityCheckerDialog([
                new AccessibilityIssue(
                    AccessibilityIssueKind.LowContrastCellText,
                    sheetId,
                    "Sheet1",
                    "B2",
                    "Cell text has low contrast against its fill color.")
            ]);
            try
            {
                ShowAndPump(dialog);

                var issueList = GetField<ListBox>(dialog, "_issueList");
                var goToButton = GetField<Button>(dialog, "_goToButton");
                Keyboard.FocusedElement.Should().BeSameAs(issueList);
                issueList.SelectedIndex.Should().Be(0);
                GetIssueText(issueList.Items[0]).Should().Contain("Sheet1!B2").And.Contain("low contrast");
                AutomationProperties.GetAutomationId(issueList).Should().Be("AccessibilityCheckerIssueList");
                AutomationProperties.GetHelpText(goToButton).Should().Be("Navigate to the selected accessibility issue.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AccessibilityCheckerDialog_CleanState_FocusesResultText()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AccessibilityCheckerDialog([]);
            try
            {
                ShowAndPump(dialog);

                var messageBox = GetField<TextBox>(dialog, "_messageBox");
                Keyboard.FocusedElement.Should().BeSameAs(messageBox);
                messageBox.Text.Should().Be("No accessibility issues found.");
                AutomationProperties.GetAutomationId(messageBox).Should().Be("AccessibilityCheckerResultText");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void WorkbookStatisticsDialog_FocusesDefaultOkAndExposesSummaryMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new WorkbookStatisticsDialog(new WorkbookStatistics(
                WorksheetCount: 1,
                CellCount: 2,
                FormulaCount: 3,
                CommentCount: 4,
                ChartCount: 5,
                PictureCount: 6,
                ShapeCount: 7,
                NamedRangeCount: 8));
            try
            {
                ShowAndPump(dialog);

                Keyboard.FocusedElement.Should().Match<Button>(button => button.IsDefault && Equals(button.Content, "_OK"));
                var summary = FindVisualChild<TextBlock>(dialog, element =>
                    AutomationProperties.GetAutomationId(element) == "WorkbookStatisticsSummary");
                summary.Should().NotBeNull();
                AutomationProperties.GetHelpText(summary!).Should().Be("Summarizes sheet, cell, formula, comment, and object counts for the workbook.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static void ShowAndPump(Window dialog)
    {
        dialog.Show();
        PumpDispatcher();
    }

    private static Button FindButton(DependencyObject root, string content)
    {
        var button = FindVisualChild<Button>(root, element => Equals(element.Content?.ToString()?.Replace("_", string.Empty), content));
        button.Should().NotBeNull();
        return button!;
    }

    private static T? FindVisualChild<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T current && predicate(current))
            return current;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindVisualChild(VisualTreeHelper.GetChild(root, i), predicate);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    private static string GetIssueText(object item)
    {
        var property = item.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
        property.Should().NotBeNull();
        return property!.GetValue(item).Should().BeOfType<string>().Subject;
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
