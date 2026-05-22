using System.IO;
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
        AssertLabelTargets(document, presentation, "First header _left:", "FirstHeaderLeftBox");
        AssertLabelTargets(document, presentation, "First header _center:", "FirstHeaderCenterBox");
        AssertLabelTargets(document, presentation, "First header _right:", "FirstHeaderRightBox");
        AssertLabelTargets(document, presentation, "First footer le_ft:", "FirstFooterLeftBox");
        AssertLabelTargets(document, presentation, "First footer cent_er:", "FirstFooterCenterBox");
        AssertLabelTargets(document, presentation, "First footer righ_t:", "FirstFooterRightBox");
        AssertLabelTargets(document, presentation, "Even header le_ft:", "EvenHeaderLeftBox");
        AssertLabelTargets(document, presentation, "Even header ce_nter:", "EvenHeaderCenterBox");
        AssertLabelTargets(document, presentation, "Even header rig_ht:", "EvenHeaderRightBox");
        AssertLabelTargets(document, presentation, "Even footer lef_t:", "EvenFooterLeftBox");
        AssertLabelTargets(document, presentation, "Even footer cent_er:", "EvenFooterCenterBox");
        AssertLabelTargets(document, presentation, "Even footer rig_ht:", "EvenFooterRightBox");

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
    public void PictureButtons_UseDedicatedPictureHandlers()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "P_icture")
            .Attribute("Click")?.Value
            .Should()
            .Be("PictureButton_Click");
        document.Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "For_mat picture")
            .Attribute("Click")?.Value
            .Should()
            .Be("FormatPictureButton_Click");

        source.Should().Contain("new OpenFileDialog");
        source.Should().Contain("HeaderFooterPictureFormatDialog");
        source.Should().Contain("SetPictureForActiveBox");
    }

    [Fact]
    public void PictureFormatDialog_ExposesExcelLikeSizeControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml.cs"));

        source.Should().Contain("private readonly CheckBox _lockAspectRatioBox");
        source.Should().Contain("Content = \"_Lock aspect ratio\"");
        source.Should().Contain("Content = \"_Reset\"");
        source.Should().Contain("CalculateLockedAspectHeight");
        source.Should().Contain("CalculateLockedAspectWidth");
    }

    [Fact]
    public void PictureFormatDialog_CalculatesLockedAspectSize()
    {
        HeaderFooterPictureFormatDialog.CalculateLockedAspectHeight(200, originalWidth: 100, originalHeight: 50)
            .Should()
            .Be(100);
        HeaderFooterPictureFormatDialog.CalculateLockedAspectWidth(75, originalWidth: 100, originalHeight: 50)
            .Should()
            .Be(150);
    }

    [Fact]
    public void FirstAndEvenHeadersAndFooters_UseSectionBoxesWithoutPipeParsing()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "HeaderFooterDialog.xaml.cs"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[]
        {
            "FirstHeaderLeftBox",
            "FirstHeaderCenterBox",
            "FirstHeaderRightBox",
            "FirstFooterLeftBox",
            "FirstFooterCenterBox",
            "FirstFooterRightBox",
            "EvenHeaderLeftBox",
            "EvenHeaderCenterBox",
            "EvenHeaderRightBox",
            "EvenFooterLeftBox",
            "EvenFooterCenterBox",
            "EvenFooterRightBox"
        })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == name)
                .Should().BeTrue($"{name} should exist so first/even pages keep left/center/right sections");
        }

        foreach (var oldFlattenedName in new[] { "FirstHeaderBox", "FirstFooterBox", "EvenHeaderBox", "EvenFooterBox" })
        {
            document.Descendants()
                .Any(element => element.Attribute(x + "Name")?.Value == oldFlattenedName)
                .Should().BeFalse($"{oldFlattenedName} loses literal pipe characters and should be replaced");
        }

        source.Should().NotContain("Split('|'");
        source.Should().NotContain("ToCombinedText");
        source.Should().NotContain("FromCombinedText");
        source.Should().Contain("FirstPageHeader = new WorksheetHeaderFooter(");
        source.Should().Contain("EvenPageFooter = new WorksheetHeaderFooter(");
    }

    [Fact]
    public void InsertToken_InsertsAtCaret()
    {
        HeaderFooterDialog.InsertToken("Page  of", caretIndex: 5, "&[Page]").Should().Be("Page &[Page] of");
    }
}
