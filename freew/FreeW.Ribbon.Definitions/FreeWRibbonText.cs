using FreeW.App.Localization;

namespace FreeW.Ribbon.Definitions;

internal static class FreeWRibbonText
{
    public static readonly RibbonText HomeTab = new(
        "Ribbon_Tab_Home_Label",
        "Ribbon_Tab_Home_KeyTip");

    public static readonly RibbonText ClipboardGroup = new(
        "Ribbon_Group_Clipboard_Label",
        "Ribbon_Group_Clipboard_KeyTip");

    public static readonly RibbonText PasteCommand = new(
        "Ribbon_Command_Paste_Label",
        "Ribbon_Command_Paste_KeyTip");

    public static readonly RibbonText CutCommand = new(
        "Ribbon_Command_Cut_Label",
        "Ribbon_Command_Cut_KeyTip");

    public static readonly RibbonText CopyCommand = new(
        "Ribbon_Command_Copy_Label",
        "Ribbon_Command_Copy_KeyTip");

    public static readonly RibbonText FormatPainterCommand = new(
        "Ribbon_Command_FormatPainter_Label",
        "Ribbon_Command_FormatPainter_KeyTip");

    public static readonly RibbonText PasteTextOnlyCommand = new(
        "Ribbon_Command_PasteTextOnly_Label");

    public static readonly RibbonText PasteMergeFormattingCommand = new(
        "Ribbon_Command_PasteMergeFormatting_Label");

    public static readonly RibbonText PasteSpecialCommand = new(
        "Ribbon_Command_PasteSpecial_Label");

    public static readonly RibbonText FontGroup = new(
        "Ribbon_Group_Font_Label",
        "Ribbon_Group_Font_KeyTip");

    public static readonly RibbonText FontFamilyCommand = new(
        "Ribbon_Command_FontFamily_Label");

    public static readonly RibbonText FontSizeCommand = new(
        "Ribbon_Command_FontSize_Label");

    public static readonly RibbonText BoldCommand = new(
        "Ribbon_Command_Bold_Label",
        "Ribbon_Command_Bold_KeyTip");

    public static readonly RibbonText ItalicCommand = new(
        "Ribbon_Command_Italic_Label",
        "Ribbon_Command_Italic_KeyTip");

    public static readonly RibbonText UnderlineCommand = new(
        "Ribbon_Command_Underline_Label",
        "Ribbon_Command_Underline_KeyTip");

    public static readonly RibbonText StrikethroughCommand = new(
        "Ribbon_Command_Strikethrough_Label");

    public static readonly RibbonText GrowFontCommand = new(
        "Ribbon_Command_GrowFont_Label");

    public static readonly RibbonText GrowFontCompactCommand = new(
        "Ribbon_Command_GrowFontCompact_Label");

    public static readonly RibbonText ShrinkFontCommand = new(
        "Ribbon_Command_ShrinkFont_Label");

    public static readonly RibbonText ShrinkFontCompactCommand = new(
        "Ribbon_Command_ShrinkFontCompact_Label");

    public static readonly RibbonText SubscriptCommand = new(
        "Ribbon_Command_Subscript_Label");

    public static readonly RibbonText SubscriptCompactCommand = new(
        "Ribbon_Command_SubscriptCompact_Label");

    public static readonly RibbonText SuperscriptCommand = new(
        "Ribbon_Command_Superscript_Label");

    public static readonly RibbonText SuperscriptCompactCommand = new(
        "Ribbon_Command_SuperscriptCompact_Label");

    public static readonly RibbonText ChangeCaseCommand = new(
        "Ribbon_Command_ChangeCase_Label");

    public static readonly RibbonText ChangeCaseCompactCommand = new(
        "Ribbon_Command_ChangeCaseCompact_Label");

    public static readonly RibbonText SmallCapsCommand = new(
        "Ribbon_Command_SmallCaps_Label");

    public static readonly RibbonText AllCapsCommand = new(
        "Ribbon_Command_AllCaps_Label");

    public static readonly RibbonText TextHighlightColorCommand = new(
        "Ribbon_Command_TextHighlightColor_Label");

    public static readonly RibbonText HighlightCompactCommand = new(
        "Ribbon_Command_HighlightCompact_Label");

    public static readonly RibbonText FontColorCommand = new(
        "Ribbon_Command_FontColor_Label");

    public static readonly RibbonText FontColorDropdownCommand = new(
        "Common_FontColor");

    public static readonly RibbonText CharacterBorderCommand = new(
        "Ribbon_Command_CharacterBorder_Label");

    public static readonly RibbonText CharacterShadingCommand = new(
        "Ribbon_Command_CharacterShading_Label");

    public static readonly RibbonText ClearAllFormattingCommand = new(
        "Ribbon_Command_ClearAllFormatting_Label");

    public static readonly RibbonText ClearFormattingCompactCommand = new(
        "Ribbon_Command_ClearFormattingCompact_Label");

    public static readonly RibbonText FontDialogCommand = new(
        "Ribbon_Command_FontDialog_Label");

    public static readonly RibbonText ParagraphGroup = new(
        "Ribbon_Group_Paragraph_Label",
        "Ribbon_Group_Paragraph_KeyTip");

    public static readonly RibbonText BulletsCommand = new(
        "Ribbon_Command_Bullets_Label");

    public static readonly RibbonText NumberingCommand = new(
        "Ribbon_Command_Numbering_Label");

    public static readonly RibbonText MultilevelListCommand = new(
        "Ribbon_Command_MultilevelList_Label");

    public static readonly RibbonText MultilevelPromoteCommand = new(
        "Ribbon_Command_MultilevelPromote_Label",
        "Ribbon_Command_MultilevelPromote_KeyTip");

    public static readonly RibbonText MultilevelDemoteCommand = new(
        "Ribbon_Command_MultilevelDemote_Label",
        "Ribbon_Command_MultilevelDemote_KeyTip");

    public static readonly RibbonText MultilevelDefineCommand = new(
        "Ribbon_Command_MultilevelDefine_Label",
        "Ribbon_Command_MultilevelDefine_KeyTip");

    public static readonly RibbonText SymbolsGroup = new(
        "Ribbon_Group_Symbols_Label",
        "Ribbon_Group_Symbols_KeyTip");

    public static readonly RibbonText SymbolCommand = new(
        "Ribbon_Command_Symbol_Label");

    public static readonly RibbonText PageBackgroundGroup = new(
        "Ribbon_Group_PageBackground_Label",
        "Ribbon_Group_PageBackground_KeyTip");

    public static readonly RibbonText WatermarkCommand = new(
        "Ribbon_Command_Watermark_Label");

    public static readonly RibbonText PageColorCommand = new(
        "Ribbon_Command_PageColor_Label");

    public static readonly RibbonText PageBordersCommand = new(
        "Ribbon_Command_PageBorders_Label");

    public static string PageColorNoColorOption => Loc.Get("Ribbon_Palette_PageColor_NoColor_Label");
}

internal readonly record struct RibbonText(string LabelKey, string? KeyTipKey = null)
{
    public string Label => Loc.Get(LabelKey);

    public string? KeyTip => KeyTipKey is null ? null : Loc.GetNeutral(KeyTipKey);
}
