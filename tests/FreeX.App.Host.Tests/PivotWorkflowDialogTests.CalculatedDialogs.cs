using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;
using PivotCalculatedFieldResult = FreeX.App.Presentation.PivotUI.PivotCalculatedFieldPlanner.PivotCalculatedFieldResult;
using PivotCalculatedItemResult = FreeX.App.Presentation.PivotUI.PivotCalculatedItemPlanner.PivotCalculatedItemResult;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotCalculatedFieldDialog_CreateResult_TrimsAndBuildsModel()
    {
        var result = PivotCalculatedFieldDialog.CreateResult("  Revenue  ", "  Sales-Cost  ");

        result.Should().Be(new PivotCalculatedFieldResult("Revenue", "Sales-Cost"));
        result.ToModel().Should().Be(new PivotCalculatedFieldModel("Revenue", "Sales-Cost"));
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotCalculated_NameAndFormulaGroup\")");
        source.Should().Contain("AddTextBox(formulaPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox");
        source.Should().Contain("UiText.Get(\"PivotCalculated_FormulaLabel\")");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_nameBox.Focus();");
        source.Should().Contain("_nameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_nameBox);");
        source.Should().NotContain("Use field names in formulas");
        source.Should().NotContain("Calculated fields are added to the Values area");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ExposesFieldsListAndInsertFieldControl()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("private readonly ListBox _fieldList");
        source.Should().Contain("UiText.Get(\"PivotCalculated_AvailableFieldsLabel\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_InsertField\")");
        source.Should().Contain("InsertSelectedField");
        source.Should().Contain("InsertFormulaReference");
    }

    [Fact]
    public void PivotCalculatedDialogs_FieldAndItemListsExposeAutomationNames()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("AutomationProperties.SetName(_fieldList, UiText.Get(\"PivotCalculated_AvailableFields\"));");
        source.Should().Contain("AutomationProperties.SetName(_itemList, UiText.Get(\"PivotCalculated_AvailableItems\"));");
    }

    [Fact]
    public void PivotCalculatedFieldDialogInvalidRequiredInputs_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotCalculatedDialogs.cs",
            "public sealed class PivotCalculatedFieldDialog",
            "public sealed class PivotCalculatedItemDialog");

        source.Should().Contain("PivotCalculatedFieldSession.CreateDraft(");
        source.Should().Contain("EmptyNameMessage = UiText.Get(\"PivotCalculated_EnterCalculatedFieldName\")");
        source.Should().Contain("EmptyFormulaMessage = UiText.Get(\"PivotCalculated_EnterCalculatedFieldFormula\")");
        source.Should().Contain("var plan = _session.PlanSave(");
        source.Should().Contain("issue.Target == PivotCalculatedInputTarget.Formula ? _formulaBox : _nameBox");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_InsertFormulaReference_InsertsQuotedFieldAtCaret()
    {
        PivotCalculatedFieldDialog.InsertFormulaReference("Sales+Cost", "[Region Name]", 6, 0)
            .Should()
            .Be("Sales+[Region Name]Cost");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_FieldListDoubleClickInsertsSelectedFieldAndHandlesMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotCalculatedFieldDialog(formula: "Sales+", fieldNames: ["Region"]);
            var fieldList = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_fieldList");
            var formulaBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_formulaBox");

            fieldList.SelectedItem = "Region";
            formulaBox.SelectionStart = formulaBox.Text.Length;
            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            fieldList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeTrue();
            formulaBox.Text.Should().Be("Sales+Region");
            formulaBox.SelectionStart.Should().Be("Sales+Region".Length);
        });
    }

    [Fact]
    public void PivotCalculatedItemDialog_CreateResult_TrimsClampsAndBuildsModel()
    {
        var result = PivotCalculatedItemDialog.CreateResult(
            sourceFieldIndex: -8,
            "  East + West  ",
            "  East+West  ");

        result.Should().Be(new PivotCalculatedItemResult(0, "East + West", "East+West"));
        result.ToModel().Should().Be(new PivotCalculatedItemModel(0, "East + West", "East+West"));
    }

    [Fact]
    public void PivotCalculatedItemDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotCalculated_FieldAndItemGroup\")");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_nameBox.Focus();");
        source.Should().Contain("_nameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_nameBox);");
        source.Should().NotContain("Calculated items are evaluated within the selected field");
        source.Should().Contain("PivotDialogLayout.AddLabeledControl(itemPanel, UiText.Get(\"PivotCalculated_SourceFieldLabel\"), _fieldBox");
        source.Should().Contain("AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_NameLabel\"), _nameBox");
        source.Should().Contain("AddTextBox(itemPanel, UiText.Get(\"PivotCalculated_ItemFormulaLabel\"), _formulaBox");
    }

    [Fact]
    public void PivotCalculatedItemDialog_ExposesFieldItemListsAndInsertionControls()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("private readonly ListBox _fieldList");
        source.Should().Contain("private readonly ListBox _itemList");
        source.Should().Contain("UiText.Get(\"PivotCalculated_AvailableItemsLabel\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_InsertField\")");
        source.Should().Contain("UiText.Get(\"PivotCalculated_InsertItem\")");
        source.Should().Contain("RefreshItemList");
        source.Should().Contain("InsertSelectedItem");
    }

    [Fact]
    public void PivotCalculatedItemDialogInvalidRequiredInputs_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotCalculatedDialogs.cs",
            "public sealed class PivotCalculatedItemDialog",
            "");

        source.Should().Contain("PivotCalculatedItemSession.CreateDraft(");
        source.Should().Contain("EmptyNameMessage = UiText.Get(\"PivotCalculated_EnterCalculatedItemName\")");
        source.Should().Contain("EmptyFormulaMessage = UiText.Get(\"PivotCalculated_EnterCalculatedItemFormula\")");
        source.Should().Contain("var plan = _session.PlanSave(");
        source.Should().Contain("issue.Target == PivotCalculatedInputTarget.Formula ? _formulaBox : _nameBox");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void PivotCalculatedItemDialog_InsertFormulaReference_ReplacesSelectedFormulaText()
    {
        PivotCalculatedItemDialog.InsertFormulaReference("East+West", "North", 5, 4)
            .Should()
            .Be("East+North");
    }

    [Fact]
    public void PivotCalculatedItemDialog_FieldListDoubleClickInsertsSelectedFieldAndHandlesMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotCalculatedItemDialog(["Region", "Product"], formula: "East+");
            var fieldList = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_fieldList");
            var formulaBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_formulaBox");

            fieldList.SelectedIndex = 1;
            formulaBox.SelectionStart = formulaBox.Text.Length;
            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            fieldList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeTrue();
            formulaBox.Text.Should().Be("East+Product");
            formulaBox.SelectionStart.Should().Be("East+Product".Length);
        });
    }

    [Fact]
    public void PivotCalculatedItemDialog_ItemListDoubleClickInsertsSelectedItemAndHandlesMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotCalculatedItemDialog(
                ["Region"],
                formula: "Region=",
                itemNamesBySourceFieldIndex: new Dictionary<int, IEnumerable<string>>
                {
                    [0] = ["East", "West"]
                });
            var itemList = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_itemList");
            var formulaBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_formulaBox");

            itemList.SelectedIndex = 1;
            formulaBox.SelectionStart = formulaBox.Text.Length;
            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            itemList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeTrue();
            formulaBox.Text.Should().Be("Region=West");
            formulaBox.SelectionStart.Should().Be("Region=West".Length);
        });
    }
}
