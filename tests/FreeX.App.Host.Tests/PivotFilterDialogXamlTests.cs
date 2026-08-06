using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotFilterDialogXamlTests
{
    [Theory]
    [InlineData("PivotLabelFilterDialog.xaml", "LabelFilterKindBox", "LabelFilterValueBox", "LabelFilterValue2Box")]
    [InlineData("PivotValueFilterDialog.xaml", "ValueFilterKindBox", "ValueFilterValueBox", "ValueFilterValue2Box")]
    public void Dialog_ExposesAccessKeyedFieldsAndButtons(
        string xamlFile,
        string conditionTarget,
        string valueTarget,
        string andTarget)
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml(xamlFile);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "_Operator:", conditionTarget);
        AssertLabelTargets(document, presentation, "_Value:", valueTarget);
        AssertLabelTargets(document, presentation, "_And:", andTarget);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }

    [Fact]
    public void PivotFieldFilterDialog_ExposesAccessKeyedSearchChecklistAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotFieldFilterDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "_Search:", "FilterSearchBox");

        document.Descendants(presentation + "CheckBox")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain("Select _All");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);
    }

    [Fact]
    public void PivotFieldFilterDialogOpenedFromKeyboard_FocusesSearchBox()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotFieldFilterDialog.xaml.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FilterSearchBox.Focus();");
        source.Should().Contain("Keyboard.Focus(FilterSearchBox);");
    }

    [Fact]
    public void PivotFieldFilterDialog_SelectAllCheckboxShowsMixedStateForPartialSelection()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotFieldFilterDialog(["East", "West"], selectedItems: ["East"]);
            dialog.Show();
            try
            {
                var selectAll = GetControl<CheckBox>(dialog, "SelectAllCheckBox");

                selectAll.IsThreeState.Should().BeTrue();
                selectAll.IsChecked.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("PivotLabelFilterDialog.xaml.cs", "LabelFilterKindBox")]
    [InlineData("PivotValueFilterDialog.xaml.cs", "ValueFilterKindBox")]
    public void PivotConditionDialogOpenedFromKeyboard_FocusesOperatorChoice(string sourceFile, string target)
    {
        var source = DialogSourceTestSupport.ReadHostSources(sourceFile);

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain($"{target}.Focus();");
        source.Should().Contain($"Keyboard.Focus({target});");
    }

    [Theory]
    [InlineData("PivotLabelFilterDialog.xaml.cs", "ResolveInvalidLabelValue")]
    [InlineData("PivotValueFilterDialog.xaml.cs", "ResolveInvalidValueFilterInput")]
    public void PivotConditionDialogInvalidCriteria_RefocusesAndSelectsValueBox(string sourceFile, string helperName)
    {
        var source = DialogSourceTestSupport.ReadHostSources(sourceFile);

        source.Should().Contain($"{helperName}(");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(");
        source.Should().Contain("target);");
    }

    [Theory]
    [InlineData(
        "PivotLabelFilterDialog.xaml",
        "LabelFilterKindBox",
        "PivotLabelFilterOperatorBox",
        "LabelFilterValueBox",
        "PivotLabelFilterValueBox",
        "LabelFilterValue2Box",
        "PivotLabelFilterEndingValueBox")]
    [InlineData(
        "PivotValueFilterDialog.xaml",
        "ValueFilterKindBox",
        "PivotValueFilterOperatorBox",
        "ValueFilterValueBox",
        "PivotValueFilterValueBox",
        "ValueFilterValue2Box",
        "PivotValueFilterEndingValueBox")]
    public void PivotConditionDialogs_ExposeAutomationIdsAndHelpText(
        string xamlFile,
        string operatorName,
        string operatorAutomationId,
        string valueName,
        string valueAutomationId,
        string endingValueName,
        string endingValueAutomationId)
    {
        var document = XDocument.Parse(DialogSourceTestSupport.ReadHostSources(xamlFile));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        AssertAutomation(document, presentation, xaml, operatorName, operatorAutomationId);
        AssertAutomation(document, presentation, xaml, valueName, valueAutomationId);
        AssertAutomation(document, presentation, xaml, endingValueName, endingValueAutomationId);
    }

    [Fact]
    public void PivotLabelFilterDialog_ShowsSecondValueOnlyForBetween()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotLabelFilterDialog(sourceFieldIndex: 0);
            var operatorBox = GetControl<ComboBox>(dialog, "LabelFilterKindBox");
            var secondLabel = GetControl<Label>(dialog, "LabelFilterValue2Label");
            var secondValue = GetControl<TextBox>(dialog, "LabelFilterValue2Box");

            secondLabel.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.IsEnabled.Should().BeFalse();

            operatorBox.SelectedItem = "Between";

            secondLabel.Visibility.Should().Be(Visibility.Visible);
            secondValue.Visibility.Should().Be(Visibility.Visible);
            secondValue.IsEnabled.Should().BeTrue();

            operatorBox.SelectedItem = "Equals";

            secondLabel.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void PivotLabelFilterDialog_IgnoresStaleSecondValueForSingleValueOperators()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotLabelFilterDialog(sourceFieldIndex: 0);
            GetControl<ComboBox>(dialog, "LabelFilterKindBox").SelectedItem = "Contains";
            GetControl<TextBox>(dialog, "LabelFilterValueBox").Text = "East";
            GetControl<TextBox>(dialog, "LabelFilterValue2Box").Text = "West";

            InvokeDialogHandler(dialog, "OkButton_Click");

            dialog.ResultFilter.Should().Be(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "East", null));
        });
    }

    [Fact]
    public void PivotValueFilterDialog_HidesUnusedValueInputsForSelectedOperator()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotValueFilterDialog(sourceFieldIndex: 0);
            var operatorBox = GetControl<ComboBox>(dialog, "ValueFilterKindBox");
            var valueLabel = GetControl<Label>(dialog, "ValueFilterValueLabel");
            var valueBox = GetControl<TextBox>(dialog, "ValueFilterValueBox");
            var secondLabel = GetControl<Label>(dialog, "ValueFilterValue2Label");
            var secondValue = GetControl<TextBox>(dialog, "ValueFilterValue2Box");

            valueLabel.Visibility.Should().Be(Visibility.Visible);
            valueBox.Visibility.Should().Be(Visibility.Visible);
            valueBox.IsEnabled.Should().BeTrue();
            secondLabel.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.IsEnabled.Should().BeFalse();

            operatorBox.SelectedItem = "Between";

            valueLabel.Visibility.Should().Be(Visibility.Visible);
            valueBox.Visibility.Should().Be(Visibility.Visible);
            secondLabel.Visibility.Should().Be(Visibility.Visible);
            secondValue.Visibility.Should().Be(Visibility.Visible);
            secondValue.IsEnabled.Should().BeTrue();

            operatorBox.SelectedItem = "Above Average";

            valueLabel.Visibility.Should().Be(Visibility.Collapsed);
            valueBox.Visibility.Should().Be(Visibility.Collapsed);
            valueBox.IsEnabled.Should().BeFalse();
            secondLabel.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.Visibility.Should().Be(Visibility.Collapsed);
            secondValue.IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_ExposesAccessKeyedFieldsTabsAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotValueFieldSettingsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "Custom _Name:", "CustomNameBox");
        AssertLabelTargets(document, presentation, "_Summarize value field by:", "SummaryFunctionBox");
        AssertLabelTargets(document, presentation, "Show values _as:", "ShowValuesAsBox");
        AssertLabelTargets(document, presentation, "_Base field:", "BaseFieldBox");
        AssertLabelTargets(document, presentation, "Base _item:", "BaseItemBox");
        AssertLabelTargets(document, presentation, "_Number format:", "NumberFormatPresetBox");

        document.Descendants(presentation + "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Contain(["_Summarize Values By", "Show Values _As", "_Number Format"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_ExposesAutomationIdsAndHelpText()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotValueFieldSettingsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        AssertAutomation(document, presentation, xaml, "CustomNameBox", "PivotValueCustomNameBox");
        AssertAutomation(document, presentation, xaml, "SummaryFunctionBox", "PivotValueSummaryFunctionBox");
        AssertAutomation(document, presentation, xaml, "ShowValuesAsBox", "PivotValueShowValuesAsBox");
        AssertAutomation(document, presentation, xaml, "BaseFieldBox", "PivotValueBaseFieldBox");
        AssertAutomation(document, presentation, xaml, "BaseItemBox", "PivotValueBaseItemBox");
        AssertAutomation(document, presentation, xaml, "NumberFormatPresetBox", "PivotValueNumberFormatPresetBox");
        AssertAutomation(document, presentation, xaml, "NumberFormatButton", "PivotValueNumberFormatButton");
        AssertAutomation(document, presentation, xaml, "NumberFormatBox", "PivotValueNumberFormatIdBox");
        AssertAutomation(document, presentation, xaml, "NumberFormatCodeBox", "PivotValueCustomFormatCodeBox");
        AssertAutomation(document, presentation, xaml, "OkButton", "PivotValueFieldSettingsOkButton");

        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "_Cancel")
            .Attribute("AutomationProperties.AutomationId")?.Value
            .Should()
            .Be("PivotValueFieldSettingsCancelButton");
    }

    [Fact]
    public void PivotValueFieldSettingsDialogOpenedFromKeyboard_FocusesCustomName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotValueFieldSettingsDialog.xaml.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(CustomNameBox);");
    }

    [Fact]
    public void PivotValueFieldSettingsDialogInvalidInputs_SelectRelevantTabAndField()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("PivotValueFieldSettingsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("PivotValueFieldSettingsDialog.xaml.cs");

        xaml.Should().Contain("<TabControl x:Name=\"ValueFieldTabs\"");
        xaml.Should().Contain("<TabItem x:Name=\"ShowValuesAsTab\" Header=\"Show Values _As\"");
        xaml.Should().Contain("<TabItem x:Name=\"NumberFormatTab\" Header=\"_Number Format\"");
        source.Should().Contain("FocusInvalidNumberFormatInput();");
        source.Should().Contain("FocusInvalidShowValuesAsInput(baseFieldIndex);");
        source.Should().Contain("ValueFieldTabs.SelectedItem = NumberFormatTab;");
        source.Should().Contain("ValueFieldTabs.SelectedItem = ShowValuesAsTab;");
        source.Should().Contain("DialogFocus.FocusAndSelect(NumberFormatBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(BaseItemBox);");
        source.Should().Contain("Keyboard.Focus(BaseFieldBox);");
        source.Should().NotContain("private static void FocusAndSelect(System.Windows.Controls.TextBox target)");
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_HidesBaseFieldsUntilShowValuesAsNeedsThem()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotValueFieldSettingsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = DialogSourceTestSupport.ReadHostSources("PivotValueFieldSettingsDialog.xaml.cs");

        var baseFieldPanel = document.Descendants(presentation + "StackPanel")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "BaseFieldPanel");
        var baseItemPanel = document.Descendants(presentation + "StackPanel")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "BaseItemPanel");

        baseFieldPanel.Attribute("Visibility")?.Value.Should().Be("Collapsed");
        baseFieldPanel.Attribute("IsEnabled")?.Value.Should().Be("False");
        baseItemPanel.Attribute("Visibility")?.Value.Should().Be("Collapsed");
        baseItemPanel.Attribute("IsEnabled")?.Value.Should().Be("False");

        document.Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "ShowValuesAsBox")
            .Attribute("SelectionChanged")?.Value
            .Should()
            .Be("ShowValuesAsBox_SelectionChanged");
        source.Should().Contain("UpdateBaseFieldState()");
        source.Should().Contain("PivotValueFieldPlanner.ShowValuesAsFromIndex");
        source.Should().Contain("ShowValuesAsRequiresBaseField");
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_UsesNumberFormatAffordanceInsteadOfRawIds()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotValueFieldSettingsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = DialogSourceTestSupport.ReadHostSources("PivotValueFieldSettingsDialog.xaml.cs");

        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "NumberFormatButton")
            .Attribute("Content")?.Value
            .Should()
            .Be("_Number Format...");

        document.Descendants(presentation + "Label")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .NotContain(["Number format _ID:", "Custom format _code:"]);
        document.Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Should()
            .NotContain("Choose how values appear in the PivotTable.");

        document.Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "NumberFormatBox")
            .Attribute("Visibility")?.Value
            .Should()
            .Be("Collapsed");
        document.Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "NumberFormatCodeBox")
            .Attribute("Visibility")?.Value
            .Should()
            .Be("Collapsed");
        source.Should().Contain("NumberFormatButton_Click");
        source.Should().Contain("new FormatCellsDialog(style, FormatCellsDialogTab.Number)");
        source.Should().Contain("NumberFormatCodeBox.Text = numberFormat");
        source.Should().Contain("DefaultCustomNumberFormatId");
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_PresetSelectionClearsStaleCustomFormatCode()
    {
        StaTestRunner.Run(() =>
        {
            var field = new PivotDataFieldModel(
                SourceFieldIndex: 0,
                Name: "Sum of Sales",
                SummaryFunction: "sum",
                NumberFormatId: 164,
                NumberFormatCode: "#,##0.0 \"kg\"");
            var dialog = new PivotValueFieldSettingsDialog(field);

            GetControl<ComboBox>(dialog, "NumberFormatPresetBox").SelectedItem = "Currency";
            GetControl<TextBox>(dialog, "NumberFormatBox").Text.Should().Be("7");
            GetControl<TextBox>(dialog, "NumberFormatCodeBox").Text.Should().BeEmpty();
        });
    }

    [Fact]
    public void PivotFieldFilterDialog_ExposesItemLabelAndValueFilterTabsWithActions()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotFieldFilterDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = DialogSourceTestSupport.ReadHostSources("PivotFieldFilterDialog.xaml.cs");

        document.Descendants(presentation + "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Equal("Select _Items", "_Label Filters", "_Value Filters");

        document.Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Should()
            .Contain([
                "Choose items to show:",
                "No item filter",
                "No label filter",
                "No value filter",
                "Manage label filters for this PivotTable field.",
                "Manage value filters for this PivotTable field."
            ]);

        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "LabelFilterButton")
            .Attribute("Click")?.Value
            .Should()
            .Be("LabelFilterButton_Click");

        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "ValueFilterButton")
            .Attribute("Click")?.Value
            .Should()
            .Be("ValueFilterButton_Click");

        document.Descendants(presentation + "Button")
            .Where(element => element.Attribute(xaml + "Name") is not null)
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should()
            .Contain([
                "ClearItemFilterButton",
                "ClearFieldFiltersButton",
                "RemoveLabelFilterButton",
                "RemoveValueFilterButton"
            ]);

        source.Should().Contain("public PivotFieldFilterDialogAction RequestedAction");
        source.Should().Contain("LabelFilterButton_Click");
        source.Should().Contain("ValueFilterButton_Click");
        source.Should().Contain("RemoveLabelFilterButton_Click");
        source.Should().Contain("RemoveValueFilterButton_Click");
        source.Should().Contain("ClearFieldFiltersButton_Click");
    }

    [Fact]
    public void PivotFieldFilterDialog_ShowsActiveFilterStateAndFocusesRequestedTab()
    {
        StaTestRunner.Run(() =>
        {
            var state = new PivotFieldFilterState(
                "Region",
                0,
                ["East", "West"],
                ["East"],
                new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "Ea"),
                new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 10, SourceFieldIndex: 0),
                [new PivotDataFieldModel(2, "Sum of Amount", "sum")]);
            var dialog = new PivotFieldFilterDialog(
                ["East", "West"],
                ["East"],
                filterState: state,
                initialTab: PivotFieldFilterDialogTab.ValueFilters);
            dialog.Show();
            try
            {
                GetControl<TextBlock>(dialog, "ItemFilterSummaryText").Text.Should().Contain("East");
                GetControl<TextBlock>(dialog, "LabelFilterSummaryText").Text.Should().Contain("contains \"Ea\"");
                GetControl<TextBlock>(dialog, "ValueFilterSummaryText").Text.Should().Contain("Sum of Amount > 10");
                GetControl<Button>(dialog, "ClearFieldFiltersButton").Content.Should().Be("Clear Filters from \"Region\"");
                GetControl<Button>(dialog, "ClearItemFilterButton").IsEnabled.Should().BeTrue();
                GetControl<Button>(dialog, "RemoveLabelFilterButton").IsEnabled.Should().BeTrue();
                GetControl<Button>(dialog, "RemoveValueFilterButton").IsEnabled.Should().BeTrue();
                GetControl<Button>(dialog, "LabelFilterButton").Content.Should().Be("Edit Label Filter...");
                GetControl<Button>(dialog, "ValueFilterButton").Content.Should().Be("Edit Value Filter...");
                GetControl<TabControl>(dialog, "FilterTabs").SelectedItem
                    .Should()
                    .Be(GetControl<TabItem>(dialog, "ValueFiltersTab"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData("PivotLabelFilterDialog.xaml", "Show items for which the label", "_Operator:", "LabelFilterKindBox")]
    [InlineData("PivotValueFilterDialog.xaml", "Show items for which the value", "_Operator:", "ValueFilterKindBox")]
    public void PivotConditionDialogs_UseExcelLikeSectionLabels(
        string xamlFile,
        string sectionText,
        string operatorLabel,
        string operatorTarget)
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml(xamlFile);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Should()
            .Contain(sectionText);

        AssertLabelTargets(document, presentation, operatorLabel, operatorTarget);
    }

    private static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
    {
        var label = document
            .Descendants(presentation + "Label")
            .Single(element => element.Attribute("Content")?.Value == content);

        label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
    }

    private static void AssertAutomation(
        XDocument document,
        XNamespace presentation,
        XNamespace xaml,
        string name,
        string automationId)
    {
        var element = document
            .Descendants()
            .Single(element => element.Name.Namespace == presentation && element.Attribute(xaml + "Name")?.Value == name);

        element.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(automationId);
        element.Attribute("AutomationProperties.HelpText")?.Value.Should().NotBeNullOrWhiteSpace();
    }

    private static T GetControl<T>(PivotValueFieldSettingsDialog dialog, string name)
    {
        return GetControl<T>((object)dialog, name);
    }

    private static T GetControl<T>(PivotFieldFilterDialog dialog, string name)
    {
        return GetControl<T>((object)dialog, name);
    }

    private static T GetControl<T>(object dialog, string name)
    {
        var field = dialog.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        field.Should().NotBeNull($"control {name} should exist");
        var value = field!.GetValue(dialog);
        value.Should().BeOfType<T>();
        return (T)value!;
    }

    private static void InvokeDialogHandler(object dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, methodName);

}
