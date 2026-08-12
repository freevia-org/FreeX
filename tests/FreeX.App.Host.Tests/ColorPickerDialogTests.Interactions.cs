using FreeX.Core.Model;
using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeX.App.Host.Tests;

public sealed partial class ColorPickerDialogTests
{
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
    public void Constructor_CanLabelClearChoiceForFillWorkflows()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ColorPickerDialog(initialColor: null, allowNoColor: true, noColorButtonText: "No Fill");
            try
            {
                var noColorButton = (Button)dialog.FindName("NoColorButton");

                noColorButton.Content.Should().Be("No Fill");
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
            var newColor = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent2);
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var currentForegroundPreview = (TextBlock)dialog.FindName("CurrentForegroundPreview");
                var currentBackgroundPreview = (Border)dialog.FindName("CurrentBackgroundPreview");
                var newForegroundPreview = (TextBlock)dialog.FindName("NewForegroundPreview");
                var newBackgroundPreview = (Border)dialog.FindName("NewBackgroundPreview");
                var swatchButton = FindSwatchButton((Panel)dialog.FindName("ThemeColorsPanel"), newColor);

                DialogSourceTestSupport.ClickButton(swatchButton);

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
            var initialColor = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1);
            var newColor = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent2);
            var dialog = new ColorPickerDialog(initialColor);
            try
            {
                var themePanel = (Panel)dialog.FindName("ThemeColorsPanel");
                var initialButton = FindSwatchButton(themePanel, initialColor);
                var newButton = FindSwatchButton(themePanel, newColor);

                initialButton.BorderThickness.Should().Be(new System.Windows.Thickness(2));

                DialogSourceTestSupport.ClickButton(newButton);

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
            var initialColor = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1);
            var paletteColor = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent2);
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

                hex.Text = $"#{paletteColor.R:X2}{paletteColor.G:X2}{paletteColor.B:X2}";

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

                DialogSourceTestSupport.ClickButton(spectrumButton);

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
}
