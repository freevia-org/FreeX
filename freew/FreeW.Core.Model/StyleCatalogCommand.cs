namespace FreeW.Core.Model;

/// <summary>
/// Reversible mutation of the document's style catalog. Used by New Style / Manage Styles so custom
/// style create, modify, and delete participate in the same undo/redo bus as paragraph style apply.
/// </summary>
public sealed class StyleCatalogCommand(string label, Action<TextDocument> apply) : IDocumentCommand
{
    public string Label => label;

    private Dictionary<string, DocumentStyle>? _snapshot;

    public void Apply(IDocumentCommandContext context)
    {
        if (_snapshot is null)
            _snapshot = CloneCatalog(context.Document.Styles);

        apply(context.Document);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_snapshot is null)
            return;

        context.Document.Styles.Clear();
        foreach (var (id, style) in _snapshot)
            context.Document.Styles[id] = CloneStyle(style);
    }

    private static Dictionary<string, DocumentStyle> CloneCatalog(IReadOnlyDictionary<string, DocumentStyle> styles) =>
        styles.ToDictionary(kv => kv.Key, kv => CloneStyle(kv.Value), StringComparer.Ordinal);

    private static DocumentStyle CloneStyle(DocumentStyle style) => new()
    {
        Id = style.Id,
        Name = style.Name,
        Type = style.Type,
        BasedOnStyleId = style.BasedOnStyleId,
        NextStyleId = style.NextStyleId,
        OutlineLevel = style.OutlineLevel,
        Run = style.Run,
        Paragraph = style.Paragraph,
        TableBorders = style.TableBorders,
        PreservedNumbering = style.PreservedNumbering,
    };
}
