using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class HeaderFooterDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesAccessKeysForOptionsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "CheckBox")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain([
                "_Different first page",
                "Different _odd and even pages",
                "_Scale with document",
                "_Align with page margins"]);

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_OK", "_Cancel"]);
    }
}
