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
    public void DialogOpenedFromKeyboard_FocusesThemeNameBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("ThemeNameBox.Focus();");
        source.Should().Contain("ThemeNameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(ThemeNameBox);");
    }

    [Fact]
    public void DialogInvalidThemeColor_FocusesInvalidColorBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml.cs"));

        source.Should().Contain("TryReadThemeColor(Dark1ColorBox, out var dark1)");
        source.Should().Contain("private bool TryReadThemeColor(TextBox colorBox, out CellColor color)");
        source.Should().Contain("FocusInvalidColorInput(colorBox);");
        source.Should().Contain("private static void FocusInvalidColorInput(TextBox colorBox)");
        source.Should().Contain("colorBox.Focus();");
        source.Should().Contain("colorBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(colorBox);");
    }

    [Fact]
    public void Dialog_ExposesKeyboardAccessKeysForThemeFieldsColorsAndButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Save", "_Cancel", "_Office", "Freexcel _Colorful", "_Grayscale"]);

        AssertLabelTargets(document, presentation, "_Name:", "ThemeNameBox");
        AssertLabelTargets(document, presentation, "_Heading font:", "HeadingFontBox");
        AssertLabelTargets(document, presentation, "_Body font:", "BodyFontBox");
        AssertLabelTargets(document, presentation, "_Effects:", "EffectsBox");

        AssertLabelTargets(document, presentation, "_Dark 1", "Dark1ColorBox");
        AssertLabelTargets(document, presentation, "_Light 1", "Light1ColorBox");
        AssertLabelTargets(document, presentation, "D_ark 2", "Dark2ColorBox");
        AssertLabelTargets(document, presentation, "L_ight 2", "Light2ColorBox");
        AssertLabelTargets(document, presentation, "_Accent 1", "Accent1ColorBox");
        AssertLabelTargets(document, presentation, "A_ccent 2", "Accent2ColorBox");
        AssertLabelTargets(document, presentation, "Ac_cent 3", "Accent3ColorBox");
        AssertLabelTargets(document, presentation, "Acc_ent 4", "Accent4ColorBox");
        AssertLabelTargets(document, presentation, "Acce_nt 5", "Accent5ColorBox");
        AssertLabelTargets(document, presentation, "Accen_t 6", "Accent6ColorBox");
        AssertLabelTargets(document, presentation, "H_yperlink", "HyperlinkColorBox");
        AssertLabelTargets(document, presentation, "Followed Hype_rlink", "FollowedHyperlinkColorBox");

        static void AssertLabelTargets(XDocument document, XNamespace presentation, string content, string target)
        {
            var label = document
                .Descendants(presentation + "Label")
                .Single(element => element.Attribute("Content")?.Value == content);

            label.Attribute("Target")?.Value.Should().Be($"{{Binding ElementName={target}}}");
        }
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

    [Fact]
    public void Dialog_ExposesExcelLikeThemePreviewPane()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "WorkbookThemeDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"ThemePreviewPane\"");
        xaml.Should().Contain("x:Name=\"PreviewHeadingText\"");
        xaml.Should().Contain("x:Name=\"PreviewBodyText\"");
        xaml.Should().Contain("x:Name=\"PreviewAccentStrip\"");
        xaml.Should().Contain("Sample");
        source.Should().Contain("UpdatePreview");
        source.Should().Contain("WirePreviewRefresh");
        source.Should().Contain("HeadingFontBox.SelectionChanged += (_, _) => UpdatePreview()");
        source.Should().Contain("HeadingFontBox.AddHandler(TextBox.TextChangedEvent");
        source.Should().Contain("colorBox.TextChanged += (_, _) => UpdatePreview()");
        source.Should().Contain("ThemeColorTextBoxes");
        source.Should().Contain("PreviewHeadingText.FontFamily");
        source.Should().Contain("PreviewAccentStrip");
    }

}
