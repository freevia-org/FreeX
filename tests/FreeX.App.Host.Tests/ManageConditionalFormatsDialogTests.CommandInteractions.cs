using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void DialogCommands_ExposeKeyboardAccessKeys()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        foreach (var key in new[]
        {
            "ManageConditionalFormats_Apply",
            "ManageConditionalFormats_NewRule",
            "ManageConditionalFormats_EditRule",
            "ManageConditionalFormats_DuplicateRule",
            "ManageConditionalFormats_DeleteRule"
        })
        source.Should().Contain($"UiText.Get(\"{key}\")");

        source.Should().Contain("Content = UiText.Get(\"ManageConditionalFormats_ShowFormattingRulesFor\")");
        source.Should().Contain("Content = UiText.Ok");
        source.Should().Contain("Content = UiText.Cancel");
        source.Should().Contain("Target = _scopeBox");
        source.Should().Contain("ToolTip = UiText.Get(\"ManageConditionalFormats_MoveSelectedRuleUp\")");
        source.Should().Contain("ToolTip = UiText.Get(\"ManageConditionalFormats_MoveSelectedRuleDown\")");
        source.Should().Contain("AutomationProperties.SetName(_moveUpBtn, UiText.Get(\"ManageConditionalFormats_MoveUp\"))");
        source.Should().Contain("AutomationProperties.SetName(_moveDownBtn, UiText.Get(\"ManageConditionalFormats_MoveDown\"))");
    }

    [Fact]
    public void ToolbarButtons_EnableOnlyValidSelectedRuleActions()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 1, 1, 1));
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 2, 1, 2));
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var listView = GetControl<ListView>(dialog, "_listView");
            var editButton = GetControl<Button>(dialog, "_editBtn");
            var duplicateButton = GetControl<Button>(dialog, "_duplicateBtn");
            var deleteButton = GetControl<Button>(dialog, "_deleteBtn");
            var moveUpButton = GetControl<Button>(dialog, "_moveUpBtn");
            var moveDownButton = GetControl<Button>(dialog, "_moveDownBtn");

            editButton.IsEnabled.Should().BeFalse();
            duplicateButton.IsEnabled.Should().BeFalse();
            deleteButton.IsEnabled.Should().BeFalse();
            moveUpButton.IsEnabled.Should().BeFalse();
            moveDownButton.IsEnabled.Should().BeFalse();

            listView.SelectedIndex = 0;
            editButton.IsEnabled.Should().BeTrue();
            duplicateButton.IsEnabled.Should().BeTrue();
            deleteButton.IsEnabled.Should().BeTrue();
            moveUpButton.IsEnabled.Should().BeFalse();
            moveDownButton.IsEnabled.Should().BeTrue();

            listView.SelectedIndex = 1;
            editButton.IsEnabled.Should().BeTrue();
            duplicateButton.IsEnabled.Should().BeTrue();
            deleteButton.IsEnabled.Should().BeTrue();
            moveUpButton.IsEnabled.Should().BeTrue();
            moveDownButton.IsEnabled.Should().BeFalse();

            dialog.Close();
        });
    }

    [Fact]
    public void SelectionGuardCommands_FocusRulesListWhenNoRuleIsSelected()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("FocusRulesList();");
        source.Should().Contain("private void FocusRulesList()");
        source.Should().Contain("_listView.Focus();");
        source.Should().Contain("Keyboard.Focus(_listView);");
    }

    [Fact]
    public void DuplicateRuleCommand_InsertsCopyBelowSelectedRuleWithNewIdentity()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var first = CreateRule(sheet.Id, 1, 1, 1);
            var second = CreateRule(sheet.Id, 2, 1, 2);
            first.StopIfTrue = true;
            sheet.ConditionalFormats.Add(first);
            sheet.ConditionalFormats.Add(second);
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var listView = GetControl<ListView>(dialog, "_listView");
            var duplicateButton = GetControl<Button>(dialog, "_duplicateBtn");

            listView.SelectedIndex = 0;
            duplicateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            listView.Items.Count.Should().Be(3);
            listView.SelectedIndex.Should().Be(1);

            var copied = listView.SelectedItem.Should().BeOfType<ConditionalFormat>().Subject;
            copied.Id.Should().NotBe(first.Id);
            copied.AppliesTo.Should().Be(first.AppliesTo);
            copied.StopIfTrue.Should().BeTrue();
            listView.Items.Cast<ConditionalFormat>().Select(rule => rule.Priority).Should().Equal(1, 2, 3);

            dialog.Close();
        });
    }

    [Fact]
    public void ApplyCommand_CommitsRulesThroughCallbackWithoutClosingDialog()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var first = CreateRule(sheet.Id, 1, 1, 1);
            var second = CreateRule(sheet.Id, 2, 1, 2);
            sheet.ConditionalFormats.Add(first);
            sheet.ConditionalFormats.Add(second);

            IReadOnlyList<ConditionalFormat>? applied = null;
            var dialog = new ManageConditionalFormatsDialog(
                sheet,
                selection: null,
                applyRules: rules => applied = rules);

            var listView = GetControl<ListView>(dialog, "_listView");
            var moveDownButton = GetControl<Button>(dialog, "_moveDownBtn");
            var applyButton = GetControl<Button>(dialog, "_applyBtn");

            listView.SelectedIndex = 0;
            moveDownButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            applyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            applied.Should().NotBeNull();
            applied!.Select(rule => rule.Id).Should().Equal(second.Id, first.Id);
            applied.Select(rule => rule.Priority).Should().Equal(1, 2);
            dialog.ResultRules.Should().BeEquivalentTo(applied);

            dialog.Close();
        });
    }

    [Fact]
    public void ApplyCommand_DoesNotSetDialogResultOrCloseLikeOk()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("_applyRules?.Invoke(ResultRules);");
        source.Should().Contain("private void ApplyBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().NotContain("private void ApplyBtn_Click(object sender, RoutedEventArgs e)\r\n    {\r\n        CommitResult();\r\n        DialogResult = true;");
    }

    [Fact]
    public void ApplyAppliesToRangeSelection_UpdatesOnlyRequestingRule()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var first = CreateRule(sheet.Id, 1, 1, 1);
            var second = CreateRule(sheet.Id, 2, 2, 2);
            sheet.ConditionalFormats.Add(first);
            sheet.ConditionalFormats.Add(second);
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var newRange = new GridRange(new CellAddress(sheet.Id, 5, 3), new CellAddress(sheet.Id, 8, 4));
            dialog.ApplyAppliesToRangeSelection(second.Id, newRange);

            var listView = GetControl<ListView>(dialog, "_listView");
            var rules = listView.Items.Cast<ConditionalFormat>().ToList();
            rules[0].AppliesTo.Should().Be(first.AppliesTo);
            rules[1].Id.Should().Be(second.Id);
            rules[1].AppliesTo.Should().Be(newRange);
            rules[1].Priority.Should().Be(2);
            listView.SelectedItem.Should().BeSameAs(rules[1]);

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleCommand_OpensExcelStyleRuleTypeShellOnFirstCategory()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("Content = UiText.Get(\"ManageConditionalFormats_NewRule\")");
        source.Should().Contain("DefaultNewRuleType => UiText.Get(\"ManageConditionalFormats_DefaultNewRuleType\")");
        source.Should().Contain("new NewConditionalFormatRuleDialog(DefaultNewRuleType, defaultRange)");
        source.Should().NotContain("new NewConditionalFormatRuleDialog(\"Greater Than\", defaultRange)");
        source.Should().NotContain("new ConditionalFormatDialog(\"Greater Than\", defaultRange)");
        source.Should().NotContain("_newRuleTypeBox");
        source.Should().NotContain("toolBar.Children.Add(_newRuleTypeBox)");
    }
}
