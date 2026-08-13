using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Rendering;

public sealed class CellTextMaterializationPlannerTests
{
    [Fact]
    public void NumericSuperscript_PreservesFormattedTextInputsAndPlansBaseline()
    {
        var style = new CellStyle
        {
            NumberFormat = "#,##0.00",
            Superscript = true,
        };

        var plan = CellTextMaterializationPlanner.Plan(
            "1,234.50",
            true,
            style,
            16,
            null,
            CellTextMaterializationProfile.Wpf);

        plan.Formatting.Should().Be(new CellTextFormattingInputs("1,234.50", "#,##0.00", true));
        plan.RenderedFontSize.Should().BeApproximately(9.328, 0.000001);
        plan.BaselineOffset.Should().BeApproximately(-5.28, 0.000001);
        plan.Baseline.Should().Be(CellTextBaselineKind.Superscript);
    }

    [Fact]
    public void RichText_CellScriptBehaviorIsAnExplicitRendererProfileChoice()
    {
        var style = new CellStyle { Subscript = true };
        var runs = new[] { Run("42") };

        var wpf = CellTextMaterializationPlanner.Plan(
            "42", true, style, 12, runs, CellTextMaterializationProfile.Wpf);
        var avalonia = CellTextMaterializationPlanner.Plan(
            "42", true, style, 12, runs, CellTextMaterializationProfile.Avalonia);

        wpf.RenderedFontSize.Should().BeApproximately(6.996, 0.000001);
        wpf.BaselineOffset.Should().BeApproximately(1.68, 0.000001);
        avalonia.RenderedFontSize.Should().Be(12);
        avalonia.BaselineOffset.Should().Be(0);
        avalonia.Baseline.Should().Be(CellTextBaselineKind.Baseline);
    }

    [Fact]
    public void WpfRunSegments_ClampRangesToFormattedDisplayText()
    {
        var runs = new[] { Run("123"), Run("456") };

        var segments = CellTextMaterializationPlanner.MaterializeRuns(
            "123.4",
            runs,
            CellRichTextMaterializationMode.FormattedDisplayTextRanges);

        segments.Select(segment => (segment.Text, segment.Start, segment.Length)).Should().Equal(
            ("123", 0, 3),
            (".4", 3, 2));
    }

    [Fact]
    public void AvaloniaRunSegments_PreserveNativeRunTextIncludingEmptyRuns()
    {
        var runs = new[] { Run("12"), Run(string.Empty), Run("kg") };

        var segments = CellTextMaterializationPlanner.MaterializeRuns(
            "12 kg",
            runs,
            CellRichTextMaterializationMode.NativeRunText);

        segments.Select(segment => (segment.Text, segment.Start, segment.Length)).Should().Equal(
            ("12", 0, 2),
            (string.Empty, 2, 0),
            ("kg", 2, 2));
    }

    [Fact]
    public void SuperscriptWinsWhenMalformedStyleSetsBothScriptFlags()
    {
        var style = new CellStyle { Superscript = true, Subscript = true };

        var plan = CellTextMaterializationPlanner.Plan(
            "7", true, style, 10, null, CellTextMaterializationProfile.Wpf);

        plan.Baseline.Should().Be(CellTextBaselineKind.Superscript);
        plan.BaselineOffset.Should().BeApproximately(-3.3, 0.000001);
    }

    private static ResolvedCellTextRun Run(string text) =>
        new(
            text,
            Bold: false,
            Italic: false,
            Underline: false,
            Strikethrough: false,
            FontName: "Calibri",
            RenderedFontSize: 11,
            BaseFontSize: 11,
            FontColor: CellColor.Black,
            VertAlign: CellTextRunVertAlign.None);
}
