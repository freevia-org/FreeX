using FreeX.App.Presentation.Text;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Deterministic text measurer for layout tests: width = character count × a per-em width factor,
/// height = the font size. Newlines split into lines (width = widest line, height = line count ×
/// font size). No platform font stack involved, so results are stable across machines.
/// </summary>
internal sealed class FakeTextMeasurer : ITextMeasurer
{
    private readonly double _widthFactorPerEm;

    public FakeTextMeasurer(double widthFactorPerEm = 0.6) => _widthFactorPerEm = widthFactorPerEm;

    public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic)
    {
        if (string.IsNullOrEmpty(text))
            return TextSize.Empty;

        var lines = text.Split('\n');
        var widest = 0;
        foreach (var line in lines)
            widest = Math.Max(widest, line.Length);

        var charWidth = fontSize * _widthFactorPerEm;
        var bonus = (bold ? 1.1 : 1.0) * (italic ? 1.05 : 1.0);
        return new TextSize(widest * charWidth * bonus, lines.Length * fontSize);
    }
}
