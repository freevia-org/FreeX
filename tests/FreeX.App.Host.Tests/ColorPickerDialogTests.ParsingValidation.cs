using FreeX.Core.Model;
using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class ColorPickerDialogTests
{
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
}
