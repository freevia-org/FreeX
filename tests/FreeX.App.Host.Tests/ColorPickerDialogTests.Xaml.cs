using FreeX.App.Services;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Xml.Linq;

namespace FreeX.App.Host.Tests;

public sealed partial class ColorPickerDialogTests
{
    [Fact]
    public void DialogXaml_ExposesExcelLikePaletteSectionsAndPreview()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("ColorPickerDialog.xaml");
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("ColorPickerDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        xaml.Should().Contain("<TabControl");
        document.Descendants(presentation + "TabItem")
            .Select(tab => (string?)tab.Attribute("Header"))
            .Should()
            .Contain(["_Standard", "_Custom"]);
        xaml.Should().Contain("Theme Colors");
        xaml.Should().Contain("Standard Colors");
        xaml.Should().Contain("Current");
        xaml.Should().Contain("New");
        xaml.Should().Contain("CurrentForegroundPreview");
        xaml.Should().Contain("CurrentBackgroundPreview");
        xaml.Should().Contain("NewForegroundPreview");
        xaml.Should().Contain("NewBackgroundPreview");
        xaml.Should().Contain("ThemeColorsPanel");
        xaml.Should().Contain("StandardColorsPanel");
        xaml.Should().Contain("CustomSpectrumPanel");
        xaml.Should().Contain("CustomLuminositySlider");
    }

    [Fact]
    public void DialogXaml_ExposesKeyboardAccessKeysForCustomColorAndButtons()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("ColorPickerDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var label = document
            .Descendants(presentation + "Label")
            .Single(element => element.Attribute("Content")?.Value == "Custom _color");

        label.Attribute("Target")?.Value.Should().Be("{Binding ElementName=CustomColorTextBox}");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_No Color", "_OK", "_Cancel"]);
    }

    [Fact]
    public void Dialog_ExposesAccessibleNamesForSwatchesAndLuminositySlider()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ColorPickerDialog();
            try
            {
                var themePanel = (Panel)dialog.FindName("ThemeColorsPanel");
                var standardPanel = (Panel)dialog.FindName("StandardColorsPanel");
                var spectrumPanel = (Panel)dialog.FindName("CustomSpectrumPanel");
                var slider = (Slider)dialog.FindName("CustomLuminositySlider");

                var themeSwatch = CellColorPalettePlanner.BuildThemePalette()[4].Shades[0];
                var themeButton = FindSwatchButton(themePanel, themeSwatch.Color);
                var standardButton = FindSwatchButton(standardPanel, new CellColor(0xFF, 0x00, 0x00));
                var spectrumButton = FindSwatchButton(spectrumPanel, new CellColor(0x00, 0xFF, 0x00));

                AutomationProperties.GetName(themeButton).Should().Be(UiText.Format("ColorPicker_GroupSwatchAutomationName", "Accent 1", themeSwatch.Hex));
                AutomationProperties.GetName(standardButton).Should().Be(UiText.Format("ColorPicker_GroupSwatchAutomationName", UiText.Get("ColorPicker_StandardColorGroup"), "#FF0000"));
                AutomationProperties.GetName(spectrumButton).Should().Be(UiText.Format("ColorPicker_GroupSwatchAutomationName", UiText.Get("ColorPicker_CustomSpectrumColorGroup"), "#00FF00"));
                AutomationProperties.GetHelpText(themeButton).Should().Be(UiText.Get("ColorPicker_SwatchHelpText"));
                AutomationProperties.GetName(slider).Should().Be(UiText.Get("ColorPicker_CustomColorLuminosity"));
                AutomationProperties.GetHelpText(slider).Should().Be(UiText.Get("ColorPicker_AdjustTheBrightnessOfTheSelectedCustomColor"));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesFirstThemeSwatch()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ColorPickerDialog.xaml.cs");

        source.Should().Contain("CellColorPalettePlanner.BuildThemePalette");
        source.Should().NotContain("ColorPickerPalettePlanner");
        source.Should().Contain("private Button? _initialFocusButton;");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_initialFocusButton?.Focus();");
        source.Should().Contain("Keyboard.Focus(_initialFocusButton);");
    }

    [Fact]
    public void DialogXaml_CustomTab_LabelsRgbAndHexInputsLikeExcelMoreColors()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("ColorPickerDialog.xaml");

        foreach (var expected in new[]
        {
            "Header=\"_Custom\"",
            "Content=\"_Hex:\"",
            "Content=\"_Red:\"",
            "Content=\"_Green:\"",
            "Content=\"_Blue:\"",
            "x:Name=\"CustomRedTextBox\"",
            "x:Name=\"CustomGreenTextBox\"",
            "x:Name=\"CustomBlueTextBox\""
        })
            xaml.Should().Contain(expected);
    }
}
