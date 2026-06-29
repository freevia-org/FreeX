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

    public static readonly RibbonText FontDialogCommand = new(
        "Ribbon_Command_FontDialog_Label");
}

internal readonly record struct RibbonText(string LabelKey, string? KeyTipKey = null)
{
    public string Label => Loc.Get(LabelKey);

    public string? KeyTip => KeyTipKey is null ? null : Loc.GetNeutral(KeyTipKey);
}
