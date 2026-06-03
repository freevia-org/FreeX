using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookThemeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesExcelStyleThemeMetadataFields()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("WorkbookThemeDialog.xaml");

        xaml.Should().Contain("AutomationProperties.Name=\"Theme name\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Heading font\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Body font\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Effects\"");
    }

    [Fact]
    public void Dialog_ExposesAllThemeColorSlots()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("WorkbookThemeDialog.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var expectedNames = new[]
        {
            "Dark1ColorBox",
            "Light1ColorBox",
            "Dark2ColorBox",
            "Light2ColorBox",
            "Accent1ColorBox",
            "Accent2ColorBox",
            "Accent3ColorBox",
            "Accent4ColorBox",
            "Accent5ColorBox",
            "Accent6ColorBox",
            "HyperlinkColorBox",
            "FollowedHyperlinkColorBox"
        };

        var actualNames = document
            .Descendants(presentation + "TextBox")
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Where(name => name is not null)
            .ToHashSet();

        foreach (var expectedName in expectedNames)
        {
            actualNames.Should().Contain(expectedName);
        }
    }

    [Fact]
    public void Dialog_SaveButton_IsDefaultAction()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("WorkbookThemeDialog.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var saveButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "SaveButton");

        saveButton.Attribute("IsDefault")?.Value.Should().Be("True");
    }
}
