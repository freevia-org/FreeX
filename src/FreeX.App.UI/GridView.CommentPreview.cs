using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

    private readonly record struct CommentPreviewKey(
        uint Row,
        uint Col,
        CommentPreviewActivation Activation,
        CellCommentDisplayKind Kind,
        string Title,
        string Body,
        bool IsResolved);

    private Popup? _commentPreviewPopup;
    private Border? _commentPreviewBorder;
    private ScrollViewer? _commentPreviewScrollViewer;
    private TextBlock? _commentPreviewTitleBlock;
    private TextBlock? _commentPreviewBodyBlock;
    private CommentPreviewKey? _activeCommentPreviewKey;

    private void UpdateCommentPreviewForPointer(Point pos)
    {
        if (TryGetCommentPreviewAt(pos, out var cell, out var rect))
        {
            ShowCommentPreview(cell, rect, CommentPreviewActivation.Hover);
            return;
        }

        RestoreSelectedCommentPreview();
    }

    private void UpdateCommentPreviewForSelection()
    {
        if (TryGetSelectedCommentPreview(out var cell, out var rect))
            ShowCommentPreview(cell, rect, CommentPreviewActivation.Selection);
        else
            DismissCommentPreview();
    }

    private void RestoreSelectedCommentPreview()
    {
        if (TryGetSelectedCommentPreview(out var cell, out var rect))
            ShowCommentPreview(cell, rect, CommentPreviewActivation.Selection);
        else
            DismissCommentPreview(CommentPreviewActivation.Hover);
    }

    private void DismissCommentPreview(CommentPreviewActivation? activation = null)
    {
        if (activation.HasValue &&
            _activeCommentPreviewKey?.Activation != activation.Value)
        {
            return;
        }

        if (_commentPreviewPopup is { IsOpen: true } popup)
            popup.IsOpen = false;

        _activeCommentPreviewKey = null;
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

        foreach (var candidate in viewport.Cells)
        {
            if (candidate.Row != row ||
                candidate.Col != col ||
                candidate.CommentDisplay is null)
            {
                continue;
            }

            if (!TryGetCellRect(viewport, row, col, out rect))
                break;

            cell = candidate;
            return true;
        }

        cell = default;
        rect = Rect.Empty;
        return false;
    }

    private bool TryGetCellRect(ViewportModel viewport, uint row, uint col, out Rect rect)
    {
        var rowMetric = FindRowMetric(viewport.RowMetrics, row);
        var colMetric = FindColMetric(viewport.ColMetrics, col);
        if (rowMetric is null || colMetric is null)
        {
            rect = Rect.Empty;
            return false;
        }

        rect = new Rect(
            ActualRowHeaderWidth + colMetric.LeftOffset,
            EffectiveColHeaderHeight + rowMetric.TopOffset,
            colMetric.Width,
            rowMetric.Height);
        return true;
    }

    private void ShowCommentPreview(
        DisplayCell cell,
        Rect cellRect,
        CommentPreviewActivation activation)
    {
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
        var popup = EnsureCommentPreviewPopup();
        var placement = GridCommentPreviewPlacementPlanner.Calculate(
            cellRect,
            new Size(GetLogicalViewportWidth(), GetLogicalViewportHeight()),
            display);

        if (_activeCommentPreviewKey != key)
            UpdateCommentPreviewContent(display);

        _commentPreviewBorder!.Width = placement.Width;
        _commentPreviewBorder.MaxHeight = placement.MaxHeight;
        _commentPreviewScrollViewer!.MaxHeight = Math.Max(32, placement.MaxHeight - 36);
        popup.HorizontalOffset = placement.HorizontalOffset;
        popup.VerticalOffset = placement.VerticalOffset;
        popup.IsOpen = true;
        _activeCommentPreviewKey = key;
    }

    private Popup EnsureCommentPreviewPopup()
    {
        if (_commentPreviewPopup is { } existing)
            return existing;

        _commentPreviewTitleBlock = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 5)
        };
        _commentPreviewBodyBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        };
        _commentPreviewScrollViewer = new ScrollViewer
        {
            Content = _commentPreviewBodyBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false
        };

        var panel = new StackPanel();
        panel.Children.Add(_commentPreviewTitleBlock);
        panel.Children.Add(_commentPreviewScrollViewer);

        _commentPreviewBorder = new Border
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

        _commentPreviewPopup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = this,
            StaysOpen = true,
            Child = _commentPreviewBorder
        };
        return _commentPreviewPopup;
    }

    private void UpdateCommentPreviewContent(CellCommentDisplay display)
    {
        _commentPreviewTitleBlock!.Text = display.Title;
        _commentPreviewTitleBlock.Foreground = display.IsResolved
            ? new SolidColorBrush(Color.FromRgb(85, 85, 85))
            : Brushes.Black;
        _commentPreviewBodyBlock!.Text = string.IsNullOrEmpty(display.Body)
            ? " "
            : display.Body;
    }
}
