using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>The editable core-property fields exposed by the Document Properties dialog.</summary>
public sealed record DocumentPropertiesDialogValues(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Comments,
    string? Category,
    string? ContentStatus,
    string? Language,
    string? Version)
{
    public static DocumentPropertiesDialogValues FromInput(
        string? title,
        string? author,
        string? subject,
        string? keywords,
        string? comments,
        string? category,
        string? contentStatus,
        string? language,
        string? version) =>
        new(
            Normalize(title),
            Normalize(author),
            Normalize(subject),
            Normalize(keywords),
            Normalize(comments),
            Normalize(category),
            Normalize(contentStatus),
            Normalize(language),
            Normalize(version));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Applies the editable core properties as one undoable document operation.</summary>
public sealed class ApplyDocumentPropertiesCommand(DocumentPropertiesDialogValues values) : IDocumentCommand
{
    private DocumentPropertiesDialogValues? _previous;

    public string Label => "Document Properties";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.Mixed;

    public int EstimatedBytes => 256
        + StringBytes(values.Title)
        + StringBytes(values.Author)
        + StringBytes(values.Subject)
        + StringBytes(values.Keywords)
        + StringBytes(values.Comments)
        + StringBytes(values.Category)
        + StringBytes(values.ContentStatus)
        + StringBytes(values.Language)
        + StringBytes(values.Version);

    public void Apply(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(values);
        _previous = Capture(context);
        Restore(context, values);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;

        Restore(context, _previous);
        _previous = null;
    }

    private static DocumentPropertiesDialogValues Capture(IDocumentCommandContext context)
    {
        var properties = context.Document.Properties;
        return new DocumentPropertiesDialogValues(
            properties.Title,
            properties.Author,
            properties.Subject,
            properties.Keywords,
            properties.Comments,
            properties.Category,
            properties.ContentStatus,
            properties.Language,
            properties.Version);
    }

    private static void Restore(IDocumentCommandContext context, DocumentPropertiesDialogValues snapshot)
    {
        var properties = context.Document.Properties;
        properties.Title = snapshot.Title;
        properties.Author = snapshot.Author;
        properties.Subject = snapshot.Subject;
        properties.Keywords = snapshot.Keywords;
        properties.Comments = snapshot.Comments;
        properties.Category = snapshot.Category;
        properties.ContentStatus = snapshot.ContentStatus;
        properties.Language = snapshot.Language;
        properties.Version = snapshot.Version;
    }

    private static int StringBytes(string? value) => (value?.Length ?? 0) * sizeof(char);
}
