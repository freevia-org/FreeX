using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for shared-undo-across-panes F1: clicking OK (or Apply) in the "Manage
/// Conditional Formatting Rules" dialog with no edits made must not hand the caller a non-null
/// <see cref="ManageConditionalFormatsDialog.ResultRules"/>, since the caller (MainWindow.HomeFormatting.cs,
/// CfManageRulesMenuItem_Click) treats any non-null result as "apply this and push an undo entry" --
/// with no way for it to tell a real edit apart from a byte-identical re-application of the same rules.
/// </summary>
public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void OkImmediately_WithNoEdits_LeavesResultRulesNullSoNoUndoEntryIsPushed()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 1, 1, 1));
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkBtn_Click");

            // The caller (CfManageRulesMenuItem_Click) does
            //   if (dlg.ShowDialog() != true || dlg.ResultRules is null) return;
            // so a null ResultRules here is exactly what makes the visit a true no-op: nothing is
            // re-applied to the sheet and no undo entry is pushed for an unedited dialog visit.
            dialog.ResultRules.Should().BeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void ApplyImmediately_WithNoEdits_NeverInvokesCallbackAndLeavesResultRulesNull()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 1, 1, 1));

            var invokeCount = 0;
            var dialog = new ManageConditionalFormatsDialog(
                sheet,
                selection: null,
                applyRules: _ => invokeCount++);

            var applyButton = GetControl<Button>(dialog, "_applyBtn");

            // Apply must not even be enabled when nothing has changed.
            applyButton.IsEnabled.Should().BeFalse();

            applyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            invokeCount.Should().Be(0);
            dialog.ResultRules.Should().BeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void ApplyThenOkWithNoFurtherEdits_PushesOnlyOneCommitNotTwo()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var first = CreateRule(sheet.Id, 1, 1, 1);
            var second = CreateRule(sheet.Id, 2, 1, 2);
            sheet.ConditionalFormats.Add(first);
            sheet.ConditionalFormats.Add(second);

            var invokeCount = 0;
            var dialog = new ManageConditionalFormatsDialog(
                sheet,
                selection: null,
                applyRules: _ => invokeCount++);

            var listView = GetControl<ListView>(dialog, "_listView");
            var moveDownButton = GetControl<Button>(dialog, "_moveDownBtn");
            var applyButton = GetControl<Button>(dialog, "_applyBtn");

            // One real edit, then Apply -- this is a genuine commit and must fire the callback once.
            listView.SelectedIndex = 0;
            moveDownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            applyButton.IsEnabled.Should().BeTrue();
            applyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            invokeCount.Should().Be(1);

            // Apply button is disabled again -- nothing pending -- and clicking OK afterwards with no
            // further edits must not re-invoke the callback a second time for the same final state.
            applyButton.IsEnabled.Should().BeFalse();
            DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkBtn_Click");

            invokeCount.Should().Be(1);
            dialog.ResultRules.Should().BeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void OkAfterARealEdit_StillCommitsTheEditedRules()
    {
        // Sibling/no-regression case: OK must still build and hand back ResultRules -- and the caller
        // must still be able to push its undo entry -- when the user actually changed something.
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var first = CreateRule(sheet.Id, 1, 1, 1);
            var second = CreateRule(sheet.Id, 2, 1, 2);
            sheet.ConditionalFormats.Add(first);
            sheet.ConditionalFormats.Add(second);
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var listView = GetControl<ListView>(dialog, "_listView");
            var moveDownButton = GetControl<Button>(dialog, "_moveDownBtn");

            listView.SelectedIndex = 0;
            moveDownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkBtn_Click");

            dialog.ResultRules.Should().NotBeNull();
            dialog.ResultRules!.Select(rule => rule.Id).Should().Equal(second.Id, first.Id);

            dialog.Close();
        });
    }
}
