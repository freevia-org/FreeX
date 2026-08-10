using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private enum CommentPreviewActivation
    {
        None,
        Hover,
        Selection
    }

    private enum CommentPopupMode
    {
        None,
        Preview,
        NoteEditor,
        ThreadedCommentEditor
    }

    private readonly record struct CommentPreviewKey(
        uint Row,
        uint Col,
        CommentPreviewActivation Activation,
        CellCommentDisplayKind Kind,
        string Title,
        string Body,
        bool IsResolved);

    private const double CommentEditorWidth = 300;
    private const double CommentEditorDesiredHeight = 230;
    private const double CommentEditorExistingDesiredHeight = 300;

    private Border? _commentPreviewBorder;
    private StackPanel? _commentPreviewPanel;
    private ScrollViewer? _commentPreviewScrollViewer;
    private TextBlock? _commentInlineErrorBlock;
    private CommentPreviewKey? _activeCommentPreviewKey;
    private CommentPopupMode _commentPopupMode;
    private uint _activeCommentPopupRow;
    private uint _activeCommentPopupCol;
    private CellAddress? _activeNoteEditAddress;
    private CellAddress? _activeThreadedEditAddress;
    private string _activeCommentCellReference = "";
    private TextBox? _noteEditBox;
    private TextBox? _threadedRootBox;
    private TextBox? _threadedReplyBox;
    private TextBox? _threadedSelectedReplyBox;
    private ComboBox? _threadedReplySelector;
    private Button? _threadedUpdateReplyButton;
    private Button? _threadedDeleteReplyButton;
    private CheckBox? _threadedResolveBox;
    private ThreadedComment? _threadedEditExisting;

    // Pinned ("Show Comment") boxes — keyed by (Row, Col), always-visible in CommentOverlayHost.
    private readonly Dictionary<(uint Row, uint Col), Border> _pinnedNoteBorders = [];

    // Leader line bridging each pinned box back to its cell corner (Excel's pinned-note connector).
    private readonly Dictionary<(uint Row, uint Col), Line> _pinnedNoteConnectors = [];

    public void HideCommentPreview() => DismissCommentPreview();

    public bool BeginNoteInlineEdit(CellAddress address, string cellReference, string initialText)
    {
        if (!TryGetCellRect(Viewport, address.Row, address.Col, out var cellRect))
            return false;

        _activeNoteEditAddress = address;
        _activeThreadedEditAddress = null;
        _activeCommentCellReference = cellReference;
        _activeCommentPreviewKey = null;
        _commentPopupMode = CommentPopupMode.NoteEditor;
        _activeCommentPopupRow = address.Row;
        _activeCommentPopupCol = address.Col;

        BuildNoteInlineEditor(initialText);
        ShowCommentPopup(cellRect, new Size(CommentEditorWidth, CommentEditorDesiredHeight));
        FocusCommentTextBox(_noteEditBox);
        return true;
    }

    public bool BeginThreadedCommentInlineEdit(CellAddress address, string cellReference, ThreadedComment? existing)
    {
        if (!TryGetCellRect(Viewport, address.Row, address.Col, out var cellRect))
            return false;

        _activeNoteEditAddress = null;
        _activeThreadedEditAddress = address;
        _activeCommentCellReference = cellReference;
        _activeCommentPreviewKey = null;
        _commentPopupMode = CommentPopupMode.ThreadedCommentEditor;
        _activeCommentPopupRow = address.Row;
        _activeCommentPopupCol = address.Col;
        _threadedEditExisting = existing;

        BuildThreadedCommentInlineEditor(existing);
        ShowCommentPopup(
            cellRect,
            new Size(
                CommentEditorWidth,
                existing is null ? CommentEditorDesiredHeight : CommentEditorExistingDesiredHeight));
        FocusCommentTextBox(existing is null ? _threadedRootBox : _threadedReplyBox ?? _threadedRootBox);
        return true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshActiveCommentPopupPlacement();
    }

    private void UpdateCommentPreviewForPointer(Point pos)
    {
        if (IsInlineCommentEditorOpen())
            return;

        if (TryGetCommentPreviewAt(pos, out var cell, out var rect) &&
            !IsPinnedNoteAddress(cell.Row, cell.Col))
        {
            ShowCommentPreview(cell, rect, CommentPreviewActivation.Hover);
            return;
        }

        RestoreSelectedCommentPreview();
    }

    // A cell whose note is pinned always-visible (Excel's "Show Comment"/"Always Show") already
    // renders via RefreshPinnedNoteBoxes's independent _pinnedNoteBorders overlay, so the transient
    // hover-preview border must not also be raised for it -- otherwise the two boxes overlap.
    private bool IsPinnedNoteAddress(uint row, uint col) =>
        PinnedNoteAddresses is { } pinned && pinned.Contains((row, col));

    private void UpdateCommentPreviewForSelection()
    {
        if (IsInlineCommentEditorOpen())
            return;

        if (TryGetSelectedCommentPreview(out var cell, out var rect))
            ShowCommentPreview(cell, rect, CommentPreviewActivation.Selection);
        else
            DismissCommentPreview();
    }

    private void RestoreSelectedCommentPreview()
    {
        if (IsInlineCommentEditorOpen())
            return;

        if (TryGetSelectedCommentPreview(out var cell, out var rect))
            ShowCommentPreview(cell, rect, CommentPreviewActivation.Selection);
        else
            DismissCommentPreview(CommentPreviewActivation.Hover);
    }

    private void DismissCommentPreview(CommentPreviewActivation? activation = null)
    {
        if (activation.HasValue && IsInlineCommentEditorOpen())
            return;

        if (activation.HasValue &&
            _activeCommentPreviewKey?.Activation != activation.Value)
        {
            return;
        }

        if (_commentPreviewBorder is not null)
            _commentPreviewBorder.Visibility = Visibility.Collapsed;

        if (CommentOverlayHost is not null)
            CommentOverlayHost.IsHitTestVisible = false;

        _activeCommentPreviewKey = null;
        _commentPopupMode = CommentPopupMode.None;
        _activeNoteEditAddress = null;
        _activeThreadedEditAddress = null;
        _threadedEditExisting = null;
        _noteEditBox = null;
        _threadedRootBox = null;
        _threadedReplyBox = null;
        _threadedSelectedReplyBox = null;
        _threadedReplySelector = null;
        _threadedUpdateReplyButton = null;
        _threadedDeleteReplyButton = null;
        _threadedResolveBox = null;
        _commentInlineErrorBlock = null;
    }

    private bool TryGetCommentPreviewAt(Point pos, out DisplayCell cell, out Rect rect)
    {
        if (Viewport is not { } viewport)
        {
            cell = default;
            rect = Rect.Empty;
            return false;
        }

        if (viewport.SplitPanes is not null)
        {
            foreach (var layout in CalculateSplitPaneCellLayouts(viewport, MergedRegions, EditingCell))
            {
                if (layout.Cell.CommentDisplay is not null &&
                    RectHitTest.ContainsInclusive(layout.Rect, pos))
                {
                    cell = layout.Cell;
                    rect = layout.Rect;
                    return true;
                }
            }
        }

        if (HitTestViewportCell(viewport, default, pos) is { } address &&
            TryGetCommentPreviewForCell(address.Row, address.Col, out cell, out rect))
        {
            return true;
        }

        cell = default;
        rect = Rect.Empty;
        return false;
    }

    private bool TryGetSelectedCommentPreview(out DisplayCell cell, out Rect rect)
    {
        var selectedCell = SelectedRange?.Start;
        if (!selectedCell.HasValue &&
            SelectedRanges is { Count: > 0 } ranges)
        {
            selectedCell = ranges[0].Start;
        }

        if (selectedCell is { } address)
            return TryGetCommentPreviewForCell(address.Row, address.Col, out cell, out rect);

        cell = default;
        rect = Rect.Empty;
        return false;
    }

    private bool TryGetCommentPreviewForCell(uint row, uint col, out DisplayCell cell, out Rect rect)
    {
        if (Viewport is not { } viewport)
        {
            cell = default;
            rect = Rect.Empty;
            return false;
        }

        if (viewport.SplitPanes is not null)
        {
            foreach (var layout in CalculateSplitPaneCellLayouts(viewport, MergedRegions, EditingCell))
            {
                if (layout.Cell.Row == row &&
                    layout.Cell.Col == col &&
                    layout.Cell.CommentDisplay is not null)
                {
                    cell = layout.Cell;
                    rect = layout.Rect;
                    return true;
                }
            }
        }

        // Comments/notes are only ever keyed on a merged range's anchor cell (ViewportService only
        // populates CommentDisplay for the anchor address), so a hit-test or selection landing
        // anywhere else in the merged footprint must be redirected to the anchor before matching
        // against DisplayCell.CommentDisplay -- otherwise hovering/selecting the non-anchor part of
        // a merged cell silently finds nothing.
        var merge = FindMerge(row, col);
        var anchorRow = merge?.Start.Row ?? row;
        var anchorCol = merge?.Start.Col ?? col;

        foreach (var candidate in viewport.Cells)
        {
            if (candidate.Row != anchorRow ||
                candidate.Col != anchorCol ||
                candidate.CommentDisplay is null)
            {
                continue;
            }

            if (!TryGetCellRect(viewport, anchorRow, anchorCol, merge, out rect))
                break;

            cell = candidate;
            return true;
        }

        cell = default;
        rect = Rect.Empty;
        return false;
    }

    private bool TryGetCellRect(ViewportModel? viewport, uint row, uint col, out Rect rect) =>
        TryGetCellRect(viewport, row, col, FindMerge(row, col), out rect);

    /// <summary>
    /// Split-pane-aware counterpart to <see cref="TryGetCellRect(ViewportModel?, uint, uint, out Rect)"/>.
    /// When the viewport is split (<see cref="ViewportModel.SplitPanes"/> is set) the cell's on-screen
    /// rect depends on which pane it is scrolled into, so this walks
    /// <see cref="CalculateSplitPaneCellLayouts"/> first -- exactly as the transient hover preview
    /// (<see cref="TryGetCommentPreviewAt"/>/<see cref="TryGetCommentPreviewForCell"/>) already does --
    /// before falling back to the flat single-pane rect. Used for the PINNED ("Show Comment") note
    /// box so it lands in the correct pane instead of always assuming the flat viewport layout.
    /// See R91-render-comment-ui-5-1.
    /// </summary>
    private bool TryGetPinnedNoteCellRect(ViewportModel? viewport, uint row, uint col, out Rect rect)
    {
        if (viewport is { SplitPanes: not null })
        {
            foreach (var layout in CalculateSplitPaneCellLayouts(viewport, MergedRegions, EditingCell))
            {
                if (layout.Cell.Row == row && layout.Cell.Col == col)
                {
                    rect = layout.Rect;
                    return true;
                }
            }
        }

        return TryGetCellRect(viewport, row, col, out rect);
    }

    /// <summary>
    /// Computes the on-screen rect for the cell at (row, col). When <paramref name="merge"/> is a
    /// merged range anchored at (row, col), the rect is expanded to the full merged footprint (the
    /// same geometry the render passes use in GridView.Rendering.cs) so hover/selection popups and
    /// note indicators line up with the merged range's true visible bounds instead of just the
    /// anchor's own single-cell footprint.
    /// </summary>
    private bool TryGetCellRect(ViewportModel? viewport, uint row, uint col, GridRange? merge, out Rect rect)
    {
        if (viewport is null)
        {
            rect = Rect.Empty;
            return false;
        }

        var address = new CellAddress(ActiveSheetId, row, col);
        var range = merge is { } merged && merged.Start == address
            ? merged
            : new GridRange(address, address);
        if (!ViewportGeometryPlanner.TryGetVisibleRangeBounds(
                viewport,
                range,
                new ViewportGeometrySettings(
                    ActualRowHeaderWidth,
                    EffectiveColHeaderHeight,
                    MetricPlacement: ViewportMetricPlacement.MetricOffsets,
                    SplitColumnHeaderHeight: ColHeaderHeight),
                out var bounds))
        {
            rect = Rect.Empty;
            return false;
        }

        rect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        return true;
    }

    private void ShowCommentPreview(
        DisplayCell cell,
        Rect cellRect,
        CommentPreviewActivation activation)
    {
        if (IsInlineCommentEditorOpen())
            return;

        var display = cell.CommentDisplay;
        if (display is null)
        {
            DismissCommentPreview();
            return;
        }

        var key = new CommentPreviewKey(
            cell.Row,
            cell.Col,
            activation,
            display.Kind,
            display.Title,
            display.Body,
            display.IsResolved);

        if (_activeCommentPreviewKey != key || _commentPopupMode != CommentPopupMode.Preview)
            BuildCommentPreviewContent(display);

        _commentPopupMode = CommentPopupMode.Preview;
        _activeCommentPopupRow = cell.Row;
        _activeCommentPopupCol = cell.Col;
        _activeCommentPreviewKey = key;
        ShowCommentPopup(cellRect, GridCommentPreviewPlacementPlanner.EstimatePreviewSize(display));
    }

    private void ShowCommentPopup(Rect cellRect, Size desiredSize)
    {
        var border = EnsureCommentPreviewBorder();
        if (CommentOverlayHost is null)
            return;

        var placement = CalculateCommentPopupPlacement(cellRect, desiredSize);
        ApplyCommentPopupPlacement(placement);
        border.Visibility = Visibility.Visible;
        CommentOverlayHost.IsHitTestVisible = true;
    }

    private Border EnsureCommentPreviewBorder()
    {
        if (_commentPreviewBorder is { } existing)
        {
            if (CommentOverlayHost is not null && !CommentOverlayHost.Children.Contains(existing))
                CommentOverlayHost.Children.Add(existing);
            return existing;
        }

        _commentPreviewPanel = new StackPanel();
        _commentPreviewBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 225)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(158, 151, 113)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = _commentPreviewPanel,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                Direction = 315,
                Opacity = 0.22,
                ShadowDepth = 2
            }
        };
        AutomationProperties.SetAutomationId(_commentPreviewBorder, "GridCommentInWindowPopup");
        AutomationProperties.SetName(_commentPreviewBorder, "Comment");

        if (CommentOverlayHost is not null)
            CommentOverlayHost.Children.Add(_commentPreviewBorder);

        return _commentPreviewBorder;
    }

    private void MoveCommentPreviewToOverlay(Canvas? oldHost, Canvas? newHost)
    {
        if (_commentPreviewBorder is not { } border)
            return;

        oldHost?.Children.Remove(border);
        if (newHost is not null && !newHost.Children.Contains(border))
            newHost.Children.Add(border);

        if (newHost is not null)
            newHost.IsHitTestVisible = border.Visibility == Visibility.Visible;
        RefreshActiveCommentPopupPlacement();
    }

    /// <summary>
    /// Rebuilds the always-on pinned note boxes based on <see cref="PinnedNoteAddresses"/> and
    /// the current <see cref="Viewport"/>. Called when either property changes.
    /// </summary>
    internal void RefreshPinnedNoteBoxes()
    {
        if (CommentOverlayHost is not { } host)
            return;

        var pinned = PinnedNoteAddresses;
        var viewport = Viewport;

        // Remove borders for addresses that are no longer pinned or have scrolled off-screen.
        var toRemove = new List<(uint Row, uint Col)>();
        foreach (var key in _pinnedNoteBorders.Keys)
        {
            if (pinned is null || !pinned.Contains(key) ||
                !TryGetPinnedNoteCellRect(viewport, key.Row, key.Col, out _))
            {
                toRemove.Add(key);
            }
        }
        foreach (var key in toRemove)
        {
            host.Children.Remove(_pinnedNoteBorders[key]);
            _pinnedNoteBorders.Remove(key);
            if (_pinnedNoteConnectors.TryGetValue(key, out var removedConnector))
            {
                host.Children.Remove(removedConnector);
                _pinnedNoteConnectors.Remove(key);
            }
        }

        if (pinned is null || viewport is null)
            return;

        // Add/update borders for pinned addresses that are on-screen.
        foreach (var (row, col) in pinned)
        {
            if (!TryGetPinnedNoteCellRect(viewport, row, col, out var cellRect))
                continue;

            // Get display content — walk viewport cells for the comment text.
            CellCommentDisplay? display = null;
            foreach (var vcell in viewport.Cells)
            {
                if (vcell.Row == row && vcell.Col == col && vcell.CommentDisplay is not null)
                {
                    display = vcell.CommentDisplay;
                    break;
                }
            }
            if (display is null)
                continue;

            var desiredSize = GridCommentPreviewPlacementPlanner.EstimatePreviewSize(display);
            var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
            var scaledCellRect = new Rect(
                cellRect.Left * zoom, cellRect.Top * zoom,
                cellRect.Width * zoom, cellRect.Height * zoom);
            var placement = GridCommentPreviewPlacementPlanner.Calculate(
                scaledCellRect,
                new Size(Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)),
                desiredSize);

            // Draw/update the leader line bridging the pinned box back to its cell corner before the
            // box itself, so the box's z-order stays above the line it connects to.
            var connectorLine = GridCommentPreviewPlacementPlanner.CalculateConnector(scaledCellRect, placement);
            if (!_pinnedNoteConnectors.TryGetValue((row, col), out var connector))
            {
                connector = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(158, 151, 113)),
                    StrokeThickness = 1
                };
                AutomationProperties.SetAutomationId(connector, $"GridPinnedNoteConnector_{row}_{col}");
                host.Children.Add(connector);
                _pinnedNoteConnectors[(row, col)] = connector;
            }
            connector.X1 = connectorLine.Start.X;
            connector.Y1 = connectorLine.Start.Y;
            connector.X2 = connectorLine.End.X;
            connector.Y2 = connectorLine.End.Y;

            if (!_pinnedNoteBorders.TryGetValue((row, col), out var border))
            {
                var panel = new StackPanel();
                border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 255, 225)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(158, 151, 113)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8),
                    Child = panel,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 8,
                        Direction = 315,
                        Opacity = 0.22,
                        ShadowDepth = 2
                    }
                };
                AutomationProperties.SetAutomationId(border, $"GridPinnedNoteBox_{row}_{col}");
                AutomationProperties.SetName(border, "Pinned Note");
                host.Children.Add(border);
                _pinnedNoteBorders[(row, col)] = border;
            }

            // Rebuild content.
            if (border.Child is StackPanel existingPanel)
                existingPanel.Children.Clear();
            var contentPanel = new StackPanel();
            var titleBlock = CreateHeaderTextBlock(display.Title);
            titleBlock.Foreground = Brushes.Black;
            contentPanel.Children.Add(titleBlock);
            contentPanel.Children.Add(new TextBlock
            {
                FontSize = 12,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                Text = string.IsNullOrEmpty(display.Body) ? " " : display.Body
            });
            border.Child = contentPanel;

            border.Width = placement.Width;
            border.MaxHeight = placement.MaxHeight;
            Canvas.SetLeft(border, placement.HorizontalOffset);
            Canvas.SetTop(border, placement.VerticalOffset);
        }
    }

    private void BuildCommentPreviewContent(CellCommentDisplay display)
    {
        var panel = ResetCommentPanel();
        var title = CreateHeaderTextBlock(display.Title);
        title.Foreground = display.IsResolved
            ? new SolidColorBrush(Color.FromRgb(85, 85, 85))
            : Brushes.Black;
        panel.Children.Add(title);

        var body = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            Text = string.IsNullOrEmpty(display.Body) ? " " : display.Body
        };
        _commentPreviewScrollViewer = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };
        panel.Children.Add(_commentPreviewScrollViewer);
    }

    private void BuildNoteInlineEditor(string initialText)
    {
        var panel = ResetCommentPanel();
        panel.Children.Add(CreateHeaderTextBlock($"Note - {_activeCommentCellReference}"));

        _noteEditBox = CreateCommentTextBox(initialText, minLines: 4, maxLines: 7);
        AutomationProperties.SetAutomationId(_noteEditBox, "GridNoteInlineTextBox");
        AutomationProperties.SetName(_noteEditBox, "Note");
        _noteEditBox.PreviewKeyDown += NoteEditBox_PreviewKeyDown;
        panel.Children.Add(_noteEditBox);

        AddInlineErrorBlock(panel);
        panel.Children.Add(CreateEditorButtonRow(SubmitNoteInlineEdit, CancelCommentInlineEdit, saveText: "Save"));
    }

    private void BuildThreadedCommentInlineEditor(ThreadedComment? existing)
    {
        var panel = ResetCommentPanel();
        panel.Children.Add(CreateHeaderTextBlock($"Comment - {_activeCommentCellReference}"));

        if (existing is not null)
            panel.Children.Add(CreateThreadedConversationViewer(existing));

        _threadedRootBox = CreateCommentTextBox(existing?.Text ?? "", minLines: 3, maxLines: 6);
        AutomationProperties.SetAutomationId(_threadedRootBox, "GridThreadedCommentRootBox");
        AutomationProperties.SetName(_threadedRootBox, existing is null ? "Comment" : "Edit comment");
        _threadedRootBox.PreviewKeyDown += ThreadedTextBox_PreviewKeyDown;
        panel.Children.Add(CreateFieldLabel(existing is null ? "Comment" : "Edit comment", _threadedRootBox, topMargin: 0));
        panel.Children.Add(_threadedRootBox);

        if (existing is not null)
        {
            if (existing.Replies.Count > 0)
                panel.Children.Add(CreateSelectedReplyEditor(existing));

            _threadedReplyBox = CreateCommentTextBox("", minLines: 2, maxLines: 4);
            AutomationProperties.SetAutomationId(_threadedReplyBox, "GridThreadedCommentReplyBox");
            AutomationProperties.SetName(_threadedReplyBox, "Reply");
            _threadedReplyBox.PreviewKeyDown += ThreadedTextBox_PreviewKeyDown;
            panel.Children.Add(CreateFieldLabel("Reply", _threadedReplyBox, topMargin: 8));
            panel.Children.Add(_threadedReplyBox);
        }

        _threadedResolveBox = new CheckBox
        {
            Content = "Mark as resolved",
            IsChecked = existing?.IsResolved ?? false,
            Margin = new Thickness(0, 6, 0, 0)
        };
        AutomationProperties.SetAutomationId(_threadedResolveBox, "GridThreadedCommentResolvedBox");
        AutomationProperties.SetName(_threadedResolveBox, "Mark as resolved");
        panel.Children.Add(_threadedResolveBox);

        AddInlineErrorBlock(panel);
        panel.Children.Add(CreateEditorButtonRow(SubmitThreadedCommentInlineEdit, CancelCommentInlineEdit, existing is null ? "Save" : "Apply"));
    }

    private StackPanel ResetCommentPanel()
    {
        EnsureCommentPreviewBorder();
        _commentPreviewPanel ??= new StackPanel();
        _commentPreviewPanel.Children.Clear();
        _commentPreviewScrollViewer = null;
        _commentInlineErrorBlock = null;
        return _commentPreviewPanel;
    }

    private static TextBlock CreateHeaderTextBlock(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 5)
        };

    private static Label CreateFieldLabel(string content, Control target, double topMargin) =>
        new()
        {
            Content = content,
            Target = target,
            Padding = new Thickness(0),
            Margin = new Thickness(0, topMargin, 0, 2),
            FontSize = 11
        };

    private static TextBox CreateCommentTextBox(string text, int minLines, int maxLines)
    {
        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinLines = minLines,
            MaxLines = maxLines,
            Padding = new Thickness(5),
            FontSize = 12
        };
        TextOptions.SetTextFormattingMode(box, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(box, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(box, TextHintingMode.Fixed);
        return box;
    }

    private void AddInlineErrorBlock(Panel panel)
    {
        _commentInlineErrorBlock = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(178, 34, 34)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 0)
        };
        panel.Children.Add(_commentInlineErrorBlock);
    }

    private static StackPanel CreateEditorButtonRow(Action save, Action cancel, string saveText)
    {
        var saveButton = new Button
        {
            Content = saveText,
            Width = 72,
            MinHeight = 24,
            Margin = new Thickness(0, 8, 6, 0)
        };
        AutomationProperties.SetAutomationId(saveButton, "GridCommentInlineSaveButton");
        AutomationProperties.SetName(saveButton, saveText);
        saveButton.Click += (_, _) => save();

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 72,
            MinHeight = 24,
            Margin = new Thickness(0, 8, 0, 0)
        };
        AutomationProperties.SetAutomationId(cancelButton, "GridCommentInlineCancelButton");
        AutomationProperties.SetName(cancelButton, "Cancel");
        cancelButton.Click += (_, _) => cancel();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        row.Children.Add(saveButton);
        row.Children.Add(cancelButton);
        return row;
    }

    private ScrollViewer CreateThreadedConversationViewer(ThreadedComment existing)
    {
        var messages = new StackPanel();
        messages.Children.Add(CreateThreadedMessage(existing.Author, existing.Text, existing.CreatedAtUtc, isRoot: true));
        foreach (var reply in existing.Replies)
            messages.Children.Add(CreateThreadedMessage(reply.Author, reply.Text, reply.CreatedAtUtc, isRoot: false));

        return new ScrollViewer
        {
            Content = messages,
            MaxHeight = 92,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 6)
        };
    }

    private StackPanel CreateSelectedReplyEditor(ThreadedComment existing)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _threadedReplySelector = new ComboBox { MinWidth = 180 };
        AutomationProperties.SetAutomationId(_threadedReplySelector, "GridThreadedCommentReplySelector");
        AutomationProperties.SetName(_threadedReplySelector, "Reply to edit or delete");
        for (var i = 0; i < existing.Replies.Count; i++)
        {
            var descriptor = ThreadedCommentDialogPlanner.DescribeReply(
                i,
                existing.Replies[i],
                ThreadedCommentTimestampProfile.InlineRelativeLocal);
            var item = new ComboBoxItem { Content = descriptor.ChoiceText };
            AutomationProperties.SetName(item, descriptor.AutomationName.LiteralText ?? descriptor.ChoiceText);
            _threadedReplySelector.Items.Add(item);
        }

        _threadedReplySelector.SelectionChanged += (_, _) => PopulateSelectedReplyText(existing);
        _threadedReplySelector.SelectedIndex = 0;
        panel.Children.Add(CreateFieldLabel("Reply to edit", _threadedReplySelector, topMargin: 0));
        panel.Children.Add(_threadedReplySelector);

        _threadedSelectedReplyBox = CreateCommentTextBox("", minLines: 2, maxLines: 4);
        AutomationProperties.SetAutomationId(_threadedSelectedReplyBox, "GridThreadedCommentSelectedReplyBox");
        AutomationProperties.SetName(_threadedSelectedReplyBox, "Selected reply text");
        _threadedSelectedReplyBox.TextChanged += (_, _) => UpdateSelectedReplyActionState(existing);
        _threadedSelectedReplyBox.PreviewKeyDown += (_, e) =>
        {
            if (_threadedUpdateReplyButton?.IsEnabled == true &&
                Keyboard.Modifiers == ModifierKeys.Control &&
                e.Key == Key.Enter)
            {
                SubmitThreadedCommentReplyEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                CancelCommentInlineEdit();
                e.Handled = true;
            }
        };
        panel.Children.Add(CreateFieldLabel("Selected reply", _threadedSelectedReplyBox, topMargin: 8));
        panel.Children.Add(_threadedSelectedReplyBox);

        _threadedUpdateReplyButton = new Button
        {
            Content = "Update reply",
            Width = 104,
            MinHeight = 24,
            Margin = new Thickness(0, 6, 6, 0)
        };
        AutomationProperties.SetAutomationId(_threadedUpdateReplyButton, "GridThreadedCommentUpdateReplyButton");
        AutomationProperties.SetName(_threadedUpdateReplyButton, "Update selected reply");
        _threadedUpdateReplyButton.Click += (_, _) => SubmitThreadedCommentReplyEdit();

        _threadedDeleteReplyButton = new Button
        {
            Content = "Delete reply",
            Width = 104,
            MinHeight = 24,
            Margin = new Thickness(0, 6, 0, 0)
        };
        AutomationProperties.SetAutomationId(_threadedDeleteReplyButton, "GridThreadedCommentDeleteReplyButton");
        AutomationProperties.SetName(_threadedDeleteReplyButton, "Delete selected reply");
        _threadedDeleteReplyButton.Click += (_, _) => SubmitThreadedCommentReplyDelete();

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        actionRow.Children.Add(_threadedUpdateReplyButton);
        actionRow.Children.Add(_threadedDeleteReplyButton);
        panel.Children.Add(actionRow);
        PopulateSelectedReplyText(existing);
        return panel;
    }

    private static Border CreateThreadedMessage(string author, string text, DateTimeOffset? createdAtUtc, bool isRoot)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 5) };
        panel.Children.Add(new TextBlock
        {
            Text = ThreadedCommentDialogPlanner.FormatMessageHeading(
                author,
                createdAtUtc,
                ThreadedCommentTimestampProfile.InlineRelativeLocal),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(isRoot ? Color.FromRgb(0x1F, 0x49, 0x7D) : Color.FromRgb(0x40, 0x40, 0x40))
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 0, 0),
            FontSize = 11
        });

        return new Border
        {
            Child = panel,
            Background = new SolidColorBrush(isRoot ? Color.FromRgb(0xF0, 0xF4, 0xF8) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 5, 7, 5),
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private void NoteEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            SubmitNoteInlineEdit();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
        {
            CancelCommentInlineEdit();
            e.Handled = true;
        }
    }

    private void ThreadedTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            SubmitThreadedCommentInlineEdit();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
        {
            CancelCommentInlineEdit();
            e.Handled = true;
        }
    }

    private void SubmitNoteInlineEdit()
    {
        var text = (_noteEditBox?.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowInlineCommentError("Enter a note.");
            FocusCommentTextBox(_noteEditBox);
            return;
        }

        if (_activeNoteEditAddress is not { } address)
            return;

        var args = new GridNoteInlineEditSubmittedEventArgs(address, text);
        NoteInlineEditSubmitted?.Invoke(this, args);
        CompleteInlineSubmit(args.KeepOpen, args.ErrorMessage);
    }

    private void SubmitThreadedCommentInlineEdit()
    {
        if (!TryCreateThreadedCommentEditResult(
                _threadedEditExisting,
                _threadedRootBox?.Text,
                _threadedReplyBox?.Text,
                _threadedResolveBox?.IsChecked == true,
                out var result,
                out var error))
        {
            ShowInlineCommentError(error ?? "Enter a comment.");
            FocusCommentTextBox(_threadedEditExisting is null ? _threadedRootBox : _threadedReplyBox ?? _threadedRootBox);
            return;
        }

        SubmitThreadedCommentInlineResult(result);
    }

    private void SubmitThreadedCommentReplyEdit()
    {
        if (_threadedEditExisting is not { } existing ||
            !TryCreateThreadedReplyEditResult(
                existing,
                _threadedReplySelector?.SelectedIndex ?? -1,
                _threadedSelectedReplyBox?.Text,
                _threadedResolveBox?.IsChecked == true,
                out var result,
                out var error))
        {
            ShowInlineCommentError("Select a reply and enter reply text.");
            FocusCommentTextBox(_threadedSelectedReplyBox);
            return;
        }

        SubmitThreadedCommentInlineResult(result);
    }

    private void SubmitThreadedCommentReplyDelete()
    {
        if (_threadedEditExisting is not { } existing)
        {
            ShowInlineCommentError("Select a reply.");
            FocusCommentTextBox(_threadedSelectedReplyBox);
            return;
        }

        if (!TryCreateThreadedReplyDeleteResult(
                existing,
                _threadedReplySelector?.SelectedIndex ?? -1,
                _threadedResolveBox?.IsChecked == true,
                out var result,
                out var error))
        {
            ShowInlineCommentError(error ?? "Select a reply.");
            FocusCommentTextBox(_threadedSelectedReplyBox);
            return;
        }

        SubmitThreadedCommentInlineResult(result);
    }

    private void SubmitThreadedCommentInlineResult(GridThreadedCommentEditResult result)
    {
        if (_activeThreadedEditAddress is not { } address)
            return;

        var args = new GridThreadedCommentInlineEditSubmittedEventArgs(address, result);
        ThreadedCommentInlineEditSubmitted?.Invoke(this, args);
        CompleteInlineSubmit(args.KeepOpen, args.ErrorMessage);
    }

    private void CompleteInlineSubmit(bool keepOpen, string? errorMessage)
    {
        if (keepOpen)
        {
            ShowInlineCommentError(errorMessage ?? "The comment could not be saved.");
            return;
        }

        DismissCommentPreview();
        Focus();
        Keyboard.Focus(this);
    }

    private void CancelCommentInlineEdit()
    {
        DismissCommentPreview();
        Focus();
        Keyboard.Focus(this);
    }

    private void ShowInlineCommentError(string message)
    {
        if (_commentInlineErrorBlock is null)
            return;

        _commentInlineErrorBlock.Text = message;
        _commentInlineErrorBlock.Visibility = Visibility.Visible;
    }

    private void FocusCommentTextBox(TextBox? textBox)
    {
        if (textBox is null)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.CaretIndex = textBox.Text.Length;
            textBox.SelectionLength = 0;
        }));
    }

    private void PopulateSelectedReplyText(ThreadedComment existing)
    {
        var replyIndex = _threadedReplySelector?.SelectedIndex ?? -1;
        if (_threadedSelectedReplyBox is not null)
        {
            _threadedSelectedReplyBox.Text = IsValidReplyIndex(existing, replyIndex)
                ? existing.Replies[replyIndex].Text
                : "";
        }

        UpdateSelectedReplyActionState(existing);
    }

    private void UpdateSelectedReplyActionState(ThreadedComment existing)
    {
        var hasSelection = IsValidReplyIndex(existing, _threadedReplySelector?.SelectedIndex ?? -1);
        if (_threadedDeleteReplyButton is not null)
            _threadedDeleteReplyButton.IsEnabled = hasSelection;
        if (_threadedUpdateReplyButton is not null)
            _threadedUpdateReplyButton.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_threadedSelectedReplyBox?.Text);
    }

    private static bool TryCreateThreadedCommentEditResult(
        ThreadedComment? existing,
        string? rootText,
        string? replyText,
        bool isResolved,
        out GridThreadedCommentEditResult result,
        out string? error)
    {
        var trimmedRoot = (rootText ?? "").Trim();
        var trimmedReply = (replyText ?? "").Trim();
        if (existing is null)
        {
            result = new GridThreadedCommentEditResult(
                null,
                string.IsNullOrWhiteSpace(trimmedRoot) ? null : trimmedRoot,
                isResolved);
            if (result.ReplyText is null)
            {
                error = "Enter a comment.";
                return false;
            }

            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(trimmedRoot))
        {
            result = default;
            error = "Enter a comment.";
            return false;
        }

        var rootEdit = !string.Equals(trimmedRoot, existing.Text, StringComparison.Ordinal)
            ? trimmedRoot
            : null;
        result = new GridThreadedCommentEditResult(
            rootEdit,
            string.IsNullOrWhiteSpace(trimmedReply) ? null : trimmedReply,
            isResolved);
        error = null;
        return true;
    }

    private static bool TryCreateThreadedReplyEditResult(
        ThreadedComment existing,
        int replyIndex,
        string? replyText,
        bool isResolved,
        out GridThreadedCommentEditResult result,
        out string? error)
    {
        result = new GridThreadedCommentEditResult(
            null,
            null,
            isResolved,
            GridThreadedCommentEditAction.EditReply,
            replyIndex,
            (replyText ?? "").Trim());
        if (!IsValidReplyIndex(existing, replyIndex))
        {
            error = "Select a reply.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.ReplyEditText))
        {
            error = "Enter reply text.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreateThreadedReplyDeleteResult(
        ThreadedComment existing,
        int replyIndex,
        bool isResolved,
        out GridThreadedCommentEditResult result,
        out string? error)
    {
        result = new GridThreadedCommentEditResult(
            null,
            null,
            isResolved,
            GridThreadedCommentEditAction.DeleteReply,
            replyIndex);
        if (!IsValidReplyIndex(existing, replyIndex))
        {
            error = "Select a reply.";
            return false;
        }

        error = null;
        return true;
    }

    private void RefreshCommentPreviewAfterViewportChanged()
    {
        if (IsInlineCommentEditorOpen())
        {
            RefreshActiveCommentPopupPlacement();
            return;
        }

        DismissCommentPreview();
        UpdateCommentPreviewForSelection();
    }

    private void RefreshActiveCommentPopupPlacement()
    {
        if (_commentPreviewBorder is null ||
            _commentPopupMode == CommentPopupMode.None ||
            CommentOverlayHost is null)
        {
            return;
        }

        if (!TryGetCellRect(Viewport, _activeCommentPopupRow, _activeCommentPopupCol, out var cellRect))
        {
            DismissCommentPreview();
            return;
        }

        var desiredSize = _commentPopupMode switch
        {
            CommentPopupMode.Preview when TryGetCommentPreviewForCell(_activeCommentPopupRow, _activeCommentPopupCol, out var cell, out _) &&
                cell.CommentDisplay is { } display => GridCommentPreviewPlacementPlanner.EstimatePreviewSize(display),
            CommentPopupMode.ThreadedCommentEditor when _threadedEditExisting is not null => new Size(CommentEditorWidth, CommentEditorExistingDesiredHeight),
            CommentPopupMode.NoteEditor or CommentPopupMode.ThreadedCommentEditor => new Size(CommentEditorWidth, CommentEditorDesiredHeight),
            _ => GridCommentPreviewPlacementPlanner.EstimatePreviewSize(new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", " "))
        };

        ApplyCommentPopupPlacement(CalculateCommentPopupPlacement(cellRect, desiredSize));
    }

    private GridCommentPreviewPlacement CalculateCommentPopupPlacement(Rect cellRect, Size desiredSize)
    {
        var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        var scaledCellRect = new Rect(
            cellRect.Left * zoom,
            cellRect.Top * zoom,
            cellRect.Width * zoom,
            cellRect.Height * zoom);
        return GridCommentPreviewPlacementPlanner.Calculate(
            scaledCellRect,
            new Size(Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)),
            desiredSize);
    }

    private void ApplyCommentPopupPlacement(GridCommentPreviewPlacement placement)
    {
        if (_commentPreviewBorder is null)
            return;

        _commentPreviewBorder.Width = placement.Width;
        _commentPreviewBorder.MaxHeight = placement.MaxHeight;
        if (_commentPreviewScrollViewer is not null)
            _commentPreviewScrollViewer.MaxHeight = Math.Max(32, placement.MaxHeight - 36);
        Canvas.SetLeft(_commentPreviewBorder, placement.HorizontalOffset);
        Canvas.SetTop(_commentPreviewBorder, placement.VerticalOffset);
    }

    private bool IsInlineCommentEditorOpen() =>
        _commentPopupMode is CommentPopupMode.NoteEditor or CommentPopupMode.ThreadedCommentEditor;

    private static bool IsValidReplyIndex(ThreadedComment comment, int replyIndex) =>
        replyIndex >= 0 && replyIndex < comment.Replies.Count;

    internal static string FormatMessageHeading(string author, DateTimeOffset? createdAtUtc) =>
        ThreadedCommentDialogPlanner.FormatMessageHeading(
            author,
            createdAtUtc,
            ThreadedCommentTimestampProfile.InlineRelativeLocal);

    /// <summary>
    /// R91-render-comment-ui-5-2: real Excel always shows comment/note timestamps converted to the
    /// viewer's LOCAL time zone, with relative phrasing for recent activity ("2m", "Today, 9:00 AM",
    /// "Yesterday, ..."), never a bare absolute UTC stamp. <paramref name="now"/> is threaded through
    /// (defaulting to <see cref="DateTimeOffset.Now"/> at the single-arg call sites) purely so tests
    /// can pin "the current moment" instead of racing the real clock.
    /// </summary>
    internal static string FormatMessageHeading(string author, DateTimeOffset? createdAtUtc, DateTimeOffset now) =>
        ThreadedCommentDialogPlanner.FormatMessageHeading(
            author,
            createdAtUtc,
            ThreadedCommentTimestampProfile.InlineRelativeLocal,
            now);
}
