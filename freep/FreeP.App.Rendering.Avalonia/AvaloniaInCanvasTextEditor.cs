using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Manages rich in-canvas shape and table-cell editing for the Avalonia <see cref="SlideCanvas"/>.
/// </summary>
public sealed class AvaloniaInCanvasTextEditor
{
    private readonly SlideCanvas _canvas;
    private readonly EditingSession _editor;
    private readonly Panel _overlay;

    private AvaloniaRichTextEditor? _textBox;
    private InCanvasTextEditPlanner? _editPlan;
    private uint _editingShapeId;
    private bool _active;
    private bool _committing;

    private AvaloniaRichTextEditor? _cellTextBox;
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

    /// <summary>True while a table cell is being edited in the rich overlay editor.</summary>
    public bool IsCellEditActive => _cellEditActive;

    /// <summary>The id of the table shape currently being edited, or 0 if not active.</summary>
    public uint ActiveTableShapeId => _editingTableShapeId;

    public bool TryApplyActiveShapeTextFormat(TableCellTextFormatKind kind)
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ToggleTextFormat(kind);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeFontFamily(string? fontFamily)
    {
        if (!_active || _textBox is null)
            return false;

        bool changed = _textBox.ApplyFontFamily(fontFamily);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeFontSize(double? sizePt)
    {
        if (!_active || _textBox is null)
            return false;

        bool changed = _textBox.ApplyFontSize(sizePt);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeColor(ThemeAwareColor? color)
    {
        if (!_active || _textBox is null)
            return false;

        bool changed = _textBox.ApplyColor(color);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellTextFormat(TableCellTextFormatKind kind)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ToggleTextFormat(kind);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellFontFamily(string? fontFamily)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyFontFamily(fontFamily);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellFontSize(double? sizePt)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyFontSize(sizePt);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellColor(ThemeAwareColor? color)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyColor(color);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellParagraphAlignment(TextAlign alignment)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyParagraphAlignment(alignment);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellParagraphBulletToggle()
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ToggleParagraphBullets();
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellParagraphNumberingToggle()
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ToggleParagraphNumbering();
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellParagraphListPreset(TableCellListPresetDescriptor preset)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyParagraphListPreset(preset);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellParagraphPictureBullet(PresentationPictureBulletPayload payload)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyParagraphPictureBullet(payload);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellParagraphIndent()
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyParagraphIndent(increase: true);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveTableCellParagraphOutdent()
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;
        bool changed = _cellTextBox.ApplyParagraphIndent(increase: false);
        if (changed)
            RefreshCellOverlayRichTextPlan();
        return changed;
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
            InCanvasTextEditKind.RichText);
        if (!startPlan.IsReady || startPlan.Placement is null)
            return;

        _editingShapeId = shapeId;
        _active = true;
        _editPlan = startPlan.EditPlanner;

        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            new SlideScreenRect(
                startPlan.Placement.Value.Left,
                startPlan.Placement.Value.Top,
                startPlan.Placement.Value.Width,
                startPlan.Placement.Value.Height),
            minimumWidth: 40,
            minimumHeight: 20);

        _textBox = new AvaloniaRichTextEditor(startPlan.OriginalBody, backgroundAlpha: 0xCC)
        {
            MinWidth = placement.Width,
            MinHeight = placement.Height,
            Width = placement.Width,
            Height = placement.Height,
        };
        AvaloniaInCanvasTextEditAdapter.ApplyRichTextEditorPlan(_textBox, startPlan.RichTextPlan);

        Canvas.SetLeft(_textBox, placement.Left);
        Canvas.SetTop(_textBox, placement.Top);

        _textBox.InputBox.LostFocus += (_, _) => Commit();
        _textBox.InputBox.KeyDown += OnTextBoxKeyDown;

        _overlay.Children.Add(_textBox);
        UpdateOverlayState();
        _textBox.FocusEditor();
        ApplyInitialSelection(_textBox, startPlan.InitialSelection);
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

        _cellTextBox = new AvaloniaRichTextEditor(startPlan.OriginalBody, backgroundAlpha: 0xEE)
        {
            MinWidth = placement.Width,
            MinHeight = placement.Height,
            Width = placement.Width,
            Height = placement.Height,
        };
        AvaloniaTableCellEditAdapter.ApplyRichTextEditorPlan(_cellTextBox, startPlan.RichTextPlan);

        Canvas.SetLeft(_cellTextBox, placement.Left);
        Canvas.SetTop(_cellTextBox, placement.Top);

        _cellTextBox.InputBox.LostFocus += (_, _) => CommitCellEdit();
        _cellTextBox.InputBox.KeyDown += OnCellTextBoxKeyDown;

        _overlay.Children.Add(_cellTextBox);
        RefreshTableCellHighlight();
        UpdateOverlayState();
        _cellTextBox.FocusEditor();
        ApplyInitialSelection(_cellTextBox, startPlan.InitialSelection);
    }

    /// <summary>Commits the current text edit, if active, to the command bus and hides the overlay.</summary>
    public void Commit()
    {
        if (!_active || _textBox is null || _committing)
            return;

        _committing = true;
        try
        {
            var newBody = _textBox.EditedBody;
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

            var decision = editPlan?.CommitRichText(newBody)
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
            var newBody = _cellTextBox.EditedBody;
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

    public bool TryNavigateActiveTableCell(TableCellNavigationDirection direction)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;

        var plan = AvaloniaTableCellEditAdapter.PlanNavigation(_editor, direction);
        if (!plan.IsReady || plan.ShapeId is null || plan.Row is null || plan.Col is null)
            return false;

        CommitCellEdit();
        ActivateCellEdit(plan.ShapeId.Value, plan.Row.Value, plan.Col.Value);
        return true;
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
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            e.Handled = e.Key switch
            {
                Key.B => TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Bold),
                Key.I => TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Italic),
                Key.U => TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Underline),
                _ => false,
            };
    }

    private void OnCellTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelCellEdit();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab &&
            (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            var direction = (e.KeyModifiers & KeyModifiers.Shift) != 0
                ? TableCellNavigationDirection.Previous
                : TableCellNavigationDirection.Next;
            if (TryNavigateActiveTableCell(direction))
                e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            e.Handled = e.Key switch
            {
                Key.B => TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold),
                Key.I => TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Italic),
                Key.U => TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Underline),
                _ => false,
            };
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

    private static void ApplyInitialSelection(AvaloniaRichTextEditor textBox, InCanvasEditorTextSelection selection)
    {
        int textLength = textBox.Text.Length;
        textBox.SelectionStart = Math.Clamp(selection.Start, 0, textLength);
        textBox.SelectionEnd = Math.Clamp(selection.End, 0, textLength);
    }

    private void RefreshCellOverlayRichTextPlan()
    {
        if (!_cellEditActive || _cellTextBox is null)
            return;
        AvaloniaTableCellEditAdapter.ApplyRichTextEditorPlan(
            _cellTextBox,
            _cellTextBox.CurrentPlan());
    }

    private void RefreshShapeOverlayRichTextPlan()
    {
        if (!_active || _textBox is null)
            return;
        AvaloniaInCanvasTextEditAdapter.ApplyRichTextEditorPlan(
            _textBox,
            _textBox.CurrentPlan());
    }
}
