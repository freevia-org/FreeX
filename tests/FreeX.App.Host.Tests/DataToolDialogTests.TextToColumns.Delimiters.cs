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
    [Theory]
    [InlineData(TextToColumnsDelimiterKind.Comma, null, ",")]
    [InlineData(TextToColumnsDelimiterKind.Semicolon, null, ";")]
    [InlineData(TextToColumnsDelimiterKind.Tab, null, "\t")]
    [InlineData(TextToColumnsDelimiterKind.Space, null, " ")]
    [InlineData(TextToColumnsDelimiterKind.Custom, "|", "|")]
    public void TextToColumnsResult_MapsDelimiterChoiceToDelimiterString(
        TextToColumnsDelimiterKind kind,
        string? customDelimiter,
        string expectedDelimiter)
    {
        var result = TextToColumnsDialog.CreateResult(kind, customDelimiter);

        result.Delimiter.Should().Be(expectedDelimiter);
    }

    [Fact]
    public void TextToColumnsResult_CombinesCheckedDelimiters()
    {
        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Tab, TextToColumnsDelimiterKind.Comma, TextToColumnsDelimiterKind.Custom],
            "|");

        result.Delimiters.Should().Be("\t,|");
        result.DelimiterKind.Should().Be(TextToColumnsDelimiterKind.Custom);
    }

    [Fact]
    public void TextToColumnsDelimiterPlanner_BuildsDistinctDelimiterPlan()
    {
        var plan = TextToColumnsDelimiterPlanner.CreatePlan(
            [
                TextToColumnsDelimiterKind.Space,
                TextToColumnsDelimiterKind.Comma,
                TextToColumnsDelimiterKind.Space,
                TextToColumnsDelimiterKind.Custom
            ],
            "|");

        plan.Should().Be(new TextToColumnsDelimiterPlan(TextToColumnsDelimiterKind.Custom, " ,|"));
        TextToColumnsDelimiterPlanner.DelimiterFor(TextToColumnsDelimiterKind.Tab).Should().Be("\t");
        var act = () => TextToColumnsDelimiterPlanner.DelimiterFor(TextToColumnsDelimiterKind.Custom);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Custom delimiter is required.*");
    }

    [Fact]
    public void TextToColumnsResult_RejectsEmptyDelimiterSelection()
    {
        var act = () => TextToColumnsDialog.CreateResult([]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Select at least one delimiter.*");
    }

    [Fact]
    public void TextToColumnsDialog_ExposesDelimiterPreviewAffordances()
    {
        var source = ReadTextToColumnsDialogSources();

        foreach (var key in new[]
        {
            "TextToColumns_Tab",
            "TextToColumns_Semicolon",
            "TextToColumns_Comma",
            "TextToColumns_Space",
            "TextToColumns_Other",
            "TextToColumns_DataPreview"
        })
            source.Should().Contain($"UiText.Get(\"{key}\")");

        source.Should().Contain("_previewGrid");
        source.Should().Contain("RefreshPreview");
        source.Should().Contain("TextToColumnsPlanner.SplitText");
        source.Should().Contain("_textQualifierBox");
        source.Should().Contain("SelectedTextQualifier");
        source.Should().Contain("TreatConsecutiveDelimitersAsOne");
        source.Should().Contain("_destinationBox");
        source.Should().Contain("_formatColumnBox");
        source.Should().Contain("BuildColumnFormats");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("TextToColumnsRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void TextToColumnsResult_CapturesTextQualifierAndConsecutiveDelimiterChoice()
    {
        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Comma],
            textQualifier: TextToColumnsTextQualifier.SingleQuote,
            treatConsecutiveDelimitersAsOne: true);

        result.Delimiters.Should().Be(",");
        result.TextQualifier.Should().Be(TextToColumnsTextQualifier.SingleQuote);
        result.TextQualifierChar.Should().Be('\'');
        result.TreatConsecutiveDelimitersAsOne.Should().BeTrue();
    }
}
