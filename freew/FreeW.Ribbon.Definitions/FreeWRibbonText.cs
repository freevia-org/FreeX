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
}

internal readonly record struct RibbonText(string LabelKey, string? KeyTipKey = null)
{
    public string Label => Loc.Get(LabelKey);

    public string? KeyTip => KeyTipKey is null ? null : Loc.GetNeutral(KeyTipKey);
}
