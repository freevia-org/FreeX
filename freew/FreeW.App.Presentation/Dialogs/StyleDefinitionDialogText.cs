using FreeW.App.Localization;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Localized text for Word's Create New Style from Formatting / Modify Style dialog and the Manage
/// Styles additions. Every key is listed in <see cref="RequiredResourceKeys"/> so the renderer
/// localization-integrity tests fail when a resource goes missing.
/// </summary>
public static class StyleDefinitionDialogText
{
    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        "StyleDefinition_NewTitle",
        "StyleDefinition_ModifyTitle",
        "StyleDefinition_PropertiesGroup",
        "StyleDefinition_NameLabel",
        "StyleDefinition_TypeLabel",
        "StyleDefinition_BasedOnLabel",
        "StyleDefinition_NextStyleLabel",
        "StyleDefinition_FormattingGroup",
        "StyleDefinition_FontLabel",
        "StyleDefinition_SizeLabel",
        "StyleDefinition_Bold",
        "StyleDefinition_Italic",
        "StyleDefinition_Underline",
        "StyleDefinition_Strikethrough",
        "StyleDefinition_FontColorLabel",
        "StyleDefinition_HighlightLabel",
        "StyleDefinition_AlignmentLabel",
        "StyleDefinition_Type_Paragraph",
        "StyleDefinition_Type_Character",
        "StyleDefinition_Type_Linked",
        "StyleDefinition_AddToGallery",
        "StyleDefinition_FormatButton",
        "StyleDefinition_Format_Font",
        "StyleDefinition_Format_Paragraph",
        "StyleDefinition_Format_Tabs",
        "StyleDefinition_Format_Border",
        "StyleDefinition_PreviewLabel",
        "StyleDefinition_PreviewSample",
        "StyleDefinition_NoBasedOn",
        "StyleDefinition_SameStyle",
        "StyleDefinition_Error_EmptyName",
        "StyleDefinition_Error_DuplicateNameFormat",
        "StyleDefinition_Error_FontSize",
        "StyleDefinition_Description_FontFormat",
        "StyleDefinition_Description_SizeFormat",
        "StyleDefinition_Description_FontColorFormat",
        "StyleDefinition_Description_HighlightFormat",
        "StyleDefinition_Description_Align_Left",
        "StyleDefinition_Description_Align_Center",
        "StyleDefinition_Description_Align_Right",
        "StyleDefinition_Description_Align_Justify",
        "StyleDefinition_Description_IndentLeftFormat",
        "StyleDefinition_Description_IndentRightFormat",
        "StyleDefinition_Description_FirstLineFormat",
        "StyleDefinition_Description_HangingFormat",
        "StyleDefinition_Description_SpaceBeforeFormat",
        "StyleDefinition_Description_SpaceAfterFormat",
        "StyleDefinition_Description_LineSpacingFormat",
        "StyleDefinition_Description_KeepWithNext",
        "StyleDefinition_Description_KeepLines",
        "StyleDefinition_Description_PageBreakBefore",
        "StyleDefinition_Description_Border",
        "StyleDefinition_Description_ShadingFormat",
        "StyleDefinition_Description_TabStopsFormat",
        "StyleDefinition_Description_Linked",
        "StyleDefinition_Description_ShowInGallery",
        "StyleDefinition_Description_StyleFormat",
        "StyleDefinition_Description_BasedOnFormat",
        "StyleDefinition_Description_FollowingFormat",
        "StyleGallery_Context_UpdateToMatchFormat",
        "StyleGallery_Context_Modify",
        "StyleGallery_Context_RemoveFromGallery",
        "StyleGallery_Context_DeleteFormat",
    ];

    public static string NewTitle => Text("StyleDefinition_NewTitle");
    public static string ModifyTitle => Text("StyleDefinition_ModifyTitle");
    public static string PropertiesGroup => Text("StyleDefinition_PropertiesGroup");
    public static string NameLabel => Text("StyleDefinition_NameLabel");
    public static string TypeLabel => Text("StyleDefinition_TypeLabel");
    public static string BasedOnLabel => Text("StyleDefinition_BasedOnLabel");
    public static string NextStyleLabel => Text("StyleDefinition_NextStyleLabel");
    public static string FormattingGroup => Text("StyleDefinition_FormattingGroup");
    public static string FontLabel => Text("StyleDefinition_FontLabel");
    public static string SizeLabel => Text("StyleDefinition_SizeLabel");
    public static string Bold => Text("StyleDefinition_Bold");
    public static string Italic => Text("StyleDefinition_Italic");
    public static string Underline => Text("StyleDefinition_Underline");
    public static string Strikethrough => Text("StyleDefinition_Strikethrough");
    public static string FontColorLabel => Text("StyleDefinition_FontColorLabel");
    public static string HighlightLabel => Text("StyleDefinition_HighlightLabel");
    public static string AlignmentLabel => Text("StyleDefinition_AlignmentLabel");
    public static string AddToGallery => Text("StyleDefinition_AddToGallery");
    public static string FormatButton => Text("StyleDefinition_FormatButton");
    public static string PreviewLabel => Text("StyleDefinition_PreviewLabel");
    public static string PreviewSample => Text("StyleDefinition_PreviewSample");
    public static string NoBasedOn => Text("StyleDefinition_NoBasedOn");
    public static string SameStyle => Text("StyleDefinition_SameStyle");
    public static string EmptyNameError => Text("StyleDefinition_Error_EmptyName");
    public static string FontSizeError => Text("StyleDefinition_Error_FontSize");
    public static string DuplicateNameError(string name) =>
        Format("StyleDefinition_Error_DuplicateNameFormat", name);
    public static string GalleryModify => Text("StyleGallery_Context_Modify");
    public static string GalleryRemove => Text("StyleGallery_Context_RemoveFromGallery");
    public static string GalleryUpdateToMatch(string styleName) =>
        Format("StyleGallery_Context_UpdateToMatchFormat", styleName);
    public static string GalleryDelete(string styleName) =>
        Format("StyleGallery_Context_DeleteFormat", styleName);

    public static string KindLabel(StyleDefinitionKind kind) => kind switch
    {
        StyleDefinitionKind.Character => Text("StyleDefinition_Type_Character"),
        StyleDefinitionKind.Linked => Text("StyleDefinition_Type_Linked"),
        _ => Text("StyleDefinition_Type_Paragraph"),
    };

    public static string FormatCommandLabel(StyleDefinitionFormatCommand command) => command switch
    {
        StyleDefinitionFormatCommand.Paragraph => Text("StyleDefinition_Format_Paragraph"),
        StyleDefinitionFormatCommand.Tabs => Text("StyleDefinition_Format_Tabs"),
        StyleDefinitionFormatCommand.Border => Text("StyleDefinition_Format_Border"),
        _ => Text("StyleDefinition_Format_Font"),
    };

    internal static string Text(string resourceKey) => Loc.Get(resourceKey);

    internal static string Format(string resourceKey, params object?[] arguments) =>
        Loc.Format(resourceKey, arguments);
}
