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

        Restore(context.Document, _snapshot);
    }

    private static Dictionary<string, DocumentStyle> CloneCatalog(IReadOnlyDictionary<string, DocumentStyle> styles) =>
        styles.ToDictionary(kv => kv.Key, kv => CloneStyle(kv.Value), StringComparer.Ordinal);

    // The copy constructor carries every property, so undoing a catalog edit restores the default-style
    // flag, the w:link pairing, the gallery flag and preserved table-style XML along with the formatting.
    private static DocumentStyle CloneStyle(DocumentStyle style) => new(style);

    internal static Dictionary<string, DocumentStyle> Snapshot(IReadOnlyDictionary<string, DocumentStyle> styles) =>
        CloneCatalog(styles);

    internal static void Restore(TextDocument document, IReadOnlyDictionary<string, DocumentStyle> snapshot)
    {
        document.Styles.Clear();
        foreach (var (id, style) in snapshot)
            document.Styles[id] = CloneStyle(style);
    }
}

/// <summary>
/// Word's Delete Style as one reversible step: removes a custom style through
/// <see cref="StyleManager.DeleteStyleAndReassign"/>, which also returns the paragraphs and runs that
/// used it to the default, and on undo restores both the catalog and every style reference it cleared.
/// The references are captured when the command is applied (never when it is planned), by their position
/// in <see cref="StyleManager.EnumerateStyledParagraphs"/>, so undo is exact even if the paragraph objects
/// were recreated by an intervening edit that has since been undone.
/// </summary>
public sealed class DeleteStyleCommand(string styleId) : IDocumentCommand
{
    private Dictionary<string, DocumentStyle>? _catalog;
    private List<(string? ParagraphStyleId, string?[] RunStyleIds)>? _references;

    public string Label => "Delete Style";

    public bool HasEffect(IDocumentCommandContext context) =>
        styleId is not null
        && !StyleManager.IsBuiltIn(styleId)
        && context.Document.Styles.ContainsKey(styleId);

    public void Apply(IDocumentCommandContext context)
    {
        if (!HasEffect(context))
        {
            _catalog = null;
            _references = null;
            return;
        }

        _catalog = StyleCatalogCommand.Snapshot(context.Document.Styles);
        _references = StyleManager.EnumerateStyledParagraphs(context.Document)
            .Select(paragraph => (paragraph.StyleId, paragraph.Runs.Select(run => run.StyleId).ToArray()))
            .ToList();
        StyleManager.DeleteStyleAndReassign(context.Document, styleId);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_catalog is null || _references is null)
            return;

        StyleCatalogCommand.Restore(context.Document, _catalog);
        var index = 0;
        foreach (var paragraph in StyleManager.EnumerateStyledParagraphs(context.Document))
        {
            if (index >= _references.Count)
                break;
            var (paragraphStyleId, runStyleIds) = _references[index++];
            paragraph.StyleId = paragraphStyleId;
            for (var run = 0; run < paragraph.Runs.Count && run < runStyleIds.Length; run++)
                paragraph.Runs[run].StyleId = runStyleIds[run];
        }
    }
}
