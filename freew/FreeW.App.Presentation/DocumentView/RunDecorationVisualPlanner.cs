using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record RunDecorationVisualPlan(
    string? BackgroundColorHex,
    bool BackgroundIsCharacterShading,
    ShadingPattern CharacterShadingPattern,
    ParagraphBorder? Border,
    bool DrawTopBorder,
    bool DrawLeftBorder,
    bool DrawBottomBorder,
    bool DrawRightBorder,
    double BorderWidthDip)
{
    public bool HasBackground => !string.IsNullOrWhiteSpace(BackgroundColorHex);
    public bool HasBorder => Border is not null
        && (DrawTopBorder || DrawLeftBorder || DrawBottomBorder || DrawRightBorder);
}

public static class RunDecorationVisualPlanner
{
    public const double MinimumBorderWidthDip = 1.0;

    public static RunDecorationVisualPlan Build(RunFormatting formatting, double dipPerPoint = 1.0)
    {
        var hasCharacterShading = !string.IsNullOrWhiteSpace(formatting.CharacterShadingHex);
        var backgroundHex = hasCharacterShading
            ? formatting.CharacterShadingHex
            : formatting.HighlightColorHex;

        var border = formatting.CharacterBorder;
        var drawTop = false;
        var drawLeft = false;
        var drawBottom = false;
        var drawRight = false;
        var borderWidthDip = 0.0;

        if (border is not null)
        {
            drawTop = !border.BottomOnly && border.Top;
            drawLeft = !border.BottomOnly && border.Left;
            drawBottom = border.BottomOnly || border.Bottom;
            drawRight = !border.BottomOnly && border.Right;
            if (drawTop || drawLeft || drawBottom || drawRight)
                borderWidthDip = Math.Max(MinimumBorderWidthDip, border.WidthPt * Math.Max(0.01, dipPerPoint));
        }

        return new RunDecorationVisualPlan(
            backgroundHex,
            hasCharacterShading,
            hasCharacterShading ? formatting.CharacterShadingPattern : ShadingPattern.Clear,
            border,
            drawTop,
            drawLeft,
            drawBottom,
            drawRight,
            borderWidthDip);
    }
}
