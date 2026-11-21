using System.Linq;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>Render-only fidelity fixes found by the 2026-11-20 Word comparison (footnotes, auto text colour).</summary>
public sealed class NoteMarkAndAutoColorRenderTests
{
    [StaFact]
    public void NoteReferenceMark_TextEffect_CoversTheWholeMark()
    {
        var model = TextDocument.CreateEmpty();
        var marker = new WpfRun("12");

        DocumentView.ApplyNoteReferencePresentation(marker, model);

        var effect = Assert.Single(marker.TextEffects);
        Assert.Equal(0, effect.PositionStart);
        Assert.True(effect.PositionCount >= marker.Text.Length, "A TextEffect with PositionCount 0 moves no characters, leaving the mark lowered.");
        Assert.True(((TranslateTransform)effect.Transform).Y < 0);
    }

    [StaFact]
    public void PrintNoteRow_LabelIsSuperscriptRunWithoutPlainPrefix()
    {
        var block = PrintPlainText.BuildNote("85", "Hillman", new FontFamily("Times New Roman"), 13, Brushes.Black, 400);

        var runs = block.Inlines.OfType<WpfRun>().ToList();
        Assert.Equal(2, runs.Count);
        Assert.Equal("85", runs[0].Text);
        Assert.Equal(System.Windows.BaselineAlignment.Superscript, runs[0].BaselineAlignment);
        Assert.True(runs[0].FontSize < 13);
        Assert.Equal("Hillman", runs[1].Text);
    }

    [StaFact]
    public void PrintNoteRow_PinsEveryLineToTheFontsSingleLineSoTheRaisedMarkDoesNotGrowTheFirstLine()
    {
        var family = new FontFamily("Times New Roman");
        var labelled = PrintPlainText.BuildNote("63", "Siehe auch", family, 13, Brushes.Black, 400);
        var plain = PrintPlainText.BuildNote("", "Siehe auch", family, 13, Brushes.Black, 400);

        Assert.Equal(System.Windows.LineStackingStrategy.BlockLineHeight, labelled.LineStackingStrategy);
        Assert.InRange(labelled.LineHeight, family.LineSpacing * 13, family.LineSpacing * 13 + 0.01);
        Assert.InRange(labelled.DesiredSize.Height, plain.DesiredSize.Height - 0.5, plain.DesiredSize.Height + 0.5);
    }

    [StaFact]
    public void PrintNoteRow_WithoutLabelIsPlainText()
    {
        var block = PrintPlainText.BuildNote("", "Hillman", new FontFamily("Times New Roman"), 13, Brushes.Black, 400);
        Assert.Equal("Hillman", block.Text);
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00, true)]
    [InlineData(0x1F, 0x38, 0x64, true)]
    [InlineData(0xBF, 0xBF, 0xBF, false)]
    [InlineData(0xFF, 0xFF, 0xFF, false)]
    [InlineData(0xFF, 0xFF, 0x00, false)]
    public void AutoTextColor_FlipsToWhiteOnlyOnDarkFill(byte r, byte g, byte b, bool dark) =>
        Assert.Equal(dark, DocumentView.IsDarkAutoTextBackground(Color.FromRgb(r, g, b)));
}
