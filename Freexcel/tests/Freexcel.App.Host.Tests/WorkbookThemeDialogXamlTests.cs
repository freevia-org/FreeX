using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class WorkbookThemeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesExcelStyleThemeMetadataFields()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml"));

        xaml.Should().Contain("AutomationProperties.Name=\"Theme name\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Heading font\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Body font\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Effects\"");
    }

    [Fact]
    public void Dialog_ExposesAllThemeColorSlots()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var saveButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "SaveButton");

        saveButton.Attribute("IsDefault")?.Value.Should().Be("True");
    }

    [Fact]
    public void Dialog_ExposesThemePresetButtonsBackedByWorkflow()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"OfficePresetButton\"");
        xaml.Should().Contain("x:Name=\"ColorfulPresetButton\"");
        xaml.Should().Contain("x:Name=\"GrayscalePresetButton\"");
        xaml.Should().Contain("Click=\"OfficePresetButton_Click\"");
        xaml.Should().Contain("Click=\"ColorfulPresetButton_Click\"");
        xaml.Should().Contain("Click=\"GrayscalePresetButton_Click\"");

        source.Should().Contain("OfficePresetButton_Click");
        source.Should().Contain("LoadTheme(WorkbookTheme.Office)");
        source.Should().Contain("ColorfulPresetButton_Click");
        source.Should().Contain("LoadTheme(WorkbookThemeWorkflow.CreateColorfulTheme())");
        source.Should().Contain("GrayscalePresetButton_Click");
        source.Should().Contain("LoadTheme(WorkbookThemeWorkflow.CreateGrayscaleTheme())");
    }

    [Fact]
    public void Dialog_ExposesColorPickerButtonsForEveryThemeColorSlot()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml.cs"));

        var expectedPickerNames = new[]
        {
            "Dark1ColorPickerButton",
            "Light1ColorPickerButton",
            "Dark2ColorPickerButton",
            "Light2ColorPickerButton",
            "Accent1ColorPickerButton",
            "Accent2ColorPickerButton",
            "Accent3ColorPickerButton",
            "Accent4ColorPickerButton",
            "Accent5ColorPickerButton",
            "Accent6ColorPickerButton",
            "HyperlinkColorPickerButton",
            "FollowedHyperlinkColorPickerButton"
        };

        foreach (var expectedPickerName in expectedPickerNames)
        {
            xaml.Should().Contain($"x:Name=\"{expectedPickerName}\"");
        }

        xaml.Should().Contain("Click=\"ThemeColorPickerButton_Click\"");
        source.Should().Contain("ThemeColorPickerButton_Click");
        source.Should().Contain("new ColorPickerDialog");
        source.Should().Contain("WorkbookThemeDialogColorCodec.FormatColor(dialog.SelectedColor.Value)");
    }

}
