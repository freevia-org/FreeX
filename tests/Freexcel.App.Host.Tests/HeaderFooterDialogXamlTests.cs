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

    [Theory]
    [InlineData("Page number", "&[Page]")]
    [InlineData("Number of pages", "&[Pages]")]
    [InlineData("Date", "&[Date]")]
    [InlineData("Time", "&[Time]")]
    [InlineData("File path", "&[Path]&[File]")]
    [InlineData("File name", "&[File]")]
    [InlineData("Sheet name", "&[Tab]")]
    [InlineData("Picture", "&[Picture]")]
    [InlineData("Format picture", "&[Picture]")]
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
