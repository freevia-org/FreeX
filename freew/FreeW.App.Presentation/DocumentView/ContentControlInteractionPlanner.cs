using System.Globalization;
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

    public static ContentControlChromePlan BuildChromePlan(Run run)
    {
        var control = run.Control;
        return new ContentControlChromePlan(
            control is null ? string.Empty : Tooltip(control),
            control is null ? run.Text : DisplayText(run),
            control is not null && IsInteractive(control),
            control is null ? [] : Choices(control));
    }

    public static bool CanEditExistingContentControl(
        Run run,
        RestrictEditingEnforcementPolicy protectionPolicy) =>
        run.Control is not null
        && run.Control.LockMode is not (ContentControlLockMode.ContentLocked or ContentControlLockMode.ControlAndContentLocked)
        && protectionPolicy.DecisionFor(RestrictEditingOperationKind.FormFieldEdit).IsAllowed;

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

    public static Run? ToggleCheckBox(Run run)
    {
        if (run.Control is not { Kind: ContentControlKind.CheckBox } control)
            return null;

        var updated = control with { Checked = !control.Checked };
        return CloneWith(run, CheckBoxGlyph(updated.Checked), updated);
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
