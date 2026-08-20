using System.Globalization;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public sealed record ContentControlChoice(string Label, string Value, string DisplayText);

public sealed record ContentControlChromePlan(
    string Tooltip,
    string DisplayText,
    bool IsInteractive,
    IReadOnlyList<ContentControlChoice> Choices);

public sealed record ContentControlDateChoice(
    string Label,
    string Value,
    string DisplayText,
    DateTime Date);

public static class ContentControlInteractionPlanner
{
    public const string DefaultPromptText = "Click to enter text";

    private static readonly ContentControlListItem[] s_defaultListItems =
    [
        new("Choose an item"),
        new("Item 1"),
        new("Item 2"),
        new("Item 3")
    ];

    public static IReadOnlyList<ContentControlListItem> DefaultListItems => s_defaultListItems;

    public static string PromptText(string? selectedText) =>
        string.IsNullOrEmpty(selectedText) ? DefaultPromptText : selectedText!;

    public static IReadOnlyList<ContentControlListItem> ListItemsOrDefault(
        IReadOnlyList<ContentControlListItem>? items) =>
        items is { Count: > 0 } ? items : DefaultListItems;

    public static string DateFormatOrDefault(string? dateFormat) =>
        string.IsNullOrEmpty(dateFormat) ? ContentControl.DefaultDateFormat : dateFormat!;

    public static string FormatDate(
        ContentControl control,
        DateTime date,
        CultureInfo? culture = null) =>
        date.ToString(DateFormatOrDefault(control.DateFormat), culture ?? CultureInfo.CurrentCulture);

    public static string FormatDate(
        string? dateFormat,
        DateTime date,
        CultureInfo? culture = null) =>
        date.ToString(DateFormatOrDefault(dateFormat), culture ?? CultureInfo.CurrentCulture);

    public static bool CanEditExistingContentControl(
        Run run,
        RestrictEditingEnforcementPolicy protectionPolicy) =>
        run.Control is not null
        && run.Control.LockMode is not (ContentControlLockMode.ContentLocked or ContentControlLockMode.ControlAndContentLocked)
        && protectionPolicy.DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed;

    /// <summary>
    /// Whether a content control may be REMOVED. Word's <c>w:lock</c> separates the two protections: the
    /// <c>sdtLocked</c> family (<see cref="ContentControlLockMode.ControlLocked"/> and
    /// <see cref="ContentControlLockMode.ControlAndContentLocked"/>) forbids deleting the control itself,
    /// while <c>contentLocked</c> only freezes its text — so a plain <c>sdtLocked</c> field is still
    /// typable, and an editing gesture that would delete such a field must decline instead. A null
    /// control is ordinary content and always deletable.
    /// </summary>
    public static bool CanDeleteContentControl(ContentControl? control) =>
        control is null
        || control.LockMode is not (ContentControlLockMode.ControlLocked
            or ContentControlLockMode.ControlAndContentLocked);

    /// <summary>
    /// The block-level counterpart of <see cref="CanDeleteContentControl"/>: a body <c>w:sdt</c> that
    /// wraps whole paragraphs/tables. A delete lock anywhere in the nesting chain (see
    /// <see cref="FreeW.Core.Model.BlockContentControl.Parent"/>) protects the blocks inside it, matching
    /// Word's nested-w:sdt semantics.
    /// </summary>
    public static bool CanDeleteBlockContentControl(FreeW.Core.Model.BlockContentControl? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current.LockMode is ContentControlLockMode.ControlLocked
                or ContentControlLockMode.ControlAndContentLocked)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The block-level (body <c>w:sdt</c>) counterpart of <see cref="CanEditExistingContentControl"/>.
    /// Word can lock a whole paragraph/table -- not just an inline field -- by wrapping it in a body-level
    /// structured document tag whose <c>w:sdtPr</c> carries <c>w:lock w:val="sdtContentLocked"</c> (or
    /// <c>"contentLocked"</c>); that lock is modeled on
    /// <see cref="FreeW.Core.Model.BlockContentControl.LockMode"/>, reached from a block via
    /// <see cref="FreeW.Core.Model.Block.BlockContentControl"/> -- a completely separate slot from any
    /// run's <see cref="ContentControl"/>. A block-level control can itself be nested inside another one (see
    /// <see cref="FreeW.Core.Model.BlockContentControl.Parent"/>, set by the reader for a nested body
    /// <c>w:sdt</c>); a lock anywhere in that ancestor chain blocks editing the whole nested content, matching
    /// Word's own nested-w:sdt semantics, so this walks <c>Parent</c> rather than checking only
    /// <paramref name="control"/> itself.
    /// </summary>
    public static bool CanEditExistingBlockContentControl(
        FreeW.Core.Model.BlockContentControl control,
        RestrictEditingEnforcementPolicy protectionPolicy) =>
        !IsBlockContentControlLocked(control)
        && protectionPolicy.DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed;

    /// <summary>
    /// Pure lock-mode check backing <see cref="CanEditExistingBlockContentControl"/>: is
    /// <paramref name="control"/> (or any block-level ancestor it is nested inside, see
    /// <see cref="FreeW.Core.Model.BlockContentControl.Parent"/>) locked --
    /// <see cref="ContentControlLockMode.ContentLocked"/> or <see cref="ContentControlLockMode.ControlAndContentLocked"/>
    /// anywhere in the chain? No Filling-In-Forms policy involved, so this is also the right check for a
    /// purely structural "is this paragraph editable at all" query -- e.g.
    /// <c>DocumentEditingSession.IsPortableBodyTextParagraph</c>, which must decline the portable body-edit
    /// session's typing/Backspace/Delete/Enter paths for a locked block-level control the same way it
    /// already declines for a locked run-level one (<c>run.Control</c>), without pulling in the
    /// protection-policy dependency <see cref="CanEditExistingBlockContentControl"/> carries. A null
    /// <paramref name="control"/> (no block-level content control at all) is never locked.
    /// </summary>
    public static bool IsBlockContentControlLocked(FreeW.Core.Model.BlockContentControl? control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current.LockMode is ContentControlLockMode.ContentLocked or ContentControlLockMode.ControlAndContentLocked)
                return true;
        }

        return false;
    }

    public static string Tooltip(ContentControl control)
    {
        var label = control.Alias is { Length: > 0 } alias ? alias : null;
        return control.Kind switch
        {
            ContentControlKind.CheckBox => label is null
                ? "Checkbox content control (click to toggle)" : $"Checkbox: {label}",
            ContentControlKind.RichText => label is null
                ? "Rich-text content control" : $"Rich-text control: {label}",
            ContentControlKind.DatePicker => label is null
                ? "Date picker content control (click to pick a date)" : $"Date picker: {label}",
            ContentControlKind.DropDownList => label is null
                ? "Drop-down list content control (click to choose)" : $"Drop-down list: {label}",
            ContentControlKind.ComboBox => label is null
                ? "Combo box content control (click to choose or type)" : $"Combo box: {label}",
            _ => label is null ? "Plain-text content control" : $"Content control: {label}"
        };
    }

    public static string DisplayText(Run run) =>
        run.Control is { Kind: ContentControlKind.CheckBox } control
            ? CheckBoxGlyph(control.Checked)
            : run.Text;

    public static IReadOnlyList<ContentControlChoice> Choices(ContentControl control)
    {
        if (control.Kind is not (ContentControlKind.DropDownList or ContentControlKind.ComboBox))
            return [];

        return control.Items
            .Select(item => new ContentControlChoice(item.DisplayText, item.Value, item.DisplayText))
            .ToArray();
    }

    public static IReadOnlyList<ContentControlDateChoice> RelativeDateChoices(
        ContentControl control,
        DateTime? today = null,
        CultureInfo? culture = null)
    {
        if (control.Kind != ContentControlKind.DatePicker)
            return [];

        var anchor = today ?? DateTime.Today;
        return
        [
            DateChoice("Today", "today", anchor, control, culture),
            DateChoice("Yesterday", "yesterday", anchor.AddDays(-1), control, culture),
            DateChoice("Tomorrow", "tomorrow", anchor.AddDays(1), control, culture)
        ];
    }

    /// <summary>
    /// Clones a content-control run with replaced text, preserving every other run mark (formatting,
    /// hyperlink, revision, comment id, and the control itself). Returns null when the run carries no
    /// control. The keyboard text-editing path uses this so typing inside a control replaces just that
    /// run — rebuilding the paragraph's runs from cells would drop the w:sdt.
    /// </summary>
    public static Run? WithText(Run run, string text) =>
        run.Control is { } control ? CloneWith(run, text, control) : null;

    /// <summary>
    /// Whether a content control holds free-form text the user may type into. A check box, date picker
    /// and drop-down list own their text (glyph / formatted date / picked item), so their text only
    /// changes through the control's own interaction; a combo box, like a plain-text or rich-text
    /// control, accepts typed text in Word.
    /// </summary>
    public static bool IsTextEntryControl(ContentControl control) =>
        control.Kind is ContentControlKind.PlainText
            or ContentControlKind.RichText
            or ContentControlKind.ComboBox;

    /// <summary>
    /// Whether the user may type into <paramref name="run"/>'s content control: it must be a text-entry
    /// control that the document's protection state and the control's own lock leave editable.
    /// </summary>
    public static bool CanEditContentControlText(
        Run run,
        RestrictEditingEnforcementPolicy protectionPolicy) =>
        run.Control is { } control
        && IsTextEntryControl(control)
        && CanEditExistingContentControl(run, protectionPolicy);

    public static Run? ToggleCheckBox(Run run)
    {
        if (run.Control is not { Kind: ContentControlKind.CheckBox } control)
            return null;

        var updated = control with { Checked = !control.Checked };
        return CloneWith(run, ResolveCheckBoxGlyph(updated), updated);
    }

    public static Run? SelectItem(Run run, int itemIndex)
    {
        if (run.Control is not { } control
            || control.Kind is not (ContentControlKind.DropDownList or ContentControlKind.ComboBox)
            || itemIndex < 0
            || itemIndex >= control.Items.Count)
        {
            return null;
        }

        return CloneWith(run, control.Items[itemIndex].DisplayText, control);
    }

    public static Run? SelectRelativeDate(
        Run run,
        int choiceIndex,
        DateTime? today = null,
        CultureInfo? culture = null)
    {
        if (run.Control is not { Kind: ContentControlKind.DatePicker } control)
            return null;

        var choices = RelativeDateChoices(control, today, culture);
        if (choiceIndex < 0 || choiceIndex >= choices.Count)
            return null;

        return CloneWith(run, choices[choiceIndex].DisplayText, control);
    }

    private static bool IsInteractive(ContentControl control) =>
        control.Kind is ContentControlKind.CheckBox
            or ContentControlKind.DatePicker
            or ContentControlKind.DropDownList
            or ContentControlKind.ComboBox;

    private static ContentControlDateChoice DateChoice(
        string label,
        string value,
        DateTime date,
        ContentControl control,
        CultureInfo? culture)
    {
        var text = FormatDate(control, date, culture);
        return new ContentControlDateChoice(label, value, text, date);
    }

    private static string CheckBoxGlyph(bool isChecked) =>
        isChecked ? ContentControl.CheckedGlyph : ContentControl.UncheckedGlyph;

    /// <summary>
    /// The glyph to write into a checkbox run's text for <paramref name="control"/>'s current
    /// <see cref="ContentControl.Checked"/> state: the document's own authored symbol from
    /// <see cref="ContentControl.CheckBoxMetadata"/> (w14:checkedState/uncheckedState) when present and
    /// a valid Unicode code point, falling back to the app's fixed <see cref="ContentControl.CheckedGlyph"/>
    /// / <see cref="ContentControl.UncheckedGlyph"/> otherwise -- mirroring
    /// <c>CustomXmlDataBindingResolver.ResolveCheckBoxGlyph</c>'s glyph resolution so a toggle never
    /// overwrites a custom checkbox symbol with the generic ballot-box character.
    /// </summary>
    private static string ResolveCheckBoxGlyph(ContentControl control)
    {
        var state = control.Checked
            ? control.CheckBoxMetadata?.CheckedState
            : control.CheckBoxMetadata?.UncheckedState;
        if (state?.GlyphCodePoint is { Length: > 0 } code
            && int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint)
            && Rune.IsValid(codePoint))
        {
            return char.ConvertFromUtf32(codePoint);
        }

        return CheckBoxGlyph(control.Checked);
    }

    private static Run CloneWith(Run source, string text, ContentControl control) => new(text, source.Formatting)
    {
        Image = source.Image,
        Equation = source.Equation,
        Shape = source.Shape,
        WordArt = source.WordArt,
        Chart = source.Chart,
        EmbeddedObject = source.EmbeddedObject,
        SmartArt = source.SmartArt,
        PreservedDrawing = source.PreservedDrawing,
        DrawingGroup = source.DrawingGroup,
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        FieldKind = source.FieldKind,
        TableFormula = source.TableFormula,
        Citation = source.Citation,
        CrossReference = source.CrossReference,
        ComplexField = source.ComplexField,
        FootnoteId = source.FootnoteId,
        EndnoteId = source.EndnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        IsPageBreak = source.IsPageBreak,
        IsColumnBreak = source.IsColumnBreak,
        Revision = source.Revision,
        Control = control,
        RevisionAuthor = source.RevisionAuthor,
        RevisionDateXml = source.RevisionDateXml,
        FormatRevision = source.FormatRevision
    };
}
