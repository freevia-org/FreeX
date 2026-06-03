using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void TextToColumnsDialog_ExposesAllExcelDateColumnFormats()
    {
        var dialogSource = ReadTextToColumnsDialogSources();
        var modelSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialogModel.cs"));

        foreach (var dateOrder in new[] { "MDY", "DMY", "YMD", "MYD", "DYM", "YDM" })
        {
            dialogSource.Should().Contain($"\"{dateOrder}\"");
            modelSource.Should().Contain($"Date{dateOrder}");
        }
    }

    [Fact]
    public void TextToColumnsDialogPlanner_MapsColumnFormatState()
    {
        TextToColumnsDialogPlanner.TextQualifierFromSelectedIndex(1)
            .Should().Be(TextToColumnsTextQualifier.SingleQuote);
        TextToColumnsDialogPlanner.TextQualifierFromSelectedIndex(99)
            .Should().Be(TextToColumnsTextQualifier.DoubleQuote);
        TextToColumnsDialogPlanner.DateColumnFormatFromLabel("YDM")
            .Should().Be(TextToColumnsColumnFormat.DateYDM);
        TextToColumnsDialogPlanner.DateColumnFormatLabel(TextToColumnsColumnFormat.DateDYM)
            .Should().Be("DYM");
        TextToColumnsDialogPlanner.IsDateColumnFormat(TextToColumnsColumnFormat.Text)
            .Should().BeFalse();
        TextToColumnsDialogPlanner.BuildColumnFormats(
                4,
                new Dictionary<int, TextToColumnsColumnFormat>
                {
                    [1] = TextToColumnsColumnFormat.Text,
                    [2] = TextToColumnsColumnFormat.General,
                    [3] = TextToColumnsColumnFormat.General
                })
            .Should().Equal(TextToColumnsColumnFormat.General, TextToColumnsColumnFormat.Text);
    }

    [Fact]
    public void TextToColumnsResult_NormalizesTrailingGeneralColumnFormats()
    {
        TextToColumnsDialog.NormalizeColumnFormats(
            [
                TextToColumnsColumnFormat.Text,
                TextToColumnsColumnFormat.DateMDY,
                TextToColumnsColumnFormat.General,
                TextToColumnsColumnFormat.General
            ])
            .Should()
            .Equal(TextToColumnsColumnFormat.Text, TextToColumnsColumnFormat.DateMDY);

        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Comma],
            columnFormats:
            [
                TextToColumnsColumnFormat.General,
                TextToColumnsColumnFormat.Skip
            ]);

        result.ColumnFormats.Should().Equal(
            TextToColumnsColumnFormat.General,
            TextToColumnsColumnFormat.Skip);
    }

    [Fact]
    public void TextToColumnsResult_CapturesAdvancedNumberOptions()
    {
        var advanced = new TextToColumnsAdvancedOptions(",", ".", TrailingMinusNumbers: true);

        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Semicolon],
            advancedOptions: advanced);

        result.AdvancedOptions.Should().Be(advanced);
    }

    [Theory]
    [InlineData(".", true, ".")]
    [InlineData(" , ", true, ",")]
    [InlineData("", false, "")]
    [InlineData("  ", false, "")]
    [InlineData("..", false, "")]
    public void TextToColumnsResult_TryParseAdvancedSeparatorRequiresSingleCharacter(
        string text,
        bool expectedResult,
        string expectedSeparator)
    {
        TextToColumnsDialog.TryParseAdvancedSeparator(text, out var separator).Should().Be(expectedResult);
        separator.Should().Be(expectedSeparator);
    }
}
