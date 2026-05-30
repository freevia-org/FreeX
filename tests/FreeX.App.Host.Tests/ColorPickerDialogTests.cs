using FreeX.Core.Model;
using FluentAssertions;
using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace FreeX.App.Host.Tests;

public sealed class ColorPickerDialogTests
{
    [Fact]
    public void BuildDefaultSwatches_ReturnsNamedHexColorsWithModelColorValues()
    {
        var swatches = ColorPickerDialog.BuildDefaultSwatches();

        swatches.Should().Contain(sw => sw.Hex == "#000000" && sw.Color == CellColor.Black);
        swatches.Should().Contain(sw => sw.Hex == "#FFFFFF" && sw.Color == CellColor.White);
        swatches.Should().OnlyContain(sw => sw.Hex.Length == 7 && sw.Hex[0] == '#');
        swatches.Select(sw => sw.Hex).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildThemePalette_ReturnsExcelLikeThemeColumnsWithShades()
    {
        var columns = ColorPickerDialog.BuildThemePalette();

        columns.Should().HaveCount(10);
        columns.Should().OnlyContain(column => column.Shades.Count == 6);
        columns.Select(column => column.Name).Should().Equal(
            "Text/Background Dark 1",
            "Text/Background Light 1",
            "Text/Background Dark 2",
            "Text/Background Light 2",
            "Accent 1",
            "Accent 2",
            "Accent 3",
            "Accent 4",
            "Accent 5",
            "Accent 6");
        columns[0].Shades[0].Hex.Should().Be("#000000");
        columns[1].Shades[0].Hex.Should().Be("#FFFFFF");
        columns[4].Shades[0].Hex.Should().Be("#4472C4");
        columns.SelectMany(column => column.Shades).Select(swatch => swatch.Hex).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildStandardSwatches_ReturnsExcelLikeStandardColorRow()
    {
        var swatches = ColorPickerDialog.BuildStandardSwatches();

        swatches.Should().HaveCount(10);
        swatches.Select(swatch => swatch.Hex).Should().Contain(["#C00000", "#FFFF00", "#7030A0"]);
    }

    [Fact]
    public void BuildCustomSpectrumSwatches_ReturnsHueAndSaturationGrid()
    {
        var swatches = ColorPickerDialog.BuildCustomSpectrumSwatches();

        swatches.Should().HaveCount(48);
        swatches.Select(swatch => swatch.Hex).Should().OnlyHaveUniqueItems();
        swatches.Should().Contain(swatch => swatch.Hex == "#FF0000");
        swatches.Should().Contain(swatch => swatch.Hex == "#00FF00");
        swatches.Should().Contain(swatch => swatch.Hex == "#0000FF");
        swatches.Should().Contain(swatch => swatch.Color.R != swatch.Color.G || swatch.Color.G != swatch.Color.B);
    }

    [Fact]
    public void PalettePlanner_ScalesColorAndChoosesReadableForeground()
    {
        ColorPickerPalettePlanner.ScaleColor(new CellColor(0x40, 0x80, 0xC0), 0.5)
            .Should()
            .Be(new CellColor(0x20, 0x40, 0x60));

        ColorPickerPalettePlanner.ScaleColor(new CellColor(0xF0, 0x80, 0x40), 2)
            .Should()
            .Be(new CellColor(0xFF, 0xFF, 0x80));

        ColorPickerPalettePlanner.NeedsDarkForeground(CellColor.White).Should().BeTrue();
        ColorPickerPalettePlanner.NeedsDarkForeground(CellColor.Black).Should().BeFalse();
    }

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

                var themeButton = FindSwatchButton(themePanel, new CellColor(0x44, 0x72, 0xC4));
                var standardButton = FindSwatchButton(standardPanel, new CellColor(0xFF, 0x00, 0x00));
                var spectrumButton = FindSwatchButton(spectrumPanel, new CellColor(0x00, 0xFF, 0x00));

                AutomationProperties.GetName(themeButton).Should().Be(UiText.Format("ColorPicker_GroupSwatchAutomationName", "Accent 1", "#4472C4"));
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ColorPickerDialog.xaml.cs"));

        source.Should().Contain("ColorPickerPalettePlanner.BuildThemePalette");
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

    [Fact]
    public void InvalidCustomColor_SelectsCustomTabAndFocusesHexInput()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("ColorPickerDialog.xaml");
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ColorPickerDialog.xaml.cs"));

        xaml.Should().Contain("<TabControl x:Name=\"ColorTabs\"");
        xaml.Should().Contain("<TabItem x:Name=\"CustomTab\" Header=\"_Custom\"");
        source.Should().Contain("FocusInvalidCustomColorInput();");
        source.Should().Contain("private void FocusInvalidCustomColorInput()");
        source.Should().Contain("ColorTabs.SelectedItem = CustomTab;");
        source.Should().Contain("FocusInvalidCustomColorInput(CustomColorTextBox);");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
    }

    [Theory]
    [InlineData("#217346", 0x21, 0x73, 0x46)]
    [InlineData("217346", 0x21, 0x73, 0x46)]
    [InlineData("  #Aa10fF  ", 0xAA, 0x10, 0xFF)]
    [InlineData("33, 115, 70", 33, 115, 70)]
    [InlineData("33,115,70", 33, 115, 70)]
    public void TryParseColorText_AcceptsHexAndRgbTriples(string text, byte r, byte g, byte b)
    {
        ColorPickerDialog.TryParseColorText(text, out var color).Should().BeTrue();

        color.Should().Be(new CellColor(r, g, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("1,2")]
    [InlineData("1,2,300")]
    [InlineData("red")]
    public void TryParseColorText_RejectsInvalidColorText(string text)
    {
        ColorPickerDialog.TryParseColorText(text, out var color).Should().BeFalse();

        color.Should().Be(default(CellColor));
    }

    [Theory]
    [InlineData("33", "115", "70", 33, 115, 70)]
    [InlineData(" 0 ", "255", "128", 0, 255, 128)]
    public void TryParseRgbComponents_AcceptsByteComponents(
        string redText,
        string greenText,
        string blueText,
        byte red,
        byte green,
        byte blue)
    {
        ColorPickerDialog.TryParseRgbComponents(redText, greenText, blueText, out var color).Should().BeTrue();

        color.Should().Be(new CellColor(red, green, blue));
    }

    [Theory]
    [InlineData("300", "0", "0")]
    [InlineData("-1", "0", "0")]
    [InlineData("red", "0", "0")]
    [InlineData("", "0", "0")]
    public void TryParseRgbComponents_RejectsInvalidComponents(string redText, string greenText, string blueText)
    {
        ColorPickerDialog.TryParseRgbComponents(redText, greenText, blueText, out var color).Should().BeFalse();

        color.Should().Be(default(CellColor));
    }

    [Fact]
    public void OkButton_RejectsInvalidRgbComponentBeforeAcceptingStaleHexText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ColorPickerDialog.xaml.cs"));

        source.Should().Contain("TryParseRgbComponents(");
        source.Should().Contain("if (!TryParseCustomRgbFields(out _, out var invalidRgbInput))");
        source.Should().Contain("ShowInvalidCustomColorWarning(\"Enter RGB values from 0 to 255.\", invalidRgbInput);");
        source.Should().Contain("private void FocusInvalidCustomColorInput(TextBox target)");
        source.Should().Contain("DialogFocus.FocusAndSelect(target);");
        source.Should().NotContain("byte.TryParse(CustomRedTextBox.Text");
    }

    [Fact]
    public void Constructor_CanEnableClearChoiceWithoutSelectingAColor()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ColorPickerDialog(initialColor: null, allowNoColor: true);
            try
            {
                dialog.SelectedColor.Should().BeNull();
                dialog.AllowNoColor.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SelectingSwatch_UpdatesNewPreviewButKeepsCurrentPreview()
    {
        StaTestRunner.Run(() =>
        {
            var initialColor = new CellColor(0x21, 0x73, 0x46);
            var newColor = new CellColor(0xED, 0x7D, 0x31);
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var currentForegroundPreview = (TextBlock)dialog.FindName("CurrentForegroundPreview");
                var currentBackgroundPreview = (Border)dialog.FindName("CurrentBackgroundPreview");
                var newForegroundPreview = (TextBlock)dialog.FindName("NewForegroundPreview");
                var newBackgroundPreview = (Border)dialog.FindName("NewBackgroundPreview");
                var swatchButton = FindSwatchButton((Panel)dialog.FindName("ThemeColorsPanel"), newColor);

                swatchButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

                GetForegroundPreviewColor(currentForegroundPreview).Should().Be(initialColor);
                GetBackgroundPreviewColor(currentBackgroundPreview).Should().Be(initialColor);
                GetForegroundPreviewColor(newForegroundPreview).Should().Be(newColor);
                GetBackgroundPreviewColor(newBackgroundPreview).Should().Be(newColor);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SelectingSwatch_MarksOnlyTheChosenSwatch()
    {
        StaTestRunner.Run(() =>
        {
            var initialColor = new CellColor(0x44, 0x72, 0xC4);
            var newColor = new CellColor(0xED, 0x7D, 0x31);
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var themePanel = (Panel)dialog.FindName("ThemeColorsPanel");
                var initialButton = FindSwatchButton(themePanel, initialColor);
                var newButton = FindSwatchButton(themePanel, newColor);

                initialButton.BorderThickness.Should().Be(new System.Windows.Thickness(2));

                newButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

                initialButton.BorderThickness.Should().Be(new System.Windows.Thickness(1));
                newButton.BorderThickness.Should().Be(new System.Windows.Thickness(2));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void EditingCustomColor_UpdatesSwatchSelectionWhenColorMatchesPalette()
    {
        StaTestRunner.Run(() =>
        {
            var initialColor = new CellColor(0x44, 0x72, 0xC4);
            var paletteColor = new CellColor(0xED, 0x7D, 0x31);
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var themePanel = (Panel)dialog.FindName("ThemeColorsPanel");
                var initialButton = FindSwatchButton(themePanel, initialColor);
                var paletteButton = FindSwatchButton(themePanel, paletteColor);
                var hex = (TextBox)dialog.FindName("CustomColorTextBox");

                hex.Text = "#217346";

                initialButton.BorderThickness.Should().Be(new System.Windows.Thickness(1));
                paletteButton.BorderThickness.Should().Be(new System.Windows.Thickness(1));

                hex.Text = "#ED7D31";

                initialButton.BorderThickness.Should().Be(new System.Windows.Thickness(1));
                paletteButton.BorderThickness.Should().Be(new System.Windows.Thickness(2));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void EditingCustomRgbComponents_UpdatesSelectedColorAndPreviewText()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ColorPickerDialog();
            try
            {
                var red = (TextBox)dialog.FindName("CustomRedTextBox");
                var green = (TextBox)dialog.FindName("CustomGreenTextBox");
                var blue = (TextBox)dialog.FindName("CustomBlueTextBox");
                var hex = (TextBox)dialog.FindName("CustomColorTextBox");

                red.Text = "33";
                green.Text = "115";
                blue.Text = "70";

                dialog.SelectedColor.Should().Be(new CellColor(33, 115, 70));
                hex.Text.Should().Be("#217346");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SelectingCustomSpectrumSwatch_UpdatesNewPreviewAndRgbFieldsButKeepsCurrentPreview()
    {
        StaTestRunner.Run(() =>
        {
            var initialColor = new CellColor(0x00, 0x20, 0x60);
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var currentForegroundPreview = (TextBlock)dialog.FindName("CurrentForegroundPreview");
                var currentBackgroundPreview = (Border)dialog.FindName("CurrentBackgroundPreview");
                var newForegroundPreview = (TextBlock)dialog.FindName("NewForegroundPreview");
                var newBackgroundPreview = (Border)dialog.FindName("NewBackgroundPreview");
                var red = (TextBox)dialog.FindName("CustomRedTextBox");
                var green = (TextBox)dialog.FindName("CustomGreenTextBox");
                var blue = (TextBox)dialog.FindName("CustomBlueTextBox");
                var hex = (TextBox)dialog.FindName("CustomColorTextBox");
                var spectrumButton = FindSwatchButton((Panel)dialog.FindName("CustomSpectrumPanel"), new CellColor(0x00, 0xFF, 0x00));

                spectrumButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

                GetForegroundPreviewColor(currentForegroundPreview).Should().Be(initialColor);
                GetBackgroundPreviewColor(currentBackgroundPreview).Should().Be(initialColor);
                GetForegroundPreviewColor(newForegroundPreview).Should().Be(new CellColor(0x00, 0xFF, 0x00));
                GetBackgroundPreviewColor(newBackgroundPreview).Should().Be(new CellColor(0x00, 0xFF, 0x00));
                red.Text.Should().Be("0");
                green.Text.Should().Be("255");
                blue.Text.Should().Be("0");
                hex.Text.Should().Be("#00FF00");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void LuminositySlider_UsesInitialColorAsCustomBase()
    {
        StaTestRunner.Run(() =>
        {
            var initialColor = new CellColor(0x40, 0x80, 0xC0);
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var slider = (Slider)dialog.FindName("CustomLuminositySlider");
                var red = (TextBox)dialog.FindName("CustomRedTextBox");
                var green = (TextBox)dialog.FindName("CustomGreenTextBox");
                var blue = (TextBox)dialog.FindName("CustomBlueTextBox");
                var hex = (TextBox)dialog.FindName("CustomColorTextBox");
                var currentForegroundPreview = (TextBlock)dialog.FindName("CurrentForegroundPreview");
                var newForegroundPreview = (TextBlock)dialog.FindName("NewForegroundPreview");

                slider.Value = 50;

                dialog.SelectedColor.Should().Be(new CellColor(0x20, 0x40, 0x60));
                red.Text.Should().Be("32");
                green.Text.Should().Be("64");
                blue.Text.Should().Be("96");
                hex.Text.Should().Be("#204060");
                GetForegroundPreviewColor(currentForegroundPreview).Should().Be(initialColor);
                GetForegroundPreviewColor(newForegroundPreview).Should().Be(new CellColor(0x20, 0x40, 0x60));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Preview_ShowsColorAsForegroundAndBackgroundWithReadableFillText()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ColorPickerDialog(new CellColor(0x00, 0x20, 0x60));
            try
            {
                var foregroundPreview = (TextBlock)dialog.FindName("CurrentForegroundPreview");
                var backgroundPreview = (Border)dialog.FindName("CurrentBackgroundPreview");
                var backgroundText = (TextBlock)dialog.FindName("CurrentBackgroundText");

                GetForegroundPreviewColor(foregroundPreview).Should().Be(new CellColor(0x00, 0x20, 0x60));
                GetBackgroundPreviewColor(backgroundPreview).Should().Be(new CellColor(0x00, 0x20, 0x60));
                backgroundText.Foreground.Should().BeSameAs(Brushes.White);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ThemePanel_AddsSwatchesByRowsSoExcelColumnsStayVertical()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ColorPickerDialog();
            try
            {
                var panel = (Panel)dialog.FindName("ThemeColorsPanel");
                var firstRow = panel.Children
                    .OfType<Button>()
                    .Take(10)
                    .Select(button => (CellColor)button.Tag)
                    .ToArray();

                firstRow.Should().Equal(
                    new CellColor(0x00, 0x00, 0x00),
                    new CellColor(0xFF, 0xFF, 0xFF),
                    new CellColor(0x44, 0x54, 0x6A),
                    new CellColor(0xE7, 0xE6, 0xE6),
                    new CellColor(0x44, 0x72, 0xC4),
                    new CellColor(0xED, 0x7D, 0x31),
                    new CellColor(0xA5, 0xA5, 0xA5),
                    new CellColor(0xFF, 0xC0, 0x00),
                    new CellColor(0x5B, 0x9B, 0xD5),
                    new CellColor(0x70, 0xAD, 0x47));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static Button FindSwatchButton(Panel panel, CellColor color) =>
        panel.Children
            .OfType<Button>()
            .Single(button => button.Tag is CellColor swatchColor && swatchColor == color);

    private static CellColor GetForegroundPreviewColor(TextBlock preview)
    {
        var brush = preview.Foreground.Should().BeOfType<SolidColorBrush>().Subject;
        return new CellColor(brush.Color.R, brush.Color.G, brush.Color.B);
    }

    private static CellColor GetBackgroundPreviewColor(Border preview)
    {
        var brush = preview.Background.Should().BeOfType<SolidColorBrush>().Subject;
        return new CellColor(brush.Color.R, brush.Color.G, brush.Color.B);
    }
}
