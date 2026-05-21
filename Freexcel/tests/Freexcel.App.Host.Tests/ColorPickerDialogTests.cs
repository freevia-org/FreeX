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
    public void DialogXaml_ExposesExcelLikePaletteSectionsAndPreview()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ColorPickerDialog.xaml"));

        xaml.Should().Contain("Theme Colors");
        xaml.Should().Contain("Standard Colors");
        xaml.Should().Contain("Current");
        xaml.Should().Contain("New");
        xaml.Should().Contain("ThemeColorsPanel");
        xaml.Should().Contain("StandardColorsPanel");
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
                var currentPreview = (Border)dialog.FindName("CurrentColorPreview");
                var newPreview = (Border)dialog.FindName("NewColorPreview");
                var swatchButton = FindSwatchButton((Panel)dialog.FindName("ThemeColorsPanel"), newColor);

                swatchButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

                GetPreviewColor(currentPreview).Should().Be(initialColor);
                GetPreviewColor(newPreview).Should().Be(newColor);
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

    private static CellColor GetPreviewColor(Border preview)
    {
        var brush = preview.Background.Should().BeOfType<SolidColorBrush>().Subject;
        return new CellColor(brush.Color.R, brush.Color.G, brush.Color.B);
    }
}
