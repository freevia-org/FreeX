using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Manages rich in-canvas shape and table-cell editing for the Avalonia <see cref="SlideCanvas"/>.
/// </summary>
public sealed class AvaloniaInCanvasTextEditor : IDisposable
{
    private const double WpfRasterAlignmentOffsetX = -0.25;

    private readonly SlideCanvas _canvas;
    private readonly EditingSession _editor;
    private readonly Panel _overlay;
    private readonly Func<AvaloniaInlineOleHostRequest, Action<byte[]>, Control?>? _inlineOleHostFactory;

    private AvaloniaRichTextEditor? _textBox;
    private Control? _activeInlineOleHost;
    private InCanvasTextEditPlanner? _editPlan;
    private uint _editingShapeId;
    private bool _active;
    private bool _committing;
    private bool _canceling;

    private AvaloniaRichTextEditor? _cellTextBox;
    private Border? _cellHighlight;
    private InCanvasTableCellTextEditPlanner? _cellEditPlan;
    private uint _editingTableShapeId;
    private int _editingCellRow;
    private int _editingCellCol;
    private bool _cellEditActive;
    private bool _cellClosing;
    private bool _disposed;

    /// <summary>True while a shape's text is being edited in the overlay TextBox.</summary>
    public bool IsActive => _active;

    /// <summary>The id of the shape currently being edited, or 0 if not active.</summary>
    public uint ActiveShapeId => _editingShapeId;

    /// <summary>True when either in-canvas text editor owns keyboard focus.</summary>
    public bool IsEditorFocused =>
        _textBox?.InputBox.IsFocused == true ||
        _cellTextBox?.InputBox.IsFocused == true;

    /// <summary>The text selected by the active editor.</summary>
    public string SelectedText => _textBox is null
        ? string.Empty
        : _textBox.Text[
            Math.Min(_textBox.SelectionStart, _textBox.SelectionEnd)..
            Math.Max(_textBox.SelectionStart, _textBox.SelectionEnd)];

    /// <summary>The production Avalonia visual used for rich-text selection evidence.</summary>
    public Visual? ActiveRichTextVisual => _textBox;

    public bool TryGetSelectedShapeRunHyperlink(out Hyperlink? hyperlink)
    {
        hyperlink = null;
        if (!_active || _textBox is null || _textBox.SelectionStart == _textBox.SelectionEnd)
            return false;

        hyperlink = _textBox.SelectedRunHyperlink();
        return true;
    }

    public bool TryApplySelectedShapeRunHyperlink(Hyperlink? hyperlink)
    {
        if (!_active || _textBox is null || _textBox.SelectionStart == _textBox.SelectionEnd)
            return false;

        bool changed = _textBox.ApplyHyperlink(hyperlink);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    /// <summary>Selects a logical model-text range in the active editor.</summary>
    public bool TrySelectTextRange(int start, int end)
    {
        if (_textBox is null || start < 0 || end < start || end > _textBox.Text.Length)
            return false;

        _textBox.SelectionStart = start;
        _textBox.SelectionEnd = end;
        _textBox.FocusEditor();
        return true;
    }

    public bool TryActivateInlineOleObject() =>
        _active
        && _textBox is not null
        && _textBox.TryActivateInlineOleObject(
            TryActivateInlineOleAt);

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

    public bool TryApplyActiveShapeParagraphAlignment(TextAlign alignment)
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ApplyParagraphAlignment(alignment);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeParagraphBulletToggle()
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ToggleParagraphBullets();
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeParagraphNumberingToggle()
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ToggleParagraphNumbering();
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeParagraphListPreset(TableCellListPresetDescriptor preset)
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ApplyParagraphListPreset(preset);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeParagraphPictureBullet(PresentationPictureBulletPayload payload)
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ApplyParagraphPictureBullet(payload);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeParagraphIndent()
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ApplyParagraphIndent(increase: true);
        if (changed)
            RefreshShapeOverlayRichTextPlan();
        return changed;
    }

    public bool TryApplyActiveShapeParagraphOutdent()
    {
        if (!_active || _textBox is null)
            return false;
        bool changed = _textBox.ApplyParagraphIndent(increase: false);
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

    /// <summary>
    /// Commits the child rich-text edit before applying a cell-owned formatting command.
    /// Cell fill, geometry, and direction belong to the table transaction rather than the
    /// inline text editor, so they must not race a pending child-text commit.
    /// </summary>
    public bool TryApplyActiveTableCellTextVerticalType(TextVerticalType verticalType) =>
        TryApplyActiveTableCellCommand(editor =>
            editor.TryApplyActiveTableCellTextVerticalType(verticalType));

    public bool TryApplyActiveTableCellFill(ThemeAwareColor? color) =>
        TryApplyActiveTableCellCommand(editor =>
            editor.TryApplyActiveTableCellFill(color));

    public bool TryApplyActiveTableCellAnchor(TableCellAnchor? anchor) =>
        TryApplyActiveTableCellCommand(editor =>
            editor.TryApplyActiveTableCellAnchor(anchor));

    public bool TryApplyActiveTableCellBorder(
        TableCellBorderSide side,
        ShapeOutline? outline) =>
        TryApplyActiveTableCellCommand(editor =>
            editor.TryApplyActiveTableCellBorder(side, outline));

    public bool TryApplyActiveTableCellInset(TableCellInsetSide side, double? insetPt) =>
        TryApplyActiveTableCellCommand(editor =>
            editor.TryApplyActiveTableCellInset(side, insetPt));

    public bool TryApplyActiveTableRowHeight(long heightEmu) =>
        TryApplyActiveTableCellCommand(editor =>
            editor.TryApplyActiveTableRowHeight(heightEmu));

    /// <summary>
    /// Inserts a row above the active inline table cell after committing the child rich-text
    /// transaction through the shared command bus.
    /// </summary>
    public bool TryInsertActiveTableRowAbove() =>
        TryApplyActiveTableCommand(
            state => state.CanInsertRow,
            editor =>
            {
                editor.InsertRowAbove();
                return true;
            });

    /// <summary>Inserts a row below the active inline table cell.</summary>
    public bool TryInsertActiveTableRowBelow() =>
        TryApplyActiveTableCommand(
            state => state.CanInsertRow,
            editor =>
            {
                editor.InsertRowBelow();
                return true;
            });

    /// <summary>Inserts a column to the left of the active inline table cell.</summary>
    public bool TryInsertActiveTableColumnLeft() =>
        TryApplyActiveTableCommand(
            state => state.CanInsertColumn,
            editor =>
            {
                editor.InsertColumnLeft();
                return true;
            });

    /// <summary>Inserts a column to the right of the active inline table cell.</summary>
    public bool TryInsertActiveTableColumnRight() =>
        TryApplyActiveTableCommand(
            state => state.CanInsertColumn,
            editor =>
            {
                editor.InsertColumnRight();
                return true;
            });

    /// <summary>Deletes the active inline table cell's row.</summary>
    public bool TryDeleteActiveTableRow() =>
        TryApplyActiveTableCommand(
            state => state.CanDeleteRow,
            editor =>
            {
                editor.DeleteRow();
                return true;
            });

    /// <summary>Deletes the active inline table cell's column.</summary>
    public bool TryDeleteActiveTableColumn() =>
        TryApplyActiveTableCommand(
            state => state.CanDeleteColumn,
            editor =>
            {
                editor.DeleteColumn();
                return true;
            });

    /// <summary>
    /// Merges the active inline table cell with its right neighbor, or the cell below at a row
    /// edge, using the shared merge transaction.
    /// </summary>
    public bool TryMergeActiveTableCell() =>
        TryApplyActiveTableCommand(
            state => state.CanMergeWithRight || state.CanMergeWithBelow,
            editor => editor.TryMergeActiveTableCell());

    /// <summary>Splits the active inline table cell when it is a merged anchor.</summary>
    public bool TrySplitActiveTableCell() =>
        TryApplyActiveTableCommand(
            state => state.CanSplitCell,
            editor =>
            {
                editor.SplitSelectedCell();
                return true;
            });

    private bool TryApplyActiveTableCommand(
        Func<TableCellEditState, bool> canApply,
        Func<EditingSession, bool> apply)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;

        var state = AvaloniaTableCellEditAdapter.PlanSelectedCell(_editor);
        if (!state.HasActiveCell || state.ShapeId != _editingTableShapeId || !canApply(state))
            return false;

        CommitCellEdit();
        return apply(_editor);
    }

    private bool TryApplyActiveTableCellCommand(Func<EditingSession, bool> apply)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;

        var state = AvaloniaTableCellEditAdapter.PlanSelectedCell(_editor);
        if (!state.HasActiveCell || state.ShapeId != _editingTableShapeId)
            return false;

        CommitCellEdit();
        return apply(_editor);
    }

    public AvaloniaInCanvasTextEditor(
        SlideCanvas canvas,
        EditingSession editor,
        Panel overlay,
        Func<AvaloniaInlineOleHostRequest, Action<byte[]>, Control?>? inlineOleHostFactory = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _inlineOleHostFactory = inlineOleHostFactory;

        _canvas.PointerPressed += OnCanvasPointerPressed;

        _editor.SelectionChanged += OnEditorSelectionChanged;
        _editor.ActiveTableCellChanged += OnEditorActiveTableCellChanged;
        _editor.Changed += RefreshTableCellHighlight;
        _editor.CurrentSlideChanged += OnEditorCurrentSlideChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _canvas.PointerPressed -= OnCanvasPointerPressed;
        _editor.SelectionChanged -= OnEditorSelectionChanged;
        _editor.ActiveTableCellChanged -= OnEditorActiveTableCellChanged;
        _editor.Changed -= RefreshTableCellHighlight;
        _editor.CurrentSlideChanged -= OnEditorCurrentSlideChanged;

        if (_textBox is not null)
        {
            _textBox.InputBox.LostFocus -= OnTextBoxLostFocus;
            _textBox.InputBox.KeyDown -= OnTextBoxKeyDown;
        }
        if (_cellTextBox is not null)
        {
            _cellTextBox.InputBox.LostFocus -= OnCellTextBoxLostFocus;
            _cellTextBox.InputBox.KeyDown -= OnCellTextBoxKeyDown;
        }

        CancelCellEdit();
        Cancel();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e) => RefreshTableCellHighlight();
    private void OnEditorActiveTableCellChanged(object? sender, EventArgs e) => RefreshTableCellHighlight();
    private void OnEditorCurrentSlideChanged(object? sender, EventArgs e)
    {
        Commit();
        CommitCellEdit();
        RefreshTableCellHighlight();
    }

    private bool TryActivateInlineOleAt(int logicalPosition)
    {
        if (_textBox is not null
            && _inlineOleHostFactory is not null
            && _textBox.TryGetInlineOleHit(logicalPosition, out var request))
        {
            CloseInlineOleHost();
            var host = _inlineOleHostFactory(
                request,
                updatedBytes => _textBox?.UpdateInlineOleObjectAt(
                    request.LogicalPosition,
                    updatedBytes));
            if (host is not null)
            {
                host.Width = request.Bounds.Width;
                host.Height = request.Bounds.Height;
                host.HorizontalAlignment = HorizontalAlignment.Left;
                host.VerticalAlignment = VerticalAlignment.Top;
                host.Margin = new Thickness(request.Bounds.Left, request.Bounds.Top, 0, 0);
                host.ZIndex = 20;
                _textBox.Children.Add(host);
                _activeInlineOleHost = host;
                return true;
            }
        }

        return _editor.TryActivateInlineOleObject(
            _editingShapeId,
            logicalPosition,
            updatedBytes => _textBox?.UpdateInlineOleObjectAt(logicalPosition, updatedBytes));
    }

    private void CloseInlineOleHost()
    {
        if (_activeInlineOleHost is null)
            return;

        if (_textBox is not null)
            _textBox.Children.Remove(_activeInlineOleHost);
        (_activeInlineOleHost as IDisposable)?.Dispose();
        _activeInlineOleHost = null;
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

        var placement = startPlan.Placement.Value;

        var shapeFallbackFontSizePt = InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
            startPlan.OriginalBody,
            InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt);
        _textBox = new AvaloniaRichTextEditor(
            startPlan.OriginalBody,
            backgroundAlpha: 0xCC,
            fallbackFontSizePt: shapeFallbackFontSizePt)
        {
            MinWidth = placement.Width,
            MinHeight = placement.Height,
            Width = placement.Width,
            Height = placement.Height,
        };
        AvaloniaInCanvasTextEditAdapter.ApplyRichTextEditorPlan(_textBox, startPlan.RichTextPlan);

        Canvas.SetLeft(_textBox, placement.Left);
        Canvas.SetTop(_textBox, placement.Top);
        ApplyPlacementTransform(_textBox, placement);

        _textBox.InputBox.LostFocus += OnTextBoxLostFocus;
        _textBox.InputBox.KeyDown += OnTextBoxKeyDown;

        _overlay.Children.Add(_textBox);
        _canvas.ActiveTextEditShapeId = shapeId;
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

        var cellFallbackFontSizePt = InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
            startPlan.OriginalBody,
            InCanvasRichTextEditorDefaults.TableCellFallbackFontSizePt);
        _cellTextBox = new AvaloniaRichTextEditor(
            startPlan.OriginalBody,
            backgroundAlpha: 0xEE,
            fallbackFontSizePt: cellFallbackFontSizePt)
        {
            MinWidth = placement.Width,
            MinHeight = placement.Height,
            Width = placement.Width,
            Height = placement.Height,
        };
        AvaloniaTableCellEditAdapter.ApplyRichTextEditorPlan(_cellTextBox, startPlan.RichTextPlan);

        Canvas.SetLeft(_cellTextBox, placement.Left);
        Canvas.SetTop(_cellTextBox, placement.Top);
        ApplyPlacementTransform(_cellTextBox, placement);

        _cellTextBox.InputBox.LostFocus += OnCellTextBoxLostFocus;
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
        if (_committing)
            return;

        if (!_active || _textBox is null)
        {
            _canvas.ActiveTextEditShapeId = null;
            return;
        }

        _committing = true;
        try
        {
            var newBody = _textBox.EditedBody;
            var editPlan = _editPlan;

            CloseInlineOleHost();
            _overlay.Children.Remove(_textBox);
            _textBox = null;
            _active = false;
            _canvas.ActiveTextEditShapeId = null;
            _editPlan = null;
            UpdateOverlayState();

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
        {
            _canvas.ActiveTextEditShapeId = null;
            return;
        }

        _canceling = true;
        try
        {
            CloseInlineOleHost();
            _overlay.Children.Remove(_textBox);
            _textBox = null;
            _active = false;
            _canvas.ActiveTextEditShapeId = null;
            _ = _editPlan?.Cancel();
            _editPlan = null;
            UpdateOverlayState();
        }
        finally
        {
            _canceling = false;
        }
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

        var shape = ShapeHitTester.FindShape(slide, hitId.Value);
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

        var shape = ShapeHitTester.FindShape(slide, hitId.Value);
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
                Key.OemPlus or Key.Add => TryApplyActiveShapeTextFormat(
                    (e.KeyModifiers & KeyModifiers.Shift) != 0
                        ? TableCellTextFormatKind.Superscript
                        : TableCellTextFormatKind.Subscript),
                _ => false,
            };
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!_canceling)
            Commit();
    }
    private void OnCellTextBoxLostFocus(object? sender, RoutedEventArgs e) => CommitCellEdit();

    private static void ApplyPlacementTransform(
        Control editor,
        InCanvasEditorPlacement placement)
    {
        if (!placement.HasTransform)
        {
            // Avalonia snaps Canvas placement to the current device scale while WPF
            // preserves the shared fractional origin. Keep the layout box unchanged
            // and align only the rendered left edge to the WPF raster.
            editor.RenderTransform = new TranslateTransform(WpfRasterAlignmentOffsetX, 0);
            return;
        }

        double originX = placement.EffectiveTransformOriginX;
        double originY = placement.EffectiveTransformOriginY;
        double scaleX = placement.FlipHorizontal ? -1 : 1;
        double scaleY = placement.FlipVertical ? -1 : 1;
        double radians = placement.RotationDegrees * Math.PI / 180.0;
        var matrix = Matrix.CreateScale(scaleX, scaleY);
        if (Math.Abs(placement.RotationDegrees) > 0.0001)
            matrix *= Matrix.CreateRotation(radians);

        editor.RenderTransformOrigin = new RelativePoint(
            originX / Math.Max(1, placement.Width),
            originY / Math.Max(1, placement.Height),
            RelativeUnit.Relative);
        editor.RenderTransform = new MatrixTransform(matrix);
    }

    private void OnCellTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0 &&
            e.Key is Key.OemPlus or Key.Add)
        {
            e.Handled = TryApplyActiveTableCellTextFormat(
                (e.KeyModifiers & KeyModifiers.Shift) != 0
                    ? TableCellTextFormatKind.Superscript
                    : TableCellTextFormatKind.Subscript);
            return;
        }

        var plan = TableCellEditPlanner.PlanKeyboard(
            ToTableCellEditKeyboardKey(e.Key),
            ToTableCellEditKeyboardModifiers(e.KeyModifiers));

        switch (plan.Action)
        {
            case TableCellEditKeyboardAction.Cancel:
                CancelCellEdit();
                e.Handled = true;
                break;
            case TableCellEditKeyboardAction.Navigate when plan.NavigationDirection is { } direction:
                if (TryNavigateActiveTableCell(direction))
                    e.Handled = true;
                break;
            case TableCellEditKeyboardAction.ToggleTextFormat when plan.TextFormatKind is { } kind:
                e.Handled = TryApplyActiveTableCellTextFormat(kind);
                break;
        }
    }

    private static TableCellEditKeyboardKey ToTableCellEditKeyboardKey(Key key) => key switch
    {
        Key.Escape => TableCellEditKeyboardKey.Escape,
        Key.Tab => TableCellEditKeyboardKey.Tab,
        Key.B => TableCellEditKeyboardKey.B,
        Key.I => TableCellEditKeyboardKey.I,
        Key.U => TableCellEditKeyboardKey.U,
        _ => TableCellEditKeyboardKey.Other,
    };

    private static TableCellEditKeyboardModifiers ToTableCellEditKeyboardModifiers(
        KeyModifiers modifiers)
    {
        var result = TableCellEditKeyboardModifiers.None;
        if ((modifiers & KeyModifiers.Control) != 0)
            result |= TableCellEditKeyboardModifiers.Control;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= TableCellEditKeyboardModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= TableCellEditKeyboardModifiers.Alt;
        if ((modifiers & KeyModifiers.Meta) != 0)
            result |= TableCellEditKeyboardModifiers.Platform;
        return result;
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

        var shape = _editor.CurrentSlide is { } currentSlide
            ? ShapeHitTester.FindShape(currentSlide, state.ShapeId.Value)
            : null;
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

        var placement = TableCellEditPlanner.PlanCellEditorPlacement(
            shape,
            cellRect.Value,
            _canvas.CurrentTransform,
            minimumWidth: 0,
            minimumHeight: 0);
        _cellHighlight = new Border
        {
            Width = Math.Max(1, placement.Width),
            Height = Math.Max(1, placement.Height),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x21, 0x96, 0xF3)),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(_cellHighlight, placement.Left);
        Canvas.SetTop(_cellHighlight, placement.Top);
        ApplyPlacementTransform(_cellHighlight, placement);
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
