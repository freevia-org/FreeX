using System.Xml.Linq;
using FluentAssertions;

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

        AssertLabelTargets(document, presentation, "_Condition:", conditionTarget);
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
    public void PivotValueFieldSettingsDialog_ExposesAccessKeyedFieldsTabsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "Custom _Name:", "CustomNameBox");
        AssertLabelTargets(document, presentation, "_Summarize value field by:", "SummaryFunctionBox");
        AssertLabelTargets(document, presentation, "Show values _as:", "ShowValuesAsBox");
        AssertLabelTargets(document, presentation, "_Base field:", "BaseFieldBox");
        AssertLabelTargets(document, presentation, "Base _item:", "BaseItemBox");
        AssertLabelTargets(document, presentation, "_Format preset:", "NumberFormatPresetBox");
        AssertLabelTargets(document, presentation, "Number format _ID:", "NumberFormatBox");
        AssertLabelTargets(document, presentation, "Custom format _code:", "NumberFormatCodeBox");

        document.Descendants(presentation + "TabItem")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Contain(["_Summarize Values By", "Show _Values As", "_Number Format"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);
    }

    private static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
    {
        var label = document
            .Descendants(presentation + "Label")
            .Single(element => element.Attribute("Content")?.Value == content);

        label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
    }
}
