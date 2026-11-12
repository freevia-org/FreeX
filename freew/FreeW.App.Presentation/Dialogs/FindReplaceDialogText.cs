using System.Globalization;
using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>Every localizable string the Word-parity parts of Find &amp; Replace show.</summary>
public enum FindReplaceTextId
{
    OptionMatchPrefix,
    OptionMatchSuffix,
    OptionIgnorePunctuation,
    OptionIgnoreWhiteSpace,
    FormatButton,
    NoFormattingButton,
    FormatSummaryFormat,
    FormattedTextLabel,
    FormatRequiresReplacement,

    SpecialParagraphMark,
    SpecialTabCharacter,
    SpecialAnyCharacter,
    SpecialAnyDigit,
    SpecialAnyLetter,
    SpecialCaretCharacter,
    SpecialManualLineBreak,
    SpecialManualPageBreak,
    SpecialNonbreakingHyphen,
    SpecialNonbreakingSpace,
    SpecialOptionalHyphen,
    SpecialWhiteSpace,
    SpecialEmDash,
    SpecialEnDash,
    SpecialFindWhatText,
    SpecialBeginningOfWord,
    SpecialEndOfWord,

    DescribeFontFormat,
    DescribeSizeFormat,
    DescribeBold,
    DescribeNotBold,
    DescribeItalic,
    DescribeNotItalic,
    DescribeUnderline,
    DescribeNoUnderline,
    DescribeFontColorFormat,
    DescribeHighlight,
    DescribeHighlightColorFormat,
    DescribeNotHighlight,
    DescribeStyleFormat,

    FormatDialogFindTitle,
    FormatDialogReplaceTitle,
    FormatDialogFontLabel,
    FormatDialogSizeLabel,
    FormatDialogColorLabel,
    FormatDialogHighlightColorLabel,
    FormatDialogStyleLabel,
    FormatDialogBold,
    FormatDialogItalic,
    FormatDialogUnderline,
    FormatDialogHighlight,
    FormatDialogAnyValue,
    FormatDialogTriStateHint,
    FormatDialogInvalidSize,
    FormatDialogInvalidColor,

    FoundInStoryFormat,
    StoryHeaderFormat,
    StoryFooterFormat,
    StoryFirstHeaderFormat,
    StoryFirstFooterFormat,
    StoryEvenHeaderFormat,
    StoryEvenFooterFormat,
    StoryFootnoteFormat,
    StoryEndnoteFormat,
    StoryCommentFormat,
    StoryTextBoxFormat,
}

/// <summary>
/// Resolves <see cref="FindReplaceTextId"/> strings through the host's resource lookup, falling back
/// to neutral English -- the same descriptor mechanism the dialog's status text already uses (see
/// <see cref="FindReplaceDialogPlanner.PolicyTextDescriptor"/>).
/// </summary>
public sealed class FindReplaceDialogText
{
    private static readonly IReadOnlyDictionary<FindReplaceTextId, ResourceTextDescriptor> Descriptors =
        new Dictionary<FindReplaceTextId, ResourceTextDescriptor>
        {
            [FindReplaceTextId.OptionMatchPrefix] = new("FreeW_FindReplace_Option_MatchPrefix", "Match prefix"),
            [FindReplaceTextId.OptionMatchSuffix] = new("FreeW_FindReplace_Option_MatchSuffix", "Match suffix"),
            [FindReplaceTextId.OptionIgnorePunctuation] = new("FreeW_FindReplace_Option_IgnorePunctuation", "Ignore punctuation characters"),
            [FindReplaceTextId.OptionIgnoreWhiteSpace] = new("FreeW_FindReplace_Option_IgnoreWhiteSpace", "Ignore white-space characters"),
            [FindReplaceTextId.FormatButton] = new("FreeW_FindReplace_Format_Button", "Format…"),
            [FindReplaceTextId.NoFormattingButton] = new("FreeW_FindReplace_NoFormatting_Button", "No Formatting"),
            [FindReplaceTextId.FormatSummaryFormat] = new("FreeW_FindReplace_Format_Summary_Format", "Format: {0}"),
            [FindReplaceTextId.FormattedTextLabel] = new("FreeW_FindReplace_FormattedText", "formatted text"),
            [FindReplaceTextId.FormatRequiresReplacement] = new("FreeW_FindReplace_NothingToReplaceWith", "Enter replacement text or choose replacement formatting."),

            [FindReplaceTextId.SpecialParagraphMark] = new("FreeW_FindReplace_Special_ParagraphMark", "Paragraph Mark"),
            [FindReplaceTextId.SpecialTabCharacter] = new("FreeW_FindReplace_Special_TabCharacter", "Tab Character"),
            [FindReplaceTextId.SpecialAnyCharacter] = new("FreeW_FindReplace_Special_AnyCharacter", "Any Character"),
            [FindReplaceTextId.SpecialAnyDigit] = new("FreeW_FindReplace_Special_AnyDigit", "Any Digit"),
            [FindReplaceTextId.SpecialAnyLetter] = new("FreeW_FindReplace_Special_AnyLetter", "Any Letter"),
            [FindReplaceTextId.SpecialCaretCharacter] = new("FreeW_FindReplace_Special_CaretCharacter", "Caret Character"),
            [FindReplaceTextId.SpecialManualLineBreak] = new("FreeW_FindReplace_Special_ManualLineBreak", "Manual Line Break"),
            [FindReplaceTextId.SpecialManualPageBreak] = new("FreeW_FindReplace_Special_ManualPageBreak", "Manual Page Break"),
            [FindReplaceTextId.SpecialNonbreakingHyphen] = new("FreeW_FindReplace_Special_NonbreakingHyphen", "Nonbreaking Hyphen"),
            [FindReplaceTextId.SpecialNonbreakingSpace] = new("FreeW_FindReplace_Special_NonbreakingSpace", "Nonbreaking Space"),
            [FindReplaceTextId.SpecialOptionalHyphen] = new("FreeW_FindReplace_Special_OptionalHyphen", "Optional Hyphen"),
            [FindReplaceTextId.SpecialWhiteSpace] = new("FreeW_FindReplace_Special_WhiteSpace", "White Space"),
            [FindReplaceTextId.SpecialEmDash] = new("FreeW_FindReplace_Special_EmDash", "Em Dash"),
            [FindReplaceTextId.SpecialEnDash] = new("FreeW_FindReplace_Special_EnDash", "En Dash"),
            [FindReplaceTextId.SpecialFindWhatText] = new("FreeW_FindReplace_Special_FindWhatText", "Find What Text"),
            [FindReplaceTextId.SpecialBeginningOfWord] = new("FreeW_FindReplace_Special_BeginningOfWord", "Beginning of Word"),
            [FindReplaceTextId.SpecialEndOfWord] = new("FreeW_FindReplace_Special_EndOfWord", "End of Word"),

            [FindReplaceTextId.DescribeFontFormat] = new("FreeW_FindReplace_Describe_Font_Format", "Font: {0}"),
            [FindReplaceTextId.DescribeSizeFormat] = new("FreeW_FindReplace_Describe_Size_Format", "{0} pt"),
            [FindReplaceTextId.DescribeBold] = new("FreeW_FindReplace_Describe_Bold", "Bold"),
            [FindReplaceTextId.DescribeNotBold] = new("FreeW_FindReplace_Describe_NotBold", "Not Bold"),
            [FindReplaceTextId.DescribeItalic] = new("FreeW_FindReplace_Describe_Italic", "Italic"),
            [FindReplaceTextId.DescribeNotItalic] = new("FreeW_FindReplace_Describe_NotItalic", "Not Italic"),
            [FindReplaceTextId.DescribeUnderline] = new("FreeW_FindReplace_Describe_Underline", "Underline"),
            [FindReplaceTextId.DescribeNoUnderline] = new("FreeW_FindReplace_Describe_NoUnderline", "No underline"),
            [FindReplaceTextId.DescribeFontColorFormat] = new("FreeW_FindReplace_Describe_FontColor_Format", "Font color: {0}"),
            [FindReplaceTextId.DescribeHighlight] = new("FreeW_FindReplace_Describe_Highlight", "Highlight"),
            [FindReplaceTextId.DescribeHighlightColorFormat] = new("FreeW_FindReplace_Describe_HighlightColor_Format", "Highlight: {0}"),
            [FindReplaceTextId.DescribeNotHighlight] = new("FreeW_FindReplace_Describe_NotHighlight", "Not Highlight"),
            [FindReplaceTextId.DescribeStyleFormat] = new("FreeW_FindReplace_Describe_Style_Format", "Style: {0}"),

            [FindReplaceTextId.FormatDialogFindTitle] = new("FreeW_FindReplace_FormatDialog_FindTitle", "Find Formatting"),
            [FindReplaceTextId.FormatDialogReplaceTitle] = new("FreeW_FindReplace_FormatDialog_ReplaceTitle", "Replace Formatting"),
            [FindReplaceTextId.FormatDialogFontLabel] = new("FreeW_FindReplace_FormatDialog_Font_Label", "Font:"),
            [FindReplaceTextId.FormatDialogSizeLabel] = new("FreeW_FindReplace_FormatDialog_Size_Label", "Size:"),
            [FindReplaceTextId.FormatDialogColorLabel] = new("FreeW_FindReplace_FormatDialog_Color_Label", "Font color (hex):"),
            [FindReplaceTextId.FormatDialogHighlightColorLabel] = new("FreeW_FindReplace_FormatDialog_HighlightColor_Label", "Highlight color (hex):"),
            [FindReplaceTextId.FormatDialogStyleLabel] = new("FreeW_FindReplace_FormatDialog_Style_Label", "Paragraph style:"),
            [FindReplaceTextId.FormatDialogBold] = new("FreeW_FindReplace_FormatDialog_Bold", "Bold"),
            [FindReplaceTextId.FormatDialogItalic] = new("FreeW_FindReplace_FormatDialog_Italic", "Italic"),
            [FindReplaceTextId.FormatDialogUnderline] = new("FreeW_FindReplace_FormatDialog_Underline", "Underline"),
            [FindReplaceTextId.FormatDialogHighlight] = new("FreeW_FindReplace_FormatDialog_Highlight", "Highlight"),
            [FindReplaceTextId.FormatDialogAnyValue] = new("FreeW_FindReplace_FormatDialog_Any", "(any)"),
            [FindReplaceTextId.FormatDialogTriStateHint] = new("FreeW_FindReplace_FormatDialog_TriStateHint", "A filled box means the setting is not part of the search."),
            [FindReplaceTextId.FormatDialogInvalidSize] = new("FreeW_FindReplace_FormatDialog_InvalidSize", "Enter a font size between 1 and 1638."),
            [FindReplaceTextId.FormatDialogInvalidColor] = new("FreeW_FindReplace_FormatDialog_InvalidColor", "Enter a color as six hexadecimal digits, for example FF0000."),

            [FindReplaceTextId.FoundInStoryFormat] = new("FreeW_FindReplace_FoundInStory_Format", "Found in {0}: {1}"),
            [FindReplaceTextId.StoryHeaderFormat] = new("FreeW_FindReplace_Story_Header_Format", "header (section {0})"),
            [FindReplaceTextId.StoryFooterFormat] = new("FreeW_FindReplace_Story_Footer_Format", "footer (section {0})"),
            [FindReplaceTextId.StoryFirstHeaderFormat] = new("FreeW_FindReplace_Story_FirstHeader_Format", "first-page header (section {0})"),
            [FindReplaceTextId.StoryFirstFooterFormat] = new("FreeW_FindReplace_Story_FirstFooter_Format", "first-page footer (section {0})"),
            [FindReplaceTextId.StoryEvenHeaderFormat] = new("FreeW_FindReplace_Story_EvenHeader_Format", "even-page header (section {0})"),
            [FindReplaceTextId.StoryEvenFooterFormat] = new("FreeW_FindReplace_Story_EvenFooter_Format", "even-page footer (section {0})"),
            [FindReplaceTextId.StoryFootnoteFormat] = new("FreeW_FindReplace_Story_Footnote_Format", "footnote {0}"),
            [FindReplaceTextId.StoryEndnoteFormat] = new("FreeW_FindReplace_Story_Endnote_Format", "endnote {0}"),
            [FindReplaceTextId.StoryCommentFormat] = new("FreeW_FindReplace_Story_Comment_Format", "comment {0}"),
            [FindReplaceTextId.StoryTextBoxFormat] = new("FreeW_FindReplace_Story_TextBox_Format", "text box {0}"),
        };

    private readonly Func<string, string?>? _getText;

    public FindReplaceDialogText(Func<string, string?>? getText = null) => _getText = getText;

    public static FindReplaceDialogText NeutralEnglish { get; } = new();

    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
        Descriptors.Values.Select(descriptor => descriptor.ResourceKey).ToArray();

    /// <summary>The neutral-English fallback for every resource key, for resource-parity checks.</summary>
    public static IReadOnlyDictionary<string, string> NeutralFallbacks { get; } =
        Descriptors.Values.ToDictionary(
            descriptor => descriptor.ResourceKey,
            descriptor => descriptor.FallbackText,
            StringComparer.Ordinal);

    public string Get(FindReplaceTextId id) => Descriptors[id].Resolve(_getText);

    public string Format(FindReplaceTextId id, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(id), args);
}
