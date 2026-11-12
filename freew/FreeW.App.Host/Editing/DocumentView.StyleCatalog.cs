using System;
using System.Linq;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Home &gt; Styles catalog editing for the WPF editor: thin entry points that commit pending native
/// edits, capture the selection, and hand the work to the shared
/// <see cref="FreeW.App.Presentation.Editing.DocumentStyleCatalogCoordinator"/> (which owns the rules
/// and the undo history).
/// </summary>
public sealed partial class DocumentView
{
    /// <summary>
    /// Raised after any model edit, undo or redo. The Styles gallery listens so its swatches follow the
    /// style catalog (new, renamed, removed or re-flagged styles) without being rebuilt by hand.
    /// </summary>
    public event Action? EditingSessionChanged
    {
        add => _editingSession.Changed += value;
        remove => _editingSession.Changed -= value;
    }

    /// <summary>
    /// Create New Style: adds the style and applies it the way Word does — a paragraph or linked style
    /// to the selected paragraphs (one undo step with the creation), a character style to the selected
    /// text through the ordinary named-style path.
    /// </summary>
    public DocumentStyle? CreateStyleAndApply(StyleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // Capture the selection first: creating the style re-renders the document, which collapses it.
        var selectedRange = SelectedVisibleTextRange();
        var paragraphTargets = SelectedModelParagraphIndices();
        var hasTextSelection = !Selection.IsEmpty;
        CommitToModel();
        var created = _editingSession.Styles.CreateStyle(definition, paragraphTargets);
        if (created is { Type: StyleType.Character } && hasTextSelection)
        {
            _editingSession.ApplyNamedStyle(
                created.Id,
                CreateNamedStyleApplicationTarget(selectedRange, paragraphTargets, hasTextSelection));
        }
        return created;
    }

    /// <summary>Modify Style: applies an edited definition (name, type-specific formatting, lineage, gallery flag).</summary>
    public DocumentStyle? ModifyStyle(string styleId, StyleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        CommitToModel();
        return _editingSession.Styles.ModifyStyle(styleId, definition);
    }

    /// <summary>
    /// "Update &lt;style&gt; to Match Selection": redefines the style from the formatting in effect at the
    /// selection — the character formatting WPF reports for it and the first selected paragraph's
    /// effective paragraph formatting. List membership stays on the paragraph: it is not style formatting.
    /// </summary>
    public DocumentStyle? UpdateStyleToMatchSelection(string styleId)
    {
        var run = CaptureSelectionRunFormatting();
        CommitToModel();
        var paragraph = SelectedModelParagraphs().FirstOrDefault() is { } selected
            ? DocumentParagraphFormattingResolver.Resolve(_model, selected) with
            {
                ListKind = ListKind.None,
                ListLevel = 0,
                ListStartOverride = null,
                ListMarkerText = null,
            }
            : ParagraphFormatting.Default;
        return _editingSession.Styles.UpdateStyleToMatch(styleId, run, paragraph);
    }

    /// <summary>"Remove from Style Gallery" / "Add to the Styles gallery" for one style.</summary>
    public DocumentStyle? SetStyleGalleryVisibility(string styleId, bool showInGallery)
    {
        CommitToModel();
        return _editingSession.Styles.SetGalleryVisibility(styleId, showInGallery);
    }
}
