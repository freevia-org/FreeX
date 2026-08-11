using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

internal static class DialogWorkflowResultFactory
{
    public static FontDialogWorkflowResult FontResult(
        string? Family,
        double? SizePt,
        bool? Bold,
        bool? Italic,
        bool? Underline,
        bool? Strikethrough,
        VerticalAlign VerticalAlign,
        bool SmallCaps,
        bool AllCaps,
        string? ColorHex,
        string? HighlightHex,
        bool FamilyChanged = true,
        bool SizeChanged = true,
        double CharacterSpacingPt = 0,
        double? KerningMinSizePt = null,
        double PositionPt = 0,
        LigatureMode Ligatures = LigatureMode.None,
        int? StylisticSet = null,
        NumberForm NumberForm = NumberForm.Default,
        NumberSpacing NumberSpacing = NumberSpacing.Default,
        bool AdvancedChanged = false,
        bool? DoubleStrikethrough = null,
        bool? Hidden = null)
    {
        var formatting = RunFormatting.Default with
        {
            FontFamily = Family,
            FontSizePt = SizePt,
            Bold = Bold ?? false,
            Italic = Italic ?? false,
            Underline = Underline ?? false,
            Strikethrough = Strikethrough ?? false,
            DoubleStrikethrough = DoubleStrikethrough ?? false,
            Hidden = Hidden ?? false,
            VerticalAlign = VerticalAlign,
            SmallCaps = SmallCaps,
            AllCaps = AllCaps,
            ColorHex = ColorHex,
            CharacterSpacingPt = CharacterSpacingPt,
            KerningMinSizePt = KerningMinSizePt,
            PositionPt = PositionPt,
            Ligatures = Ligatures,
            StylisticSet = StylisticSet,
            NumberForm = NumberForm,
            NumberSpacing = NumberSpacing,
        };

        return new FontDialogWorkflowResult(
            formatting,
            Bold,
            Italic,
            Underline,
            Strikethrough,
            DoubleStrikethrough,
            Hidden,
            FamilyChanged,
            SizeChanged,
            AdvancedChanged,
            HighlightHex);
    }

    public static ParagraphBreaksDialogResult ParagraphResult(
        TextAlignment Alignment,
        double IndentLeftPt,
        double IndentRightPt,
        double FirstLineIndentPt,
        double SpaceBeforePt,
        double SpaceAfterPt,
        LineSpacingRule LineRule,
        double LineSpacingValue)
    {
        _ = Alignment;
        _ = LineRule;
        return new ParagraphBreaksDialogResult(
            IndentLeftPt,
            IndentRightPt,
            FirstLineIndentPt,
            SpaceBeforePt,
            SpaceAfterPt,
            LineSpacingValue,
            KeepWithNext: false,
            KeepLinesTogether: false,
            WidowControl: true,
            PageBreakBefore: false,
            SuppressAutoHyphens: false,
            SuppressLineNumbers: false,
            ContextualSpacing: false);
    }
}
