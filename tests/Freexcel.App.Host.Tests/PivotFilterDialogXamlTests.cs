using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", xamlFile));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotFieldFilterDialog.xaml"));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotFieldFilterDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("FilterSearchBox.Focus();");
        source.Should().Contain("Keyboard.Focus(FilterSearchBox);");
    }

    [Theory]
    [InlineData("PivotLabelFilterDialog.xaml.cs", "LabelFilterKindBox")]
    [InlineData("PivotValueFilterDialog.xaml.cs", "ValueFilterKindBox")]
    public void PivotConditionDialogOpenedFromKeyboard_FocusesOperatorChoice(string sourceFile, string target)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", sourceFile));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain($"{target}.Focus();");
        source.Should().Contain($"Keyboard.Focus({target});");
    }

    [Theory]
    [InlineData("PivotLabelFilterDialog.xaml.cs", "FocusInvalidLabelValue")]
    [InlineData("PivotValueFilterDialog.xaml.cs", "FocusInvalidValueFilterInput")]
    public void PivotConditionDialogInvalidCriteria_RefocusesAndSelectsValueBox(string sourceFile, string helperName)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", sourceFile));

        source.Should().Contain($"{helperName}(");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_ExposesAccessKeyedFieldsTabsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml"));
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
    public void PivotValueFieldSettingsDialogOpenedFromKeyboard_FocusesCustomName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("CustomNameBox.Focus();");
        source.Should().Contain("CustomNameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(CustomNameBox);");
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_HidesBaseFieldsUntilShowValuesAsNeedsThem()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml.cs"));

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
        source.Should().Contain("ShowValuesAsRequiresBaseField");
    }

    [Fact]
    public void PivotValueFieldSettingsDialog_UsesNumberFormatAffordanceInsteadOfRawIds()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml.cs"));

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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotFieldFilterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotFieldFilterDialog.xaml.cs"));

        document.Descendants(presentation + "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Equal("Select _Items", "_Label Filters", "_Value Filters");

        document.Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
            .Should()
            .Contain([
                "Choose items to show:",
                "Open the full label filter dialog to filter PivotTable items by their captions.",
                "Open the full value filter dialog to filter PivotTable items by summarized values."
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

        document.Descendants(presentation + "ComboBox")
            .Any(element => element.Attribute("IsEnabled")?.Value == "False")
            .Should()
            .BeFalse("the field checklist should not show disabled label/value filter previews");

        source.Should().Contain("public PivotFieldFilterDialogAction RequestedAction");
        source.Should().Contain("LabelFilterButton_Click");
        source.Should().Contain("ValueFilterButton_Click");
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", xamlFile));
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

    private static T GetControl<T>(PivotValueFieldSettingsDialog dialog, string name)
    {
        var field = typeof(PivotValueFieldSettingsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"control {name} should exist");
        var value = field!.GetValue(dialog);
        value.Should().BeOfType<T>();
        return (T)value!;
    }

}
