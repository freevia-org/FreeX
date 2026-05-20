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
}
