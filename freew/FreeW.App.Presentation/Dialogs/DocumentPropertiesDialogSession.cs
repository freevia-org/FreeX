using System.Globalization;
using Free.Shared.Opc;

namespace FreeW.App.Presentation.Dialogs;

public enum DocumentPropertiesDialogField
{
    Title,
    Author,
    Subject,
    Category,
    Keywords,
    Comments,
    ContentStatus,
    Language,
    Version,
    LastModifiedBy,
    Created,
    Modified,
}

public sealed record DocumentPropertiesDialogFieldSpec(
    DocumentPropertiesDialogField Field,
    string Label,
    string AutomationId,
    string Value,
    bool IsEditable,
    bool IsMultiline = false);

public sealed record DocumentPropertiesDialogSurfaceSpec(
    string Title,
    IReadOnlyList<DocumentPropertiesDialogFieldSpec> Fields);

public sealed record DocumentPropertiesDialogInput(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Comments,
    string? Category,
    string? ContentStatus,
    string? Language,
    string? Version);

public sealed record DocumentPropertiesDialogCommitPlan(
    bool ShouldExecuteCommand,
    bool ShouldMarkDirty,
    DocumentPropertiesDialogValues? Values);

/// <summary>
/// Owns the shared field catalog, initial metadata projection, normalization, and commit decision for
/// the paired FreeW document-properties dialogs.
/// </summary>
public sealed class DocumentPropertiesDialogSession
{
    public DocumentPropertiesDialogSession(DocumentProperties properties, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(culture);

        Surface = new DocumentPropertiesDialogSurfaceSpec(
            "Document Properties",
            [
                Editable(DocumentPropertiesDialogField.Title, "Title:", "DocumentPropertiesTitle", properties.Title),
                Editable(DocumentPropertiesDialogField.Author, "Author:", "DocumentPropertiesAuthor", properties.Author),
                Editable(DocumentPropertiesDialogField.Subject, "Subject:", "DocumentPropertiesSubject", properties.Subject),
                Editable(DocumentPropertiesDialogField.Category, "Category:", "DocumentPropertiesCategory", properties.Category),
                Editable(DocumentPropertiesDialogField.Keywords, "Keywords:", "DocumentPropertiesKeywords", properties.Keywords),
                Editable(DocumentPropertiesDialogField.Comments, "Comments:", "DocumentPropertiesComments", properties.Comments, isMultiline: true),
                Editable(DocumentPropertiesDialogField.ContentStatus, "Status:", "DocumentPropertiesContentStatus", properties.ContentStatus),
                Editable(DocumentPropertiesDialogField.Language, "Language:", "DocumentPropertiesLanguage", properties.Language),
                Editable(DocumentPropertiesDialogField.Version, "Version:", "DocumentPropertiesVersion", properties.Version),
                ReadOnly(DocumentPropertiesDialogField.LastModifiedBy, "Last saved by:", "DocumentPropertiesLastModifiedBy", properties.LastModifiedBy),
                ReadOnly(DocumentPropertiesDialogField.Created, "Created:", "DocumentPropertiesCreated", FormatDate(properties.Created, culture)),
                ReadOnly(DocumentPropertiesDialogField.Modified, "Modified:", "DocumentPropertiesModified", FormatDate(properties.Modified, culture)),
            ]);
    }

    public DocumentPropertiesDialogSurfaceSpec Surface { get; }

    public DocumentPropertiesDialogCommitPlan PlanCommit(bool accepted, DocumentPropertiesDialogInput? input)
    {
        if (!accepted)
            return new DocumentPropertiesDialogCommitPlan(false, false, null);

        ArgumentNullException.ThrowIfNull(input);
        var values = DocumentPropertiesDialogValues.FromInput(
            input.Title,
            input.Author,
            input.Subject,
            input.Keywords,
            input.Comments,
            input.Category,
            input.ContentStatus,
            input.Language,
            input.Version);
        return new DocumentPropertiesDialogCommitPlan(true, true, values);
    }

    private static DocumentPropertiesDialogFieldSpec Editable(
        DocumentPropertiesDialogField field,
        string label,
        string automationId,
        string? value,
        bool isMultiline = false) =>
        new(field, label, automationId, value ?? string.Empty, IsEditable: true, isMultiline);

    private static DocumentPropertiesDialogFieldSpec ReadOnly(
        DocumentPropertiesDialogField field,
        string label,
        string automationId,
        string? value) =>
        new(
            field,
            label,
            automationId,
            string.IsNullOrWhiteSpace(value) ? "-" : value,
            IsEditable: false);

    private static string? FormatDate(DateTimeOffset? value, CultureInfo culture) =>
        value?.ToLocalTime().ToString("g", culture);
}
