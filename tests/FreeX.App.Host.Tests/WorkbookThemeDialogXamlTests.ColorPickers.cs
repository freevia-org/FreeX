using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookThemeDialogXamlTests
{
    [Fact]
    public void Dialog_ExposesColorPickerButtonsForEveryThemeColorSlot()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("WorkbookThemeDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookThemeDialog.xaml.cs"));

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
    public void ThemeColorFields_ExposeAutomationIdsNamesAndFormatHelp()
    {
        var expectedFields = new (string TextBoxName, string TextAutomationId, string AutomationName, string ButtonName, string ButtonAutomationId, string ButtonHelpText)[]
        {
            ("Dark1ColorBox", "WorkbookThemeDark1ColorBox", "Dark 1 theme color", "Dark1ColorPickerButton", "WorkbookThemeDark1ColorPickerButton", "Pick the Dark 1 theme color."),
            ("Light1ColorBox", "WorkbookThemeLight1ColorBox", "Light 1 theme color", "Light1ColorPickerButton", "WorkbookThemeLight1ColorPickerButton", "Pick the Light 1 theme color."),
            ("Dark2ColorBox", "WorkbookThemeDark2ColorBox", "Dark 2 theme color", "Dark2ColorPickerButton", "WorkbookThemeDark2ColorPickerButton", "Pick the Dark 2 theme color."),
            ("Light2ColorBox", "WorkbookThemeLight2ColorBox", "Light 2 theme color", "Light2ColorPickerButton", "WorkbookThemeLight2ColorPickerButton", "Pick the Light 2 theme color."),
            ("Accent1ColorBox", "WorkbookThemeAccent1ColorBox", "Accent 1 theme color", "Accent1ColorPickerButton", "WorkbookThemeAccent1ColorPickerButton", "Pick the Accent 1 theme color."),
            ("Accent2ColorBox", "WorkbookThemeAccent2ColorBox", "Accent 2 theme color", "Accent2ColorPickerButton", "WorkbookThemeAccent2ColorPickerButton", "Pick the Accent 2 theme color."),
            ("Accent3ColorBox", "WorkbookThemeAccent3ColorBox", "Accent 3 theme color", "Accent3ColorPickerButton", "WorkbookThemeAccent3ColorPickerButton", "Pick the Accent 3 theme color."),
            ("Accent4ColorBox", "WorkbookThemeAccent4ColorBox", "Accent 4 theme color", "Accent4ColorPickerButton", "WorkbookThemeAccent4ColorPickerButton", "Pick the Accent 4 theme color."),
            ("Accent5ColorBox", "WorkbookThemeAccent5ColorBox", "Accent 5 theme color", "Accent5ColorPickerButton", "WorkbookThemeAccent5ColorPickerButton", "Pick the Accent 5 theme color."),
            ("Accent6ColorBox", "WorkbookThemeAccent6ColorBox", "Accent 6 theme color", "Accent6ColorPickerButton", "WorkbookThemeAccent6ColorPickerButton", "Pick the Accent 6 theme color."),
            ("HyperlinkColorBox", "WorkbookThemeHyperlinkColorBox", "Hyperlink theme color", "HyperlinkColorPickerButton", "WorkbookThemeHyperlinkColorPickerButton", "Pick the Hyperlink theme color."),
            ("FollowedHyperlinkColorBox", "WorkbookThemeFollowedHyperlinkColorBox", "Followed Hyperlink theme color", "FollowedHyperlinkColorPickerButton", "WorkbookThemeFollowedHyperlinkColorPickerButton", "Pick the Followed Hyperlink theme color.")
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new WorkbookThemeDialog(WorkbookTheme.Office);
            try
            {
                foreach (var field in expectedFields)
                {
                    var colorBox = dialog.FindName(field.TextBoxName).Should().BeOfType<TextBox>().Subject;
                    AutomationProperties.GetAutomationId(colorBox).Should().Be(field.TextAutomationId);
                    AutomationProperties.GetName(colorBox).Should().Be(field.AutomationName);
                    AutomationProperties.GetHelpText(colorBox).Should().Be("Enter a theme color as a #RRGGBB value.");

                    var pickerButton = dialog.FindName(field.ButtonName).Should().BeOfType<Button>().Subject;
                    AutomationProperties.GetAutomationId(pickerButton).Should().Be(field.ButtonAutomationId);
                    AutomationProperties.GetHelpText(pickerButton).Should().Be(field.ButtonHelpText);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ColorPickerButtons_ExposeSwatchAffordance()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("WorkbookThemeDialog.xaml");

        xaml.Should().Contain("ToolTip=\"Pick color\"");
        xaml.Should().NotContain("Content=\"...\"");
    }

    [Fact]
    public void ColorPickerSwatches_UpdateWithThemeColors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookThemeDialog.xaml.cs"));

        source.Should().Contain("UpdateColorPickerSwatches();");
        source.Should().Contain("private void UpdateColorPickerSwatches()");
        source.Should().Contain("ThemeColorFields()");
        source.Should().Contain("field.Button.Background = new SolidColorBrush(ToMediaColor(ParsePreviewColor(field.TextBox.Text)));");
    }
}
