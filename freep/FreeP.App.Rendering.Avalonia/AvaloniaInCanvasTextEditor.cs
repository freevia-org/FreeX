using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Manages a plain-text in-canvas editing overlay for the Avalonia <see cref="SlideCanvas"/>.
/// </summary>
public sealed class AvaloniaInCanvasTextEditor
{
    private readonly SlideCanvas _canvas;
    private readonly EditingSession _editor;
    private readonly Panel _overlay;

    private TextBox? _textBox;
    private InCanvasTextEditPlanner? _editPlan;
    private uint _editingShapeId;
    private bool _active;
    private bool _committing;

    private TextBox? _cellTextBox;
    private Border? _cellHighlight;
    private InCanvasTableCellTextEditPlanner? _cellEditPlan;
    private uint _editingTableShapeId;
    private int _editingCellRow;
    private int _editingCellCol;
    private bool _cellEditActive;
    private bool _cellClosing;

    /// <summary>True while a shape's text is being edited in the overlay TextBox.</summary>
    public bool IsActive => _active;

    /// <summary>The id of the shape currently being edited, or 0 if not active.</summary>
    public uint ActiveShapeId => _editingShapeId;

    /// <summary>True while a table cell is being edited in the overlay TextBox.</summary>
    public bool IsCellEditActive => _cellEditActive;

    /// <summary>The id of the table shape currently being edited, or 0 if not active.</summary>
    public uint ActiveTableShapeId => _editingTableShapeId;

    public bool TryApplyActiveTableCellTextFormat(TableCellTextFormatKind kind)
    {
        var plan = _editor.PlanActiveTableCellTextFormat(kind);
        if (plan.Command is null)
            return false;

        _editor.Bus.Execute(plan.Command);

        if (_cellEditActive &&
            _cellTextBox is not null &&
            plan.ShapeId == _editingTableShapeId &&
            plan.Row == _editingCellRow &&
            plan.Col == _editingCellCol &&
            plan.TargetValue is { } value)
        {
            ApplyCellOverlayFormat(kind, value);
        }

        return true;
    }

    public AvaloniaInCanvasTextEditor(SlideCanvas canvas, EditingSession editor, Panel overlay)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

        _canvas.PointerPressed += OnCanvasPointerPressed;

        _editor.SelectionChanged += (_, _) => RefreshTableCellHighlight();
        _editor.ActiveTableCellChanged += (_, _) => RefreshTableCellHighlight();
        _editor.Changed += RefreshTableCellHighlight;
        _editor.CurrentSlideChanged += (_, _) =>
        {
            CommitCellEdit();
            RefreshTableCellHighlight();
        };
    }

    /// <summary>Activates the text editor for the given shape.</summary>
    public void Activate(uint shapeId)
    {
        if (_active && _editingShapeId == shapeId)
            return;

        CommitCellEdit();
        Commit();

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
            return;

        var startPlan = InCanvasTextEditPlanner.BeginShapeEdit(
            _editor.CurrentSlideIndex,
            _editor.Presentation,
            slide,
            shapeId,
            _canvas.CurrentTransform,
            minimumWidth: 40,
            minimumHeight: 20,
            InCanvasTextEditKind.PlainText);
        if (!startPlan.IsReady || startPlan.Placement is null)
            return;

        _editingShapeId = shapeId;
        _active = true;
        _editPlan = startPlan.EditPlanner;

        var placement = startPlan.Placement.Value;

        _textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            Text = startPlan.OriginalPlainText,
            MinWidth = placement.Width,
            MinHeight = placement.Height,
            Width = placement.Width,
            Height = placement.Height,
            Padding = new Thickness(2),
            Background = new global::Avalonia.Media.SolidColorBrush(
                global::Avalonia.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new global::Avalonia.Media.SolidColorBrush(
                global::Avalonia.Media.Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(1.5),
        };

        Canvas.SetLeft(_textBox, placement.Left);
        Canvas.SetTop(_textBox, placement.Top);

        _textBox.LostFocus += (_, _) => Commit();
        _textBox.KeyDown += OnTextBoxKeyDown;

        _overlay.Children.Add(_textBox);
        UpdateOverlayState();
        _textBox.Focus();
        _textBox.SelectAll();
    }

    /// <summary>Activates the text editor for the given table cell.</summary>
    public void ActivateCellEdit(uint shapeId, int row, int col)
    {
        if (_cellEditActive &&
            _editingTableShapeId == shapeId &&
            _editingCellRow == row &&
            _editingCellCol == col)
        {
            return;
        }

        Commit();
        CommitCellEdit();

        var startPlan = AvaloniaTableCellEditAdapter.BeginEdit(
            _canvas,
            _editor,
            shapeId,
            row,
            col,
            minimumWidth: 30,
            minimumHeight: 18);
        if (!startPlan.IsReady || startPlan.Cell is null || startPlan.Placement is null)
            return;

        var placement = startPlan.Placement.Value;
        _editingTableShapeId = shapeId;
        _editingCellRow = startPlan.Row;
        _editingCellCol = startPlan.Col;
        _cellEditPlan = startPlan.EditPlanner;
        _cellEditActive = true;

        _editor.Select(shapeId);
        _editor.SetActiveTableCell(startPlan.Row, startPlan.Col);

        _cellTextBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Text = InCanvasTextEditPlanner.ExtractPlainText(startPlan.OriginalBody),
            MinWidth = placement.Width,
            MinHeight = placement.Height,
            Width = placement.Width,
            Height = placement.Height,
            Padding = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(1.5),
        };

        Canvas.SetLeft(_cellTextBox, placement.Left);
        Canvas.SetTop(_cellTextBox, placement.Top);

        _cellTextBox.LostFocus += (_, _) => CommitCellEdit();
        _cellTextBox.KeyDown += OnCellTextBoxKeyDown;

        _overlay.Children.Add(_cellTextBox);
        RefreshTableCellHighlight();
        UpdateOverlayState();
        _cellTextBox.Focus();
        _cellTextBox.SelectAll();
    }

    /// <summary>Commits the current text edit, if active, to the command bus and hides the overlay.</summary>
    public void Commit()
    {
        if (!_active || _textBox is null || _committing)
            return;

        _committing = true;
        try
        {
            var newText = _textBox.Text ?? string.Empty;
            var editPlan = _editPlan;

            _overlay.Children.Remove(_textBox);
            _textBox = null;
            _active = false;
            _editPlan = null;
            UpdateOverlayState();

            var slide = _editor.CurrentSlide;
            if (slide is null)
                return;

            var shape = slide.Shapes.FirstOrDefault(s => s.Id == _editingShapeId);
            if (shape is null)
                return;

            var decision = editPlan?.CommitPlainText(newText)
                ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);
            if (decision.Command is not null)
                _editor.Bus.Execute(decision.Command);
        }
        finally
        {
            _committing = false;
        }
    }

    /// <summary>Commits the current table-cell edit, if active, to the command bus and hides the overlay.</summary>
    public void CommitCellEdit()
    {
        if (!_cellEditActive || _cellTextBox is null || _cellClosing)
            return;

        _cellClosing = true;
        try
        {
            var newText = _cellTextBox.Text ?? string.Empty;
            var editPlan = _cellEditPlan;
            var shapeId = _editingTableShapeId;
            var row = _editingCellRow;
            var col = _editingCellCol;

            _overlay.Children.Remove(_cellTextBox);
            _cellTextBox = null;
            _cellEditActive = false;
            _cellEditPlan = null;
            UpdateOverlayState();

            var slide = _editor.CurrentSlide;
            var shape = slide?.Shapes.FirstOrDefault(s => s.Id == shapeId);
            var cell = shape?.Table?.Rows.ElementAtOrDefault(row)?.Cells.ElementAtOrDefault(col);
            if (cell is null)
                return;

            var newBody = InCanvasTextEditPlanner.BuildPlainTextBody(cell.TextBody, newText);
            var decision = AvaloniaTableCellEditAdapter.CommitRichText(editPlan, newBody);
            if (decision.Command is not null)
                _editor.Bus.Execute(decision.Command);
        }
        finally
        {
            _cellClosing = false;
            RefreshTableCellHighlight();
        }
    }

    /// <summary>Cancels the edit without committing.</summary>
    public void Cancel()
    {
        if (!_active || _textBox is null)
            return;

        _overlay.Children.Remove(_textBox);
        _textBox = null;
        _active = false;
        _ = _editPlan?.Cancel();
        _editPlan = null;
        UpdateOverlayState();
    }

    /// <summary>Cancels the current table-cell edit without committing.</summary>
    public void CancelCellEdit()
    {
        if (!_cellEditActive || _cellTextBox is null || _cellClosing)
            return;

        _cellClosing = true;
        try
        {
            _overlay.Children.Remove(_cellTextBox);
            _cellTextBox = null;
            _cellEditActive = false;
            _ = AvaloniaTableCellEditAdapter.Cancel(_cellEditPlan);
            _cellEditPlan = null;
            UpdateOverlayState();
        }
        finally
        {
            _cellClosing = false;
            RefreshTableCellHighlight();
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
            return;

        var pt = e.GetPosition(_canvas);
        if (TryHandleTableCellPointer(pt.X, pt.Y, e.ClickCount))
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount < 2)
            return;

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
            return;

        var xf = _canvas.CurrentTransform;
        var shapeEditPoint = e.GetPosition(_canvas);
        var slidePt = xf.ScreenToSlide(shapeEditPoint.X, shapeEditPoint.Y);

        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        if (!hitId.HasValue)
        {
            CommitCellEdit();
            return;
        }

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == hitId.Value);
        if (shape?.TextBody is null)
        {
            CommitCellEdit();
            return;
        }

        Activate(hitId.Value);
        e.Handled = true;
    }

    internal bool TryHandleTableCellPointer(double screenX, double screenY, int clickCount)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
            return false;

        var xf = _canvas.CurrentTransform;
        var slidePt = xf.ScreenToSlide(screenX, screenY);
        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        if (!hitId.HasValue)
        {
            CommitCellEdit();
            return false;
        }

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == hitId.Value);
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return false;

        var cellHit = TableCellHitTester.HitTest(shape, slidePt.X, slidePt.Y);
        if (!cellHit.HasValue)
            return false;

        _editor.SetActiveTableCell(cellHit.Value.Row, cellHit.Value.Col);

        if (clickCount >= 2)
            ActivateCellEdit(shape.Id, cellHit.Value.Row, cellHit.Value.Col);

        return true;
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void OnCellTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelCellEdit();
            e.Handled = true;
        }
    }

    private void RefreshTableCellHighlight()
    {
        if (!_overlay.Dispatcher.CheckAccess())
        {
            _overlay.Dispatcher.Post(RefreshTableCellHighlight);
            return;
        }

        if (_cellHighlight is not null)
        {
            _overlay.Children.Remove(_cellHighlight);
            _cellHighlight = null;
        }

        var state = AvaloniaTableCellEditAdapter.PlanSelectedCell(_editor);
        if (!state.CanEditText ||
            state.ShapeId is null ||
            state.Row is null ||
            state.Col is null)
        {
            UpdateOverlayState();
            return;
        }

        var shape = _editor.CurrentSlide?.Shapes.FirstOrDefault(s => s.Id == state.ShapeId.Value);
        if (shape?.Kind != SlideShapeKind.Table)
        {
            UpdateOverlayState();
            return;
        }

        var cellRect = TableCellHitTester.GetCellRect(shape, state.Row.Value, state.Col.Value);
        if (cellRect is null)
        {
            UpdateOverlayState();
            return;
        }

        var screenRect = SlideCanvasGeometryPlanner.DipBoundsToScreen(cellRect.Value, _canvas.CurrentTransform);
        _cellHighlight = new Border
        {
            Width = Math.Max(1, screenRect.Width),
            Height = Math.Max(1, screenRect.Height),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x21, 0x96, 0xF3)),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(_cellHighlight, screenRect.Left);
        Canvas.SetTop(_cellHighlight, screenRect.Top);
        _overlay.Children.Insert(0, _cellHighlight);
        UpdateOverlayState();
    }

    private void UpdateOverlayState()
    {
        _overlay.IsVisible = _overlay.Children.Count > 0;
        _overlay.IsHitTestVisible = _active || _cellEditActive;
    }

    private void ApplyCellOverlayFormat(TableCellTextFormatKind kind, bool value)
    {
        if (_cellTextBox is null)
            return;

        switch (kind)
        {
            case TableCellTextFormatKind.Bold:
                _cellTextBox.FontWeight = value ? FontWeight.Bold : FontWeight.Normal;
                break;
            case TableCellTextFormatKind.Italic:
                _cellTextBox.FontStyle = value ? FontStyle.Italic : FontStyle.Normal;
                break;
            case TableCellTextFormatKind.Underline:
                _cellTextBox.Classes.Set("freep-table-cell-underline", value);
                _cellTextBox.BorderThickness = value
                    ? new Thickness(1.5, 1.5, 1.5, 3.0)
                    : new Thickness(1.5);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
