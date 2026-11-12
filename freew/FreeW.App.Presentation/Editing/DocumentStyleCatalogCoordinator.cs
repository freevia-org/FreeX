using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Applies the results of Word's Create New Style / Modify Style / Manage Styles surfaces to the
/// document's style catalog through the shared command history, so every change is one undo step and
/// both renderers get identical semantics. Catalog rules live in <see cref="StyleManager"/>; the undo
/// state is captured when the command is applied, never when it is planned.
/// </summary>
public sealed class DocumentStyleCatalogCoordinator
{
    private readonly DocumentEditingSession _session;

    internal DocumentStyleCatalogCoordinator(DocumentEditingSession session)
    {
        _session = session;
    }

    private TextDocument Document => _session.Document;

    /// <summary>
    /// Creates the style described by <paramref name="definition"/>. A paragraph or linked style is also
    /// applied to <paramref name="paragraphIndices"/> in the same undo step (Word applies a new style to
    /// the current paragraph). A character style is only added to the catalog: applying it to selected
    /// text is the renderer's ordinary named-style path, because that needs its selection mapping.
    /// </summary>
    public DocumentStyle? CreateStyle(StyleDefinition definition, IReadOnlyList<int> paragraphIndices)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(paragraphIndices);
        if (string.IsNullOrWhiteSpace(definition.Name))
            return null;

        var targets = definition.Kind == StyleDefinitionKind.Character
            ? []
            : paragraphIndices
                .Distinct()
                .Where(index => index >= 0
                    && index < Document.Blocks.Count
                    && Document.Blocks[index] is Paragraph)
                .ToArray();

        DocumentStyle? created = null;
        var commands = _session.Commands;
        commands.BeginUndoGroup();
        try
        {
            commands.Execute(new StyleCatalogCommand("New Style", document =>
            {
                created = definition.Kind switch
                {
                    StyleDefinitionKind.Character => StyleManager.CreateStyle(
                        document,
                        definition.Name,
                        definition.BasedOnId,
                        definition.Run,
                        ParagraphFormatting.Default,
                        nextStyleId: null,
                        StyleType.Character,
                        definition.AddToGallery),
                    StyleDefinitionKind.Linked => StyleManager.CreateLinkedStyle(
                        document,
                        definition.Name,
                        definition.BasedOnId,
                        definition.Run,
                        definition.Paragraph,
                        definition.NextStyleId,
                        definition.AddToGallery),
                    _ => StyleManager.CreateStyle(
                        document,
                        definition.Name,
                        definition.BasedOnId,
                        definition.Run,
                        definition.Paragraph,
                        definition.NextStyleId,
                        StyleType.Paragraph,
                        definition.AddToGallery),
                };
            }));
            if (created is not null)
            {
                foreach (var index in targets)
                    commands.Execute(new SetParagraphStyleCommand(index, created.Id));
            }
            commands.CommitUndoGroup("New Style");
        }
        catch
        {
            // Unlike AbortUndoGroup, this reverts what was already applied, so a failure cannot leave a
            // half-created style behind with no history entry to undo it.
            if (commands.IsUndoGroupOpen)
                commands.RollbackUndoGroup();
            throw;
        }

        return created;
    }

    /// <summary>
    /// Applies a Modify Style result: name (custom styles only), based-on, following style, formatting
    /// and the gallery flag, keeping the character half of a linked pair in step. A result that changes
    /// nothing pushes no undo entry.
    /// </summary>
    public DocumentStyle? ModifyStyle(string styleId, StyleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(styleId) || !Document.Styles.ContainsKey(styleId))
            return null;

        return ExecuteModification(styleId, "Modify Style", document =>
        {
            var existing = document.Styles[styleId];
            StyleManager.ModifyStyle(
                document,
                styleId,
                run: definition.Run,
                para: existing.Type == StyleType.Character ? existing.Paragraph : definition.Paragraph,
                name: StyleManager.IsBuiltIn(styleId) ? null : definition.Name,
                basedOnId: definition.BasedOnId,
                clearBasedOn: definition.BasedOnId is null,
                nextStyleId: definition.NextStyleId,
                clearNext: definition.NextStyleId is null,
                quickFormat: definition.AddToGallery);
            StyleManager.SyncLinkedCharacterStyle(document, styleId);
        });
    }

    /// <summary>Renames a custom style; a built-in style keeps its name.</summary>
    public DocumentStyle? RenameStyle(string styleId, string name)
    {
        if (string.IsNullOrWhiteSpace(styleId)
            || string.IsNullOrWhiteSpace(name)
            || StyleManager.IsBuiltIn(styleId)
            || !Document.Styles.ContainsKey(styleId))
        {
            return null;
        }

        return ExecuteModification(styleId, "Rename Style", document =>
        {
            StyleManager.ModifyStyle(document, styleId, name: name);
            StyleManager.SyncLinkedCharacterStyle(document, styleId);
        });
    }

    /// <summary>
    /// Word's "Update &lt;style&gt; to Match Selection": redefines the style from the formatting in
    /// effect at the selection. A character style takes only the run formatting.
    /// </summary>
    public DocumentStyle? UpdateStyleToMatch(string styleId, RunFormatting run, ParagraphFormatting paragraph)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(paragraph);
        if (string.IsNullOrWhiteSpace(styleId) || !Document.Styles.ContainsKey(styleId))
            return null;

        return ExecuteModification(styleId, "Update Style to Match Selection", document =>
        {
            var existing = document.Styles[styleId];
            StyleManager.ModifyStyle(
                document,
                styleId,
                run: run,
                para: existing.Type == StyleType.Character ? existing.Paragraph : paragraph);
            StyleManager.SyncLinkedCharacterStyle(document, styleId);
        });
    }

    /// <summary>Word's "Add to / Remove from the Styles gallery" for one style, as an undoable edit.</summary>
    public DocumentStyle? SetGalleryVisibility(string styleId, bool showInGallery)
    {
        if (string.IsNullOrWhiteSpace(styleId) || !Document.Styles.ContainsKey(styleId))
            return null;

        return ExecuteModification(
            styleId,
            showInGallery ? "Add to Style Gallery" : "Remove from Style Gallery",
            document => StyleManager.ModifyStyle(document, styleId, quickFormat: showInGallery));
    }

    /// <summary>
    /// Word's Delete Style: removes a custom style and returns everything formatted with it to the
    /// default, as one undo step. Built-in styles are refused.
    /// </summary>
    public bool DeleteStyle(string styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId)
            || StyleManager.IsBuiltIn(styleId)
            || !Document.Styles.ContainsKey(styleId))
        {
            return false;
        }

        _session.Commands.Execute(new DeleteStyleCommand(styleId));
        return !Document.Styles.ContainsKey(styleId);
    }

    private DocumentStyle? ExecuteModification(string styleId, string label, Action<TextDocument> mutate)
    {
        // Dry-run against a scratch catalog first (StyleManager only ever replaces entries, so sharing
        // the DocumentStyle instances is safe): an OK click that changes nothing must not push a no-op
        // undo entry or clear the redo history.
        var scratch = TextDocument.CreateEmpty();
        scratch.Styles.Clear();
        foreach (var (id, style) in Document.Styles)
            scratch.Styles[id] = style;
        mutate(scratch);
        if (CatalogsMatch(Document.Styles, scratch.Styles))
            return Document.Styles[styleId];

        _session.Commands.Execute(new StyleCatalogCommand(label, mutate));
        return Document.Styles.TryGetValue(styleId, out var updated) ? updated : null;
    }

    private static bool CatalogsMatch(
        IReadOnlyDictionary<string, DocumentStyle> current,
        IReadOnlyDictionary<string, DocumentStyle> candidate)
    {
        if (current.Count != candidate.Count)
            return false;

        foreach (var (id, style) in current)
        {
            if (!candidate.TryGetValue(id, out var other))
                return false;
            if (ReferenceEquals(style, other))
                continue;
            if (!string.Equals(style.Name, other.Name, StringComparison.Ordinal)
                || !string.Equals(style.BasedOnStyleId, other.BasedOnStyleId, StringComparison.Ordinal)
                || !string.Equals(style.NextStyleId, other.NextStyleId, StringComparison.Ordinal)
                || !string.Equals(style.LinkedStyleId, other.LinkedStyleId, StringComparison.Ordinal)
                || style.QuickFormat != other.QuickFormat
                || style.Type != other.Type
                || style.Run != other.Run
                || style.Paragraph != other.Paragraph)
            {
                return false;
            }
        }

        return true;
    }
}
