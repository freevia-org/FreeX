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

        AssertLabelTargets(document, presentation, "_Header preset:", "HeaderPresetBox");
        AssertLabelTargets(document, presentation, "_Footer preset:", "FooterPresetBox");
        AssertLabelTargets(document, presentation, "Header _left:", "HeaderLeftBox");
        AssertLabelTargets(document, presentation, "Header _center:", "HeaderCenterBox");
        AssertLabelTargets(document, presentation, "Header _right:", "HeaderRightBox");
        AssertLabelTargets(document, presentation, "Footer l_eft:", "FooterLeftBox");
        AssertLabelTargets(document, presentation, "Footer c_enter:", "FooterCenterBox");
        AssertLabelTargets(document, presentation, "Footer r_ight:", "FooterRightBox");
        AssertLabelTargets(document, presentation, "First h_eader:", "FirstHeaderBox");
        AssertLabelTargets(document, presentation, "First f_ooter:", "FirstFooterBox");
        AssertLabelTargets(document, presentation, "Even hea_der:", "EvenHeaderBox");
        AssertLabelTargets(document, presentation, "Even foot_er:", "EvenFooterBox");

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

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element =>
                    element.Attribute("Content")?.Value == content &&
                    element.Attribute("Target")?.Value == $"{{Binding ElementName={target}}}");

            label.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData("_Page number", "&[Page]")]
    [InlineData("Number of pa_ges", "&[Pages]")]
    [InlineData("_Date", "&[Date]")]
    [InlineData("_Time", "&[Time]")]
    [InlineData("File _path", "&[Path]&[File]")]
    [InlineData("File _name", "&[File]")]
    [InlineData("_Sheet name", "&[Tab]")]
    [InlineData("P_icture", "&[Picture]")]
    [InlineData("For_mat picture", "&[Picture]")]
    public void Dialog_ExposesExcelHeaderFooterTokenButtons(string label, string token)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document.Descendants(presentation + "Button").ToList();

        buttons.Select(element => element.Attribute("Content")?.Value).Should().Contain(label);
        buttons.Select(element => element.Attribute("Tag")?.Value).Should().Contain(token);
    }

    [Fact]
    public void Dialog_ExposesHeaderFooterPresets()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "ComboBox")
            .Select(element => element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value)
            .Should()
            .Contain(["HeaderPresetBox", "FooterPresetBox"]);
    }

    [Fact]
    public void InsertToken_InsertsAtCaret()
    {
        HeaderFooterDialog.InsertToken("Page  of", caretIndex: 5, "&[Page]").Should().Be("Page &[Page] of");
    }
}
