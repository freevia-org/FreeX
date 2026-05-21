using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class GoalSeekDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedInputLabelsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoalSeekDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "_Set cell:", "SetCellBox");
        AssertLabelTargets(document, presentation, "_To value:", "ToValueBox");
        AssertLabelTargets(document, presentation, "_By changing cell:", "ChangingCellBox");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("AutomationProperties.Name")?.Value)
            .Should()
            .Contain(["Select set cell reference", "Select changing cell reference"])
            .And.NotContain("Collapse Dialog");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("CommandParameter")?.Value)
            .Should()
            .Contain(["SetCellBox", "ChangingCellBox"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }
}
