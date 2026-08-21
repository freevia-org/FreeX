using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// r162 remediation. The round-162 fix for shared-undo-across-panes F1 made OK hand back a null
/// <see cref="ManageConditionalFormatsDialog.ResultRules"/> when nothing had changed, so an unedited
/// visit stops pushing an undo entry. Its scope audit then reproduced a regression that fix
/// introduced: two of the ListView's editors -- the Stop If True checkbox and the inline Applies To
/// text box -- write straight into the <see cref="ConditionalFormat"/> through their bindings and
/// never went through the dialog's edit commands, which are where the dirty flag is raised. A visit
/// whose ONLY edit came through one of those bindings therefore looked unedited, and OK discarded it.
///
/// These tests drive the real generated row containers, so they exercise the production bindings
/// and handlers rather than a detached stand-in control.
/// </summary>
public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void TogglingStopIfTrue_IsARealEdit_AndSurvivesOk()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var rule = CreateRule(sheet.Id, 1, 1, 1);
            rule.StopIfTrue = false;
            sheet.ConditionalFormats.Add(rule);
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var checkBox = FindInFirstRow<CheckBox>(dialog);

            // The row is bound to the dialog's working copy of the rule, not to the sheet's own
            // instance -- edits reach the sheet only when OK or Apply commits them.
            var editedRule = (ConditionalFormat)checkBox.DataContext;

            // The gesture: tick the box and change nothing else in the whole visit.
            checkBox.IsChecked = true;

            editedRule.StopIfTrue.Should().BeTrue("the two-way binding commits the toggle into the working rule");
            GetControl<Button>(dialog, "_applyBtn").IsEnabled
                .Should().BeTrue("a binding that mutated the rule is an edit, so the dialog is dirty");

            DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkBtn_Click");

            // Before the remediation this was null, and the caller's "ResultRules is null => nothing
            // to apply" check silently dropped the user's only edit.
            dialog.ResultRules.Should().NotBeNull();
            dialog.ResultRules!.Single(r => r.Id == rule.Id).StopIfTrue.Should().BeTrue();

            dialog.Close();
        });
    }

    [Fact]
    public void RetypingAppliesToInline_IsARealEdit_AndSurvivesOk()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var rule = CreateRule(sheet.Id, 1, 1, 1);
            sheet.ConditionalFormats.Add(rule);
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var textBox = FindInFirstRow<TextBox>(dialog);

            // GotFocus stashes the pre-edit text; the user then retypes the range and tabs away.
            textBox.RaiseEvent(new RoutedEventArgs(UIElement.GotFocusEvent));
            textBox.Text = "A1:C3";

            // With UpdateSourceTrigger=LostFocus the binding commits first and the handler runs after,
            // which is the order production sees on a real focus change.
            textBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            textBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            GetControl<Button>(dialog, "_applyBtn").IsEnabled
                .Should().BeTrue("retyping the range mutated the rule through the binding");

            DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkBtn_Click");

            dialog.ResultRules.Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void FocusPassingThroughAppliesToWithoutTyping_IsStillNotAnEdit()
    {
        // Sibling/no-regression case: the point of the original fix was that a visit which changed
        // nothing must not push an undo entry. Merely tabbing through the Applies To box -- WPF
        // fires LostFocus on every focus loss, typed or not -- must stay a no-op.
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var rule = CreateRule(sheet.Id, 1, 1, 1);
            sheet.ConditionalFormats.Add(rule);
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var textBox = FindInFirstRow<TextBox>(dialog);

            textBox.RaiseEvent(new RoutedEventArgs(UIElement.GotFocusEvent));
            textBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            GetControl<Button>(dialog, "_applyBtn").IsEnabled.Should().BeFalse();

            DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkBtn_Click");

            dialog.ResultRules.Should().BeNull();

            dialog.Close();
        });
    }

    /// <summary>
    /// Lays the rules ListView out so WPF generates real row containers, then returns the first
    /// control of the requested type from the first row.
    ///
    /// Deliberately not DataTemplate.LoadContent(): a tree instantiated that way is detached, and
    /// neither the template's bindings nor its factory-attached handlers are live on it, so a test
    /// built on it cannot tell a wired-up column from an unwired one. These tests exist precisely
    /// to prove the wiring, so they have to go through the containers the user actually clicks.
    /// </summary>
    private static T FindInFirstRow<T>(ManageConditionalFormatsDialog dialog)
        where T : FrameworkElement
    {
        var listView = GetControl<ListView>(dialog, "_listView");
        listView.ApplyTemplate();
        listView.Measure(new Size(1200, 800));
        listView.Arrange(new Rect(0, 0, 1200, 800));
        listView.UpdateLayout();

        var container = listView.ItemContainerGenerator.ContainerFromIndex(0)
            ?? throw new InvalidOperationException("The rules ListView generated no row container.");

        return FindDescendant<T>(container)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} in the first rule row.");
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : FrameworkElement
    {
        if (root is T typed)
            return typed;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }
}
