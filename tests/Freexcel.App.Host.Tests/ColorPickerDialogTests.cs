using Freexcel.Core.Model;
using FluentAssertions;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace Freexcel.App.Host.Tests;

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
    public void DialogXaml_ExposesExcelLikePaletteSectionsAndPreview()
    {
        var xamlPath = WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ColorPickerDialog.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var document = XDocument.Load(xamlPath);
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ColorPickerDialog.xaml"));
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
    public void DialogOpenedFromKeyboard_FocusesFirstThemeSwatch()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ColorPickerDialog.xaml.cs"));

        source.Should().Contain("private Button? _initialFocusButton;");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_initialFocusButton?.Focus();");
        source.Should().Contain("Keyboard.Focus(_initialFocusButton);");
    }

    [Fact]
    public void DialogXaml_CustomTab_LabelsRgbAndHexInputsLikeExcelMoreColors()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ColorPickerDialog.xaml"));

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
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ColorPickerDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ColorPickerDialog.xaml.cs"));

        xaml.Should().Contain("<TabControl x:Name=\"ColorTabs\"");
        xaml.Should().Contain("<TabItem x:Name=\"CustomTab\" Header=\"_Custom\"");
        source.Should().Contain("FocusInvalidCustomColorInput();");
        source.Should().Contain("private void FocusInvalidCustomColorInput()");
        source.Should().Contain("ColorTabs.SelectedItem = CustomTab;");
        source.Should().Contain("CustomColorTextBox.Focus();");
        source.Should().Contain("CustomColorTextBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(CustomColorTextBox);");
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
