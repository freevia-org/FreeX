using Freexcel.Core.Model;
using FluentAssertions;
using System.IO;

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
        columns.Select(column => column.Name).Should().Contain(["Text/Background", "Accent 1", "Accent 6"]);
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
}
