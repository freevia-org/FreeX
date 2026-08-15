using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// UI Automation peer for <see cref="DocumentView"/>, FreeW-Avalonia's custom document-editing
/// surface.
///
/// <para>
/// <b>What the WPF twin gets, and why.</b> <c>FreeW.App.Host.Editing.DocumentView</c> derives from
/// WPF's <c>RichTextBox</c> (a <c>FlowDocument</c>-backed <c>TextBoxBase</c>) and contains no
/// automation code at all — <c>TextBoxBase.OnCreateAutomationPeer()</c> returns a
/// <c>System.Windows.Automation.Peers.TextAutomationPeer</c> for free, which implements the full
/// UIA <c>ITextProvider</c>/<c>ITextRangeProvider</c> contract: the document's text is exposed as
/// navigable <c>TextPatternRange</c>s (by character/word/line/paragraph/document), the caret and
/// selection are reported as a range with live "text selection changed" automation events, and
/// per-run formatting is queryable via <c>TextPatternRangeAttribute</c> on any sub-range.
/// </para>
///
/// <para>
/// <b>What Avalonia 12.0.4 actually offers (verified by reflecting the shipped
/// <c>Avalonia.Controls.dll</c>/<c>Avalonia.Base.dll</c>, not assumed).</b>
/// <c>Avalonia.Automation.Provider</c> defines exactly: <c>IExpandCollapseProvider</c>,
/// <c>IInvokeProvider</c>, <c>IRangeValueProvider</c>, <c>IScrollProvider</c>,
/// <c>ISelectionProvider</c>/<c>ISelectionItemProvider</c>, <c>IToggleProvider</c>, and
/// <c>IValueProvider</c>. There is <b>no</b> <c>ITextProvider</c>, <c>ITextRangeProvider</c>, or
/// <c>ICaretProvider</c> equivalent, and no automation event dedicated to "text selection changed".
/// Text-range navigation and true per-run/paragraph structural queries via UI Automation are
/// therefore not expressible on this platform version — that is a framework limit, not something
/// this peer works around.
/// </para>
///
/// <para>
/// <b>What this peer does instead — the closest available equivalents.</b>
/// <list type="bullet">
/// <item><description>
/// Reports <see cref="AutomationControlType.Document"/> (the same control type WPF's
/// <c>TextAutomationPeer</c> would report for a <c>RichTextBox</c>) and implements
/// <see cref="IValueProvider"/> — the only pattern Avalonia has for exposing bulk text content —
/// with <see cref="Value"/> returning the full document plain text
/// (<see cref="DocumentView.PlainText"/>, itself <c>TextDocument.PlainText</c>, which already
/// joins every paragraph/table cell's runs). This covers "document text exposure".
/// </description></item>
/// <item><description>
/// Reports caret and selection context through both ItemStatus and HelpText. The renderer only
/// translates its body/table caret addresses; the flattened text, global ranges, current word,
/// paragraph, and logical line come from the shared <c>AccessibleDocumentSnapshotPlanner</c>.
/// Reporting a collapsed caret matters because most arrow-key and pointer moves never select text.
/// </description></item>
/// <item><description>
/// Projects the shared <c>DocumentAccessibilityNodePlanner</c> tree as virtual automation children:
/// headings, paragraphs, table rows/cells, hyperlinks, pictures, shapes/text boxes, charts, WordArt,
/// SmartArt, drawing groups, and embedded objects. The shared layer owns roles, names, values, and
/// structural identity; this renderer supplies layout bounds, focus/selection, and invocation only.
/// Actual floating-object selection is exposed through Selection/SelectionItem patterns; text selection
/// deliberately is not, because Avalonia's SelectionPattern represents selected child elements, not ranges.
/// </description></item>
/// <item><description>
/// Raises change notifications: <see cref="NotifySelectionChanged"/> fires an
/// ItemStatus-changed automation event on every <see cref="DocumentView.CaretMoved"/> (which
/// already fires from every caret-move/click/selection/table-navigation call site in
/// DocumentView), and <see cref="NotifyValueChanged"/> fires a Value-changed automation event on
/// every <see cref="DocumentView.DocumentChanged"/> (raised on every committed edit, undo/redo,
/// and external load/mutation). Both are de-duplicated by DocumentView so no-op moves (e.g.
/// re-clicking the same position) don't spam assistive tech.
/// </description></item>
/// <item><description>
/// <see cref="IsReadOnly"/> is <see langword="true"/> and <see cref="SetValue"/> throws: automation
/// clients can read the document's text, but cannot replace it wholesale — edits must go through
/// <see cref="DocumentView"/>'s command bus (undo/redo, track-changes, etc. all depend on that),
/// which a raw <c>ValuePattern.SetValue</c> call would bypass.
/// </description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Explicitly NOT provided</b> (because Avalonia has no pattern for it): per-character/word/
/// visual-line/paragraph <c>TextPatternRange</c> navigation, run-level formatting attribute queries via
/// automation, and a dedicated caret-position/text-selection-changed automation event. A screen
/// reader driving FreeW-Avalonia gets the whole text plus live semantic caret/selection context,
/// rather than WPF's client-driven TextPattern navigation. Shared code can query the same snapshot
/// by character, word, logical line, paragraph, or document without toolkit dependencies.
/// </para>
/// </summary>
internal sealed class DocumentViewAutomationPeer : ControlAutomationPeer, IValueProvider, ISelectionProvider
{
    private readonly DocumentView _owner;
    private DocumentAccessibilityTree _semanticTree;
    private Dictionary<string, string?> _parentIds;
    private readonly Dictionary<string, DocumentVirtualAutomationPeer> _nodePeers = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _lastSelectedNodeIds = [];

    public DocumentViewAutomationPeer(DocumentView owner)
        : base(owner)
    {
        _owner = owner;
        _semanticTree = owner.AutomationSemanticTree();
        _parentIds = BuildParentMap(_semanticTree);
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(DocumentView);

    protected override string? GetNameCore() => "Document editor";

    protected override string? GetItemStatusCore() => _owner.AutomationSelectionStatus();

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
        _semanticTree.Children.Select(GetOrCreateNodePeer).Cast<AutomationPeer>().ToArray();

    // IValueProvider: the closest Avalonia equivalent to WPF ITextProvider's document-text exposure
    // (Avalonia 12.0.4 has no ITextProvider/ITextRangeProvider). Read-only: see class remarks.
    public bool IsReadOnly => true;

    public string? Value => _owner.PlainText;

    public void SetValue(string? value) =>
        throw new System.NotSupportedException(
            "DocumentView text is read-only via UI Automation; edits must go through the document's command bus, not raw text replacement.");

    public bool CanSelectMultiple => true;

    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        SelectedObjectNodes().Select(GetOrCreateNodePeer).Cast<AutomationPeer>().ToArray();

    /// <summary>Raises the automation Value-changed event. Called by <see cref="DocumentView"/> on every <see cref="DocumentView.DocumentChanged"/>.</summary>
    internal void NotifyValueChanged(string? oldValue, string? newValue) =>
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, oldValue, newValue);

    internal void NotifySemanticDocumentChanged()
    {
        var previous = _semanticTree;
        var current = _owner.AutomationSemanticTree();
        if (!HasSameStructure(previous, current))
        {
            _semanticTree = current;
            _parentIds = BuildParentMap(current);
            _nodePeers.Clear();
            InvalidateChildren();
            return;
        }

        _semanticTree = current;
        _parentIds = BuildParentMap(current);
        foreach (var (id, peer) in _nodePeers)
        {
            if (previous.ById.TryGetValue(id, out var oldNode) && current.ById.TryGetValue(id, out var newNode))
                peer.NotifyNodeChanged(oldNode, newNode);
        }
    }

    /// <summary>
    /// Raises the automation ItemStatus-changed event — the closest available substitute for a
    /// caret/selection-changed notification (Avalonia has no ICaretProvider or dedicated
    /// selection-changed automation event). Called by <see cref="DocumentView"/> on every
    /// <see cref="DocumentView.CaretMoved"/>.
    /// </summary>
    internal void NotifySelectionChanged(string? oldStatus, string? newStatus) =>
        NotifySelectionPropertiesChanged(oldStatus, newStatus);

    private void NotifySelectionPropertiesChanged(string? oldStatus, string? newStatus)
    {
        RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, oldStatus, newStatus);
        RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, oldStatus, newStatus);
    }

    internal DocumentAccessibilityNode? ResolveNode(string id) =>
        _semanticTree.ById.TryGetValue(id, out var node) ? node : null;

    internal IReadOnlyList<AutomationPeer> GetNodeChildren(string id) =>
        ResolveNode(id)?.SemanticChildren.Select(GetOrCreateNodePeer).Cast<AutomationPeer>().ToArray()
        ?? Array.Empty<AutomationPeer>();

    internal AutomationPeer GetNodeParent(string id)
    {
        if (!_parentIds.TryGetValue(id, out var parentId) || parentId is null)
            return this;
        return GetOrCreateNodePeer(_semanticTree.ById[parentId]);
    }

    internal Rect GetNodeBounds(DocumentAccessibilityNode node) => _owner.AutomationNodeBounds(node);

    internal void FocusNode(DocumentAccessibilityNode node) => _owner.AutomationFocusNode(node);

    internal bool NodeHasKeyboardFocus(DocumentAccessibilityNode node) =>
        _owner.AutomationNodeHasKeyboardFocus(node);

    internal void InvokeNode(DocumentAccessibilityNode node)
    {
        if (!_owner.AutomationInvokeNode(node))
            throw new InvalidOperationException("The semantic node is no longer an invokable hyperlink.");
    }

    internal bool IsNodeSelected(DocumentAccessibilityNode node) => _owner.AutomationNodeIsSelected(node);

    internal void SelectNode(DocumentAccessibilityNode node, bool addToSelection) =>
        _owner.AutomationSelectNode(node, addToSelection);

    internal void RemoveNodeFromSelection(DocumentAccessibilityNode node) =>
        _owner.AutomationRemoveNodeFromSelection(node);

    internal void NotifyObjectSelectionChanged()
    {
        var current = SelectedObjectNodes().Select(node => node.Id).ToArray();
        var previous = _lastSelectedNodeIds;
        _lastSelectedNodeIds = current;
        foreach (var removedId in previous.Except(current, StringComparer.Ordinal))
        {
            if (_nodePeers.TryGetValue(removedId, out var removedPeer))
                removedPeer.RaiseSelectionChanged(true, false);
        }
        foreach (var addedId in current.Except(previous, StringComparer.Ordinal))
        {
            var addedPeer = GetOrCreateNodePeer(_semanticTree.ById[addedId]);
            addedPeer.RaiseSelectionChanged(false, true);
        }
    }

    private IReadOnlyList<DocumentAccessibilityNode> SelectedObjectNodes() =>
        _semanticTree.ById.Values
            .Where(node => node.IsFloatingObject && IsNodeSelected(node))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

    private DocumentVirtualAutomationPeer GetOrCreateNodePeer(DocumentAccessibilityNode node)
    {
        if (_nodePeers.TryGetValue(node.Id, out var peer))
            return peer;
        peer = node.IsFloatingObject && IsDrawingObjectNode(node.Kind)
            ? new DocumentDrawingObjectAutomationPeer(this, node.Id)
            : node.Kind switch
        {
            DocumentAccessibilityNodeKind.HeaderFooterStory =>
                new DocumentValueAutomationPeer(this, node.Id),
            DocumentAccessibilityNodeKind.Footnote or DocumentAccessibilityNodeKind.Endnote =>
                new DocumentValueAutomationPeer(this, node.Id),
            DocumentAccessibilityNodeKind.ListItem =>
                new DocumentValueAutomationPeer(this, node.Id),
            DocumentAccessibilityNodeKind.TextRun =>
                new DocumentValueAutomationPeer(this, node.Id),
            DocumentAccessibilityNodeKind.Paragraph or DocumentAccessibilityNodeKind.Heading =>
                new DocumentValueAutomationPeer(this, node.Id),
            DocumentAccessibilityNodeKind.TableCell => new DocumentValueAutomationPeer(this, node.Id),
            DocumentAccessibilityNodeKind.Hyperlink => new DocumentHyperlinkAutomationPeer(this, node.Id),
            DocumentAccessibilityNodeKind.Shape
                or DocumentAccessibilityNodeKind.Chart
                or DocumentAccessibilityNodeKind.WordArt
                or DocumentAccessibilityNodeKind.SmartArt
                or DocumentAccessibilityNodeKind.EmbeddedObject =>
                new DocumentValueAutomationPeer(this, node.Id),
            _ => new DocumentStructuralAutomationPeer(this, node.Id)
        };
        _nodePeers[node.Id] = peer;
        return peer;
    }

    private static bool IsDrawingObjectNode(DocumentAccessibilityNodeKind kind) =>
        kind is DocumentAccessibilityNodeKind.Image
            or DocumentAccessibilityNodeKind.Shape
            or DocumentAccessibilityNodeKind.Chart
            or DocumentAccessibilityNodeKind.WordArt
            or DocumentAccessibilityNodeKind.SmartArt
            or DocumentAccessibilityNodeKind.DrawingGroup
            or DocumentAccessibilityNodeKind.EmbeddedObject;

    private static Dictionary<string, string?> BuildParentMap(DocumentAccessibilityTree tree)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        void Visit(IEnumerable<DocumentAccessibilityNode> nodes, string? parentId)
        {
            foreach (var node in nodes)
            {
                result[node.Id] = parentId;
                Visit(node.SemanticChildren, node.Id);
            }
        }
        Visit(tree.Children, null);
        return result;
    }

    private static bool HasSameStructure(DocumentAccessibilityTree left, DocumentAccessibilityTree right)
    {
        static IEnumerable<string> Signature(DocumentAccessibilityTree tree)
        {
            yield return "root=" + string.Join('|', tree.Children.Select(node => node.Id));
            foreach (var node in tree.ById.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
                yield return node.Id + "=" + string.Join('|', node.SemanticChildren.Select(child => child.Id));
        }
        return Signature(left).SequenceEqual(Signature(right), StringComparer.Ordinal);
    }
}

internal abstract class DocumentVirtualAutomationPeer(
    DocumentViewAutomationPeer root,
    string nodeId) : AutomationPeer
{
    protected DocumentAccessibilityNode Node =>
        root.ResolveNode(nodeId) ?? throw new InvalidOperationException("The document accessibility node is stale.");

    protected override string GetNameCore() => Node.Name;

    protected override AutomationControlType GetAutomationControlTypeCore() => Node.Kind switch
    {
        DocumentAccessibilityNodeKind.HeaderFooterStory => AutomationControlType.Group,
        DocumentAccessibilityNodeKind.Footnotes or DocumentAccessibilityNodeKind.Endnotes => AutomationControlType.Group,
        DocumentAccessibilityNodeKind.Footnote or DocumentAccessibilityNodeKind.Endnote => AutomationControlType.Text,
        DocumentAccessibilityNodeKind.List => AutomationControlType.List,
        DocumentAccessibilityNodeKind.ListItem => AutomationControlType.ListItem,
        DocumentAccessibilityNodeKind.TextRun => AutomationControlType.Text,
        DocumentAccessibilityNodeKind.Paragraph or DocumentAccessibilityNodeKind.Heading => AutomationControlType.Text,
        DocumentAccessibilityNodeKind.Table => AutomationControlType.DataGrid,
        DocumentAccessibilityNodeKind.TableRow => AutomationControlType.Group,
        DocumentAccessibilityNodeKind.TableCell => AutomationControlType.DataItem,
        DocumentAccessibilityNodeKind.Hyperlink => AutomationControlType.Hyperlink,
        DocumentAccessibilityNodeKind.Image => AutomationControlType.Image,
        DocumentAccessibilityNodeKind.Shape
            or DocumentAccessibilityNodeKind.Chart
            or DocumentAccessibilityNodeKind.WordArt
            or DocumentAccessibilityNodeKind.SmartArt
            or DocumentAccessibilityNodeKind.EmbeddedObject => AutomationControlType.Image,
        DocumentAccessibilityNodeKind.DrawingGroup => AutomationControlType.Group,
        _ => AutomationControlType.Custom
    };

    protected override string GetClassNameCore() => "Document" + Node.Kind;

    protected override string GetAutomationIdCore() => Node.Id;

    protected override Rect GetBoundingRectangleCore() => root.GetNodeBounds(Node);

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => root.GetNodeChildren(nodeId);

    protected override AutomationPeer? GetParentCore() => root.GetNodeParent(nodeId);

    protected override bool TrySetParent(AutomationPeer? newParent) => false;

    protected override void BringIntoViewCore() => root.FocusNode(Node);

    protected override void SetFocusCore() => root.FocusNode(Node);

    protected override bool ShowContextMenuCore() => false;

    protected override string GetAcceleratorKeyCore() => string.Empty;

    protected override string GetAccessKeyCore() => string.Empty;

    protected override string GetHelpTextCore() => Node.HelpText ?? string.Empty;

    protected override string GetItemStatusCore() => ItemStatus(Node);

    protected override string GetItemTypeCore() => Node.Kind.ToString();

    protected override AutomationPeer? GetLabeledByCore() => null;

    protected override bool HasKeyboardFocusCore() => root.NodeHasKeyboardFocus(Node);

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => true;

    protected override bool IsKeyboardFocusableCore() =>
        Node.Kind is DocumentAccessibilityNodeKind.Paragraph
            or DocumentAccessibilityNodeKind.Heading
            or DocumentAccessibilityNodeKind.ListItem
            or DocumentAccessibilityNodeKind.TextRun
            or DocumentAccessibilityNodeKind.TableCell
            or DocumentAccessibilityNodeKind.Hyperlink
            or DocumentAccessibilityNodeKind.Image
            or DocumentAccessibilityNodeKind.Shape
            or DocumentAccessibilityNodeKind.Chart
            or DocumentAccessibilityNodeKind.WordArt
            or DocumentAccessibilityNodeKind.SmartArt
            or DocumentAccessibilityNodeKind.DrawingGroup
            or DocumentAccessibilityNodeKind.EmbeddedObject;

    protected override bool IsOffscreenCore()
    {
        var bounds = GetBoundingRectangleCore();
        return bounds.Width <= 0 || bounds.Height <= 0;
    }

    internal virtual void NotifyNodeChanged(DocumentAccessibilityNode oldNode, DocumentAccessibilityNode newNode)
    {
        if (!string.Equals(oldNode.Name, newNode.Name, StringComparison.Ordinal))
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, oldNode.Name, newNode.Name);
        if (!string.Equals(oldNode.HelpText, newNode.HelpText, StringComparison.Ordinal))
            RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, oldNode.HelpText, newNode.HelpText);
        var oldStatus = ItemStatus(oldNode);
        var newStatus = ItemStatus(newNode);
        if (!string.Equals(oldStatus, newStatus, StringComparison.Ordinal))
            RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, oldStatus, newStatus);
    }

    internal void RaiseSelectionChanged(bool oldValue, bool newValue) =>
        RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, oldValue, newValue);

    private static string ItemStatus(DocumentAccessibilityNode node) => node.Kind switch
    {
        DocumentAccessibilityNodeKind.Heading => $"Heading level {node.HeadingLevel}",
        DocumentAccessibilityNodeKind.ListItem =>
            $"List item level {node.ListLevel + 1}{(string.IsNullOrWhiteSpace(node.ListMarker) ? string.Empty : $", marker {node.ListMarker}")}",
        DocumentAccessibilityNodeKind.TextRun => node.HelpText ?? "Character formatting",
        DocumentAccessibilityNodeKind.Footnote => node.HelpText ?? "Footnote",
        DocumentAccessibilityNodeKind.Endnote => node.HelpText ?? "Endnote",
        DocumentAccessibilityNodeKind.TableCell =>
            $"Row {node.RowIndex + 1}, column {node.ColumnIndex + 1}, row span {node.RowSpan}, column span {node.ColumnSpan}",
        _ => string.Empty
    };
}

internal sealed class DocumentStructuralAutomationPeer(
    DocumentViewAutomationPeer root,
    string nodeId) : DocumentVirtualAutomationPeer(root, nodeId);

internal class DocumentValueAutomationPeer(
    DocumentViewAutomationPeer root,
    string nodeId) : DocumentVirtualAutomationPeer(root, nodeId), IValueProvider
{
    public bool IsReadOnly => true;

    public string? Value => Node.Value;

    public void SetValue(string? value) =>
        throw new NotSupportedException("Document semantic text is read-only through UI Automation.");

    internal override void NotifyNodeChanged(DocumentAccessibilityNode oldNode, DocumentAccessibilityNode newNode)
    {
        base.NotifyNodeChanged(oldNode, newNode);
        if (!string.Equals(oldNode.Value, newNode.Value, StringComparison.Ordinal))
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, oldNode.Value, newNode.Value);
    }
}

internal sealed class DocumentDrawingObjectAutomationPeer : DocumentValueAutomationPeer, ISelectionItemProvider
{
    private readonly DocumentViewAutomationPeer _root;

    public DocumentDrawingObjectAutomationPeer(DocumentViewAutomationPeer root, string nodeId)
        : base(root, nodeId)
    {
        _root = root;
    }

    public bool IsSelected => _root.IsNodeSelected(Node);

    public ISelectionProvider SelectionContainer => _root;

    public void Select() => _root.SelectNode(Node, addToSelection: false);

    public void AddToSelection() => _root.SelectNode(Node, addToSelection: true);

    public void RemoveFromSelection() => _root.RemoveNodeFromSelection(Node);
}

internal sealed class DocumentHyperlinkAutomationPeer : DocumentValueAutomationPeer, IInvokeProvider
{
    private readonly DocumentViewAutomationPeer _root;

    public DocumentHyperlinkAutomationPeer(DocumentViewAutomationPeer root, string nodeId)
        : base(root, nodeId)
    {
        _root = root;
    }

    public void Invoke() => _root.InvokeNode(Node);
}
