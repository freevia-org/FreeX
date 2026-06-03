using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void ScopeSelector_UsesExcelWorksheetLabelAndDefaultsToSelectionWhenAvailable()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("CreateScopeItem(ConditionalFormatScope.Sheet, UiText.Get(\"ManageConditionalFormats_ScopeThisWorksheet\"))");
        source.Should().Contain("CreateScopeItem(ConditionalFormatScope.Selection, UiText.Get(\"ManageConditionalFormats_ScopeCurrentSelection\"))");
        source.Should().Contain("_scopeBox.SelectedItem = selection.HasValue ? selectionScope : sheetScope");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesScopeSelector()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_scopeBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_scopeBox);");
    }

    [Fact]
    public void RulesList_IsLabeledAndNamedForAccessibility()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("new Label { Content = UiText.Get(\"ManageConditionalFormats_Rules\"), Target = _listView");
        source.Should().Contain("AutomationProperties.SetName(_listView, UiText.Get(\"ManageConditionalFormats_ConditionalFormattingRules\"));");
    }

    [Fact]
    public void ScopeSelector_DefaultsToCurrentSelectionWhenSelectionIsProvided()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var selection = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 4));
            var dialog = new ManageConditionalFormatsDialog(sheet, selection);

            var scope = GetControl<ComboBox>(dialog, "_scopeBox");

            ((ComboBoxItem)scope.SelectedItem).Content.Should().Be(UiText.Get("ManageConditionalFormats_ScopeCurrentSelection"));
            ScopeContents(scope).Should().Equal(
                UiText.Get("ManageConditionalFormats_ScopeThisWorksheet"),
                UiText.Get("ManageConditionalFormats_ScopeCurrentSelection"));

            dialog.Close();
        });
    }

    [Fact]
    public void ScopeSelector_IncludesTableScopeWhenSelectionIntersectsStructuredTable()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var tableRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 6, 4));
            sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "Sales", DisplayName = "Sales", Range = tableRange });
            var selection = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 3));
            var dialog = new ManageConditionalFormatsDialog(sheet, selection);

            var scope = GetControl<ComboBox>(dialog, "_scopeBox");

            ScopeContents(scope).Should().Equal(
                UiText.Get("ManageConditionalFormats_ScopeThisWorksheet"),
                UiText.Get("ManageConditionalFormats_ScopeThisTable"),
                UiText.Get("ManageConditionalFormats_ScopeCurrentSelection"));

            dialog.Close();
        });
    }

    [Fact]
    public void ScopeSelector_TableScopeFiltersRulesByTableRange()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var tableRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 6, 4));
            sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "Sales", DisplayName = "Sales", Range = tableRange });
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 3, 3, 1));
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 10, 10, 2));
            var selection = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 3));
            var dialog = new ManageConditionalFormatsDialog(sheet, selection);

            var scopeBox = GetControl<ComboBox>(dialog, "_scopeBox");
            scopeBox.SelectedItem = ScopeItem(scopeBox, UiText.Get("ManageConditionalFormats_ScopeThisTable"));
            var listView = GetControl<ListView>(dialog, "_listView");

            listView.Items.Cast<ConditionalFormat>().Should().ContainSingle(rule => rule.AppliesTo.Start.Row == 3);

            dialog.Close();
        });
    }
}
