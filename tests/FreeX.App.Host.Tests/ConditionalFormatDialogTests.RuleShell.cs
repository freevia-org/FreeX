using System.Linq;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ConditionalFormatDialogTests
{
    [Fact]
    public void BaseRuleDialog_ExposesKeyboardAccessKeysForFieldsAndButtons()
    {
        var source = ReadConditionalFormatDialogSource();

        source.Should().Contain("ConditionalFormatDialog_MinimumLabel");
        source.Should().Contain("ConditionalFormatDialog_ValueLabel");
        source.Should().Contain("ConditionalFormatDialog_MaximumLabel");
        source.Should().Contain("ConditionalFormatDialog_BarColorLabel");
        source.Should().Contain("ConditionalFormatDialog_FormatLabel");
        source.Should().Contain("ConditionalFormatDialog_FormulaLabel");
        source.Should().Contain("ConditionalFormatDialog_MinimumTypeLabel");
        source.Should().Contain("ConditionalFormatDialog_MinimumValueLabel");
        source.Should().Contain("ConditionalFormatDialog_MaximumTypeLabel");
        source.Should().Contain("ConditionalFormatDialog_MaximumValueLabel");
        source.Should().Contain("ConditionalFormatDialog_MinimumBarLengthLabel");
        source.Should().Contain("ConditionalFormatDialog_MaximumBarLengthLabel");
        source.Should().Contain("ConditionalFormatDialog_MinimumColorLabel");
        source.Should().Contain("ConditionalFormatDialog_MidpointTypeLabel");
        source.Should().Contain("ConditionalFormatDialog_MidpointValueLabel");
        source.Should().Contain("ConditionalFormatDialog_MidpointColorLabel");
        source.Should().Contain("ConditionalFormatDialog_MaximumColorLabel");
        source.Should().Contain("ConditionalFormatDialog_IconSetLabel");
        source.Should().Contain("ConditionalFormatDialog_DatePeriodLabel");
        source.Should().Contain("ConditionalFormatDialog_FormatCellsThatContainLabel");
        source.Should().Contain("ConditionalFormatDialog_FormatButton");
        source.Should().Contain("ConditionalFormatDialog_ShowValue");
        source.Should().Contain("ConditionalFormatDialog_ReverseIconOrder");
        source.Should().Contain("ConditionalFormatDialog_ShowBarOnly");
        source.Should().Contain("ConditionalFormatDialog_UseThreeColorScale");
        source.Should().Contain("ConditionalFormatDialog_PercentLabel");
        source.Should().Contain("ConditionalFormatDialog_RankLabel");
        source.Should().Contain("UiText.Ok");
        source.Should().Contain("UiText.Cancel");
    }

    [Fact]
    public void RuleDialogOpenedFromKeyboard_FocusesFirstRuleEditor()
    {
        var source = ReadConditionalFormatDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_formulaBox is { IsVisible: true }");
        source.Should().Contain("_conditionKindBox.IsVisible");
        source.Should().Contain("_value1Box.IsVisible");
        source.Should().Contain("_topBottomRankBox.IsVisible");
        source.Should().Contain("_dataBarMinTypeBox.IsVisible");
        source.Should().Contain("_colorScaleMinTypeBox.IsVisible");
        source.Should().Contain("_iconSetStyleBox.IsVisible");
        source.Should().Contain("_dateOccurringPeriodBox.IsVisible");
        source.Should().Contain("_duplicateValuesKindBox.IsVisible");
        source.Should().Contain("textBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void NewRuleDialog_UsesExcelRuleShell()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Formula", RangeFor(SheetId.New())));

            dialog.Title.Should().Be(UiText.Get("ConditionalFormatDialog_NewTitle"));
            dialog.Width.Should().BeApproximately(ConditionalFormatDialogCatalog.RuleEditorWpfWindowWidth, 2);
            FindText(dialog.Content, UiText.Get("ConditionalFormatDialog_SelectRuleTypeHeader")).Should().NotBeNull();
            FindText(dialog.Content, UiText.Get("ConditionalFormatDialog_EditRuleDescriptionHeader")).Should().NotBeNull();

            var ruleTypeList = FindControl<ListBox>(dialog.Content);
            ruleTypeList.Should().NotBeNull();
            ruleTypeList!.Items.Cast<object>().Select(item => item.ToString()).Should().Contain([
                UiText.Get("ConditionalFormatDialog_RuleShell_FormatAllCells"),
                UiText.Get("ConditionalFormatDialog_RuleShell_FormatContainingCells"),
                UiText.Get("ConditionalFormatDialog_RuleShell_UseFormula")
            ]);
            AutomationProperties.GetName(ruleTypeList).Should().Be(UiText.Get("ConditionalFormatDialog_RuleTypeAutomationName"));
            ruleTypeList.SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_RuleShell_UseFormula"));

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleDialog_ChangingRuleShellSelectionRefreshesEditor()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Greater Than", RangeFor(SheetId.New())));

            var ruleTypeList = FindControl<ListBox>(dialog.Content);
            ruleTypeList.Should().NotBeNull();
            ruleTypeList!.SelectedItem = UiText.Get("ConditionalFormatDialog_RuleShell_UseFormula");

            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_FormulaLabel")).Should().NotBeNull();
            GetControl<TextBox>(dialog, "_formulaBox").Text = "=A1>10";

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.Formula);
            dialog.ResultRule.FormulaText.Should().Be("A1>10");

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleDialog_ChangingToValueBasedShellRefreshesToDataBarControls()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Formula", RangeFor(SheetId.New())));

            var ruleTypeList = FindControl<ListBox>(dialog.Content);
            ruleTypeList.Should().NotBeNull();
            ruleTypeList!.SelectedItem = UiText.Get("ConditionalFormatDialog_RuleShell_FormatAllCells");

            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_MinimumTypeLabel")).Should().NotBeNull();
            FindNamedControl<Border>(dialog.Content, "DataBarPreview").Should().NotBeNull();
            GetControl<CheckBox>(dialog, "_dataBarShowValueBox").Content.Should().Be(UiText.Get("ConditionalFormatDialog_ShowBarOnly"));

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleDialog_ContainsShellShowsExcelConditionKindSelectors()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Greater Than", RangeFor(SheetId.New())));

            var conditionKind = GetControl<ComboBox>(dialog, "_conditionKindBox");
            var cellOperator = GetControl<ComboBox>(dialog, "_cellValueOperatorBox");

            conditionKind.Items.Cast<string>().Should().Contain([
                UiText.Get("ConditionalFormatDialog_ConditionKind_CellValue"),
                UiText.Get("ConditionalFormatDialog_ConditionKind_SpecificText"),
                UiText.Get("ConditionalFormatDialog_ConditionKind_DatesOccurring"),
                UiText.Get("ConditionalFormatDialog_ConditionKind_Blanks"),
                UiText.Get("ConditionalFormatDialog_ConditionKind_NoBlanks"),
                UiText.Get("ConditionalFormatDialog_ConditionKind_Errors"),
                UiText.Get("ConditionalFormatDialog_ConditionKind_NoErrors")
            ]);
            conditionKind.SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_ConditionKind_CellValue"));
            cellOperator.SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_CellValueOperator_GreaterThan"));
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_FormatOnlyCellsWithLabel")).Should().NotBeNull();
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_OperatorLabel")).Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleDialog_ContainsShellCanCreateBlankRuleWithoutValue()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Greater Than", RangeFor(SheetId.New())));

            GetControl<ComboBox>(dialog, "_conditionKindBox").SelectedItem = UiText.Get("ConditionalFormatDialog_ConditionKind_Blanks");
            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.Blanks);

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleDialog_ContainsShellCanCreateBetweenCellValueRule()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Greater Than", RangeFor(SheetId.New())));

            GetControl<ComboBox>(dialog, "_cellValueOperatorBox").SelectedItem = UiText.Get("ConditionalFormatDialog_CellValueOperator_Between");
            GetControl<TextBox>(dialog, "_value1Box").Text = "5";
            GetControl<TextBox>(dialog, "_value2Box").Text = "10";
            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.CellValue);
            dialog.ResultRule.Operator.Should().Be(CfOperator.Between);
            dialog.ResultRule.Value1.Should().Be("5");
            dialog.ResultRule.Value2.Should().Be("10");

            dialog.Close();
        });
    }

    [Fact]
    public void HighlightRuleDialog_OffersExcelFormatPresetsAndFormatButton()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Greater Than", RangeFor(SheetId.New())));

            var formatBox = GetControl<ComboBox>(dialog, "_colorBox");
            formatBox.Items.Cast<object>().Select(item => item.ToString()).Should().Contain([
                UiText.Get("ConditionalFormatDialog_FormatPreset_LightRedDarkRedText"),
                UiText.Get("ConditionalFormatDialog_FormatPreset_YellowDarkYellowText"),
                UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat")
            ]);
            FindButton(dialog.Content, UiText.Get("ConditionalFormatDialog_FormatButton")).Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void EditRuleDialog_UsesExcelEditTitleAndRuleShell()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.GreaterThan,
                Value1 = "10"
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            dialog.Title.Should().Be(UiText.Get("ConditionalFormatDialog_EditTitle"));
            FindText(dialog.Content, UiText.Get("ConditionalFormatDialog_SelectRuleTypeHeader")).Should().NotBeNull();
            FindText(dialog.Content, UiText.Get("ConditionalFormatDialog_EditRuleDescriptionHeader")).Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void RuleDialogInvalidRequiredInputs_ShowOwnedWarningsAndRefocusEditors()
    {
        var source = ReadConditionalFormatDialogSource();

        source.Should().Contain("ConditionalFormatRuleSchema.ForRuleType(input.RuleType).Validate(input)");
        source.Should().Contain("ConditionalFormatRuleBuilder.Build(");
        source.Should().Contain("ConditionalFormatDialog_InvalidFormulaMessage");
        source.Should().Contain("ConditionalFormatDialog_InvalidValueMessage");
        source.Should().Contain("ConditionalFormatDialog_InvalidMaximumValueMessage");
        source.Should().Contain("ConditionalFormatDialog_InvalidTextMessage");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox? target)");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
    }

    [Fact]
    public void RuleDialogInvalidAdvancedInputs_ShowWarningsAndRefocusEditors()
    {
        var source = ReadConditionalFormatDialogSource();

        source.Should().Contain("CfInputField.DataBarMinLength");
        source.Should().Contain("ConditionalFormatDialog_InvalidMinimumBarLengthMessage");
        source.Should().Contain("CfInputField.DataBarMaxLength");
        source.Should().Contain("ConditionalFormatDialog_InvalidMaximumBarLengthMessage");
        source.Should().Contain("CfInputField.Rank");
        source.Should().Contain("ConditionalFormatDialog_InvalidRankOrPercentMessage");
        source.Should().Contain("CfInputField.ColorScaleMinColor");
        source.Should().Contain("ConditionalFormatDialog_InvalidMinimumColorMessage");
        source.Should().Contain("CfInputField.ColorScaleMidColor");
        source.Should().Contain("ConditionalFormatDialog_InvalidMidpointColorMessage");
        source.Should().Contain("CfInputField.ColorScaleMaxColor");
        source.Should().Contain("ConditionalFormatDialog_InvalidMaximumColorMessage");
        source.Should().NotContain("Math.Clamp(value, 0, 100)");
        source.Should().NotContain(": 10;");
        source.Should().NotContain("ParseRgbOrFallback");
    }

    [Fact]
    public void DialogCatalogPolicy_DelegatesToSharedPresentationCatalog()
    {
        var source = ReadConditionalFormatDialogSource();

        source.Should().Contain("ConditionalFormatDialogCatalog.FormatStyleOptions");
        source.Should().Contain("ConditionalFormatDialogCatalog.ColorPresets");
        source.Should().Contain("ConditionalFormatDialogCatalog.RuleShellOptions");
        source.Should().Contain("ConditionalFormatDialogCatalog.ConditionKindOptions");
        source.Should().Contain("ConditionalFormatDialogCatalog.DatePeriodOptions");
        source.Should().NotContain("Color.FromRgb(255, 199, 206)");
    }
}
