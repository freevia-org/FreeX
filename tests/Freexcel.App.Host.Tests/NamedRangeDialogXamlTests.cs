using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class NamedRangeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedFieldsAndCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "NamedRangeDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GroupBox")
            .Single()
            .Attribute("Header")?.Value.Should().Be("_Defined Names");

        AssertLabelTargets(document, presentation, "_Name:", "NameBox");
        AssertLabelTargets(document, presentation, "_Range:", "RangeBox");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Define", "_Delete", "_Close"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }
}
