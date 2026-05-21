using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FindReplaceDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeyedFieldsOptionsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FindReplaceDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertLabelTargets(document, presentation, "_Find:", "FindBox");
        AssertLabelTargets(document, presentation, "_Replace:", "ReplaceBox");

        document.Descendants(presentation + "CheckBox")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["Match _case", "_Entire cell", "Search _formulas"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Find Next", "_Replace All", "_Close"]);

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
    }
}
