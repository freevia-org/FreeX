using FreeW.App.Localization;

namespace FreeW.App.Presentation;

/// <summary>Owns localized semantic text shared by the FreeW WPF and Avalonia renderers.</summary>
public static class FreeWUiTextCatalog
{
    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        "Common_Zoom",
        "Shell_ZoomIn_Label",
        "Shell_ZoomOut_Label",
        "Common_Themes",
        "Design_StyleSets_Title",
        "Design_Colors_Title",
        "Design_Fonts_Title",
        "Design_ParagraphSpacing_Title",
        "Design_ParagraphSpacing_CompactLabel",
        "Design_Effects_Title",
        "Design_FontSample_Heading",
        "Design_FontSample_Body",
        "Design_ThemeColors_AutomationNameFormat",
        "Design_StyleSet_AutomationNameFormat",
        "Design_FontSet_AutomationNameFormat",
        "Design_ParagraphSpacing_AutomationNameFormat",
        "Design_EffectSet_AutomationNameFormat",
        "Design_TableStyles_Title",
        "Design_TableStyles_CompactLabel",
        "Design_TableStyle_AutomationNameFormat",
        "Design_Styles_MoreToolTip",
        "Dialog_DateTime_Title",
        "Dialog_DateTime_FormatsLabel",
        "Dialog_DateTime_UpdateAutomatically",
        "Dialog_DateTime_UpdateAutomaticallyToolTip",
        "Dialog_MultilevelList_Title",
        "Dialog_MultilevelList_Description",
        "Dialog_MultilevelList_LevelsLabel",
        "Dialog_MultilevelList_Level1StartAtLabel",
        "Dialog_MultilevelList_Level2StartAtLabel",
        "Dialog_MultilevelList_Level1NumberStyleLabel",
        "Dialog_MultilevelList_Level2NumberStyleLabel",
        "Dialog_MultilevelList_Level3NumberStyleLabel",
        "Dialog_MultilevelList_Level1StartAtToolTip",
        "Dialog_MultilevelList_Level2StartAtToolTip",
        "Dialog_MultilevelList_PositiveStartAtMessage",
        "Pane_Thesaurus_Heading",
        "Pane_Thesaurus_EmptyWordStatus",
        "Pane_Thesaurus_NoSynonymsStatus",
        "Pane_Thesaurus_InsertToolTipFormat",
        "Pane_Thesaurus_CopyToolTipFormat",
        "Pane_Thesaurus_CopyButton",
        "Pane_Thesaurus_Adjective",
        "Pane_Thesaurus_Adverb",
        "Pane_Thesaurus_Noun",
        "Pane_Thesaurus_Verb",
        "Pane_Thesaurus_Preposition",
        "Pane_Thesaurus_Pronoun",
        "Pane_Notes_Heading",
        "Common_Apply",
        "Pane_Notes_Delete",
        "Pane_Notes_ApplyToolTip",
        "Pane_Notes_DeleteToolTip",
        "Pane_Notes_FootnoteFormat",
        "Pane_Notes_EndnoteFormat",
        "Dialog_Note_InsertFootnoteTitle",
        "Dialog_Note_InsertEndnoteTitle",
        "Dialog_Note_FootnoteTextLabel",
        "Dialog_Note_EndnoteTextLabel",
        "Dialog_Note_Ok",
        "Dialog_Note_Cancel",
        "MailMerge_ErrorReport_WindowTitle",
    ];

    public static string Zoom => Text("Common_Zoom");
    public static string ZoomIn => Text("Shell_ZoomIn_Label");
    public static string ZoomOut => Text("Shell_ZoomOut_Label");
    public static string Themes => Text("Common_Themes");
    public static string StyleSets => Text("Design_StyleSets_Title");
    public static string Colors => Text("Design_Colors_Title");
    public static string Fonts => Text("Design_Fonts_Title");
    public static string ParagraphSpacing => Text("Design_ParagraphSpacing_Title");
    public static string ParagraphSpacingCompact => Text("Design_ParagraphSpacing_CompactLabel");
    public static string Effects => Text("Design_Effects_Title");
    public static string FontSampleHeading => Text("Design_FontSample_Heading");
    public static string FontSampleBody => Text("Design_FontSample_Body");
    public static string TableStyles => Text("Design_TableStyles_Title");
    public static string TableStylesCompact => Text("Design_TableStyles_CompactLabel");
    public static string MoreStylesToolTip => Text("Design_Styles_MoreToolTip");

    public static string ThemeColorsAutomationName(string name) =>
        Format("Design_ThemeColors_AutomationNameFormat", name);

    public static string StyleSetAutomationName(string name) =>
        Format("Design_StyleSet_AutomationNameFormat", name);

    public static string FontSetAutomationName(string name) =>
        Format("Design_FontSet_AutomationNameFormat", name);

    public static string ParagraphSpacingAutomationName(string name) =>
        Format("Design_ParagraphSpacing_AutomationNameFormat", name);

    public static string EffectSetAutomationName(string name) =>
        Format("Design_EffectSet_AutomationNameFormat", name);

    public static string TableStyleAutomationName(string name) =>
        Format("Design_TableStyle_AutomationNameFormat", name);

    public static string DateTimeTitle => Text("Dialog_DateTime_Title");
    public static string DateTimeFormatsLabel => Text("Dialog_DateTime_FormatsLabel");
    public static string DateTimeUpdateAutomatically => Text("Dialog_DateTime_UpdateAutomatically");
    public static string DateTimeUpdateAutomaticallyToolTip =>
        Text("Dialog_DateTime_UpdateAutomaticallyToolTip");

    public static string MultilevelListTitle => Text("Dialog_MultilevelList_Title");
    public static string MultilevelListDescription => Text("Dialog_MultilevelList_Description");
    public static string MultilevelListLevelsLabel => Text("Dialog_MultilevelList_LevelsLabel");
    public static string MultilevelListLevel1StartAtLabel => Text("Dialog_MultilevelList_Level1StartAtLabel");
    public static string MultilevelListLevel2StartAtLabel => Text("Dialog_MultilevelList_Level2StartAtLabel");
    public static string MultilevelListLevel1NumberStyleLabel =>
        Text("Dialog_MultilevelList_Level1NumberStyleLabel");
    public static string MultilevelListLevel2NumberStyleLabel =>
        Text("Dialog_MultilevelList_Level2NumberStyleLabel");
    public static string MultilevelListLevel3NumberStyleLabel =>
        Text("Dialog_MultilevelList_Level3NumberStyleLabel");
    public static string MultilevelListLevel1StartAtToolTip =>
        Text("Dialog_MultilevelList_Level1StartAtToolTip");
    public static string MultilevelListLevel2StartAtToolTip =>
        Text("Dialog_MultilevelList_Level2StartAtToolTip");
    public static string MultilevelListPositiveStartAtMessage =>
        Text("Dialog_MultilevelList_PositiveStartAtMessage");

    public static string ThesaurusHeading => Text("Pane_Thesaurus_Heading");
    public static string ThesaurusEmptyWordStatus => Text("Pane_Thesaurus_EmptyWordStatus");
    public static string ThesaurusNoSynonymsStatus => Text("Pane_Thesaurus_NoSynonymsStatus");
    public static string ThesaurusCopyButton => Text("Pane_Thesaurus_CopyButton");
    public static string ThesaurusInsertToolTip(string synonym, string sourceWord) =>
        Format("Pane_Thesaurus_InsertToolTipFormat", synonym, sourceWord);
    public static string ThesaurusCopyToolTip(string synonym) =>
        Format("Pane_Thesaurus_CopyToolTipFormat", synonym);

    public static string ThesaurusSenseLabel(string label) => label.Trim() switch
    {
        "adj" => Text("Pane_Thesaurus_Adjective"),
        "adv" => Text("Pane_Thesaurus_Adverb"),
        "noun" => Text("Pane_Thesaurus_Noun"),
        "verb" => Text("Pane_Thesaurus_Verb"),
        "prep" => Text("Pane_Thesaurus_Preposition"),
        "pron" => Text("Pane_Thesaurus_Pronoun"),
        var value => value.Replace('_', ' '),
    };

    public static string NotesHeading => Text("Pane_Notes_Heading");
    public static string NotesApply => Text("Common_Apply");
    public static string NotesDelete => Text("Pane_Notes_Delete");
    public static string NotesApplyToolTip => Text("Pane_Notes_ApplyToolTip");
    public static string NotesDeleteToolTip => Text("Pane_Notes_DeleteToolTip");
    public static string FootnoteLabel(int id) => Format("Pane_Notes_FootnoteFormat", id);
    public static string EndnoteLabel(int id) => Format("Pane_Notes_EndnoteFormat", id);
    public static string InsertFootnoteTitle => Text("Dialog_Note_InsertFootnoteTitle");
    public static string InsertEndnoteTitle => Text("Dialog_Note_InsertEndnoteTitle");
    public static string FootnoteTextLabel => Text("Dialog_Note_FootnoteTextLabel");
    public static string EndnoteTextLabel => Text("Dialog_Note_EndnoteTextLabel");
    public static string NoteDialogOk => Text("Dialog_Note_Ok");
    public static string NoteDialogCancel => Text("Dialog_Note_Cancel");
    public static string MailMergeErrorReportWindowTitle => Text("MailMerge_ErrorReport_WindowTitle");

    private static string Text(string resourceKey) => Loc.Get(resourceKey);
    private static string Format(string resourceKey, params object?[] arguments) =>
        Loc.Format(resourceKey, arguments);
}
