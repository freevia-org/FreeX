using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Drawing; // SlideShapeKind
using FreeP.App.Compositor; // TableCellHitTester, EditingSession
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Manages in-canvas text editing for individual table cells.
///
/// On single-click of a table shape: selects the shape AND sets the active cell
/// (hit-tested from the click point).  Draws a thin highlight rectangle around the
/// active cell on the overlay canvas.
///
/// On double-click of a table cell: opens a <see cref="TextBox"/> positioned over
/// the cell (same approach as <see cref="InCanvasTextEditor"/> for shapes).  On commit
/// (Escape / focus-loss) writes the text back via <c>SetTableCellText</c> on the bus.
///
/// Tab and Shift+Tab navigation use the shared table-cell navigation planner.
/// </summary>
public sealed class InCanvasTableCellEditor
{
    private readonly SlideCanvas    _canvas;
    private readonly EditingSession _editor;
    private readonly Canvas         _overlay;
    private readonly Action<string, string>? _onClipboardWriteFailed;

    // ── Cell-edit state ───────────────────────────────────────────────────────

    private RichTextBox? _cellTextBox;
    // The session replaces `_cellEditPlan = editStart.EditPlanner` plus renderer-owned
    // TableCellEditPlanner.CommitRichText and TableCellEditPlanner.PlanNavigation calls.
    private InCanvasRichTextEditSession? _cellEditSession;
    private bool         _cellEditActive;
    private int          _editRow;
    private int          _editCol;
    private uint         _editShapeId;

    // ── Cell-highlight overlay ────────────────────────────────────────────────

    private Rectangle? _cellHighlight;

    /// <param name="onClipboardWriteFailed">
    /// Invoked with (command, message) when an in-place table-cell Copy/Cut fails to write to
    /// the OS clipboard, so callers can surface it (e.g. to the status bar) instead of the
    /// failure vanishing silently while the user believes the copy succeeded.
    /// </param>
    public InCanvasTableCellEditor(
        SlideCanvas canvas,
        EditingSession editor,
        Canvas overlay,
        Action<string, string>? onClipboardWriteFailed = null)
    {
        _canvas  = canvas  ?? throw new ArgumentNullException(nameof(canvas));
        _editor  = editor  ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _onClipboardWriteFailed = onClipboardWriteFailed;

        _canvas.MouseLeftButtonDown += OnCanvasMouseDown;

        _editor.SelectionChanged        += (_, _) => RefreshHighlight();
        _editor.ActiveTableCellChanged  += (_, _) => RefreshHighlight();
        _editor.Changed                 += RefreshHighlight;
        _editor.CurrentSlideChanged     += (_, _) => { CommitCellEdit(); RefreshHighlight(); };
    }

    // ── Public surface ────────────────────────────────────────────────────────

    public bool IsCellEditActive => _cellEditActive;

    /// <summary>
    /// Activates the cell text editor for the given table shape at (row, col).
    /// Caller should ensure the shape is a table.
    /// </summary>
    public void ActivateCellEdit(uint shapeId, int row, int col)
    {
        if (_cellEditActive && _editShapeId == shapeId && _editRow == row && _editCol == col)
            return;

        CommitCellEdit(); // commit any pending edit first

        var slide = _editor.CurrentSlide;
        if (slide is null) return;

        var editStart = TableCellEditPlanner.BeginEdit(
            _editor.CurrentSlideIndex,
            slide,
            shapeId,
            row,
            col,
            _canvas.CurrentTransform.Core,
            minimumWidth: 30,
            minimumHeight: 18);
        if (!editStart.IsReady || editStart.Cell is null || editStart.Placement is null)
            return;

        var cell = editStart.Cell;

        _editShapeId = shapeId;
        _editRow     = editStart.Row;
        _editCol     = editStart.Col;
        _cellEditActive = true;

        // Use the shared start plan for placement and commit routing.
        _cellEditSession = InCanvasRichTextEditSession.BeginTableCell(editStart);
        var placement = editStart.Placement.Value;

        // Determine a fallback font size from the cell's first run.
        double fallbackPt = InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
            cell.TextBody,
            InCanvasRichTextEditorDefaults.TableCellFallbackFontSizePt);

        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(cell.TextBody, fallbackPt);

        _cellTextBox = new RichTextBox(doc)
        {
            AcceptsReturn         = true,
            Background            = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
            BorderBrush           = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness       = new Thickness(1.5),
            SpellCheck            = { IsEnabled = false },
            IsUndoEnabled         = false,
            MinWidth              = placement.Width,
            MinHeight             = placement.Height,
            Width                 = placement.Width,
            Height                = placement.Height,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        AutomationProperties.SetAutomationId(
            _cellTextBox,
            PresentationSemanticIdentityCatalog.RichTextEditorInputAutomationId);

        Canvas.SetLeft(_cellTextBox, placement.Left);
        Canvas.SetTop (_cellTextBox, placement.Top);
        ApplyPlacementTransform(_cellTextBox, placement);

        _cellTextBox.LostFocus    += (_, _) => CommitCellEdit();
        _cellTextBox.KeyDown      += OnCellTextBoxKeyDown;
        _cellTextBox.PreviewKeyDown += OnCellTextBoxPreviewKeyDown;

        _overlay.Children.Add(_cellTextBox);
        _cellTextBox.Focus();
        ApplyInitialSelection(_cellTextBox, editStart.InitialSelection);

        // Keep active cell in sync.
        _editor.SetActiveTableCell(editStart.Row, editStart.Col);
    }

    /// <summary>Commits the current cell edit (if active) and hides the text box.</summary>
    public void CommitCellEdit()
    {
        if (!_cellEditActive || _cellTextBox is null) return;

        var doc = _cellTextBox.Document;
        _overlay.Children.Remove(_cellTextBox);
        _cellTextBox    = null;
        _cellEditActive = false;
        var editSession = _cellEditSession;
        _cellEditSession = null;

        var slide = _editor.CurrentSlide;
        if (slide is null) return;
        var shape = slide.Shapes.FirstOrDefault(s => s.Id == _editShapeId);
        if (shape?.Table is null) return;

        int row = _editRow;
        int col = _editCol;
        if (row >= shape.Table.Rows.Count) return;
        var cell = shape.Table.Rows[row].Cells.ElementAtOrDefault(col);
        if (cell is null) return;

        // Rebuild the full rich TextBody from the FlowDocument.
        var newBody = TextBodyFlowDocumentConverter.FromFlowDocument(doc, cell.TextBody);
        var decision = editSession?.Commit(newBody)
            ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);

        if (decision.Command is not null)
            _editor.Bus.Execute(decision.Command);
    }

    /// <summary>Cancels the current cell edit without writing back.</summary>
    public void CancelCellEdit()
    {
        if (!_cellEditActive || _cellTextBox is null) return;
        _overlay.Children.Remove(_cellTextBox);
        _cellTextBox = null;
        _cellEditActive = false;
        _ = _cellEditSession?.Cancel();
        _cellEditSession = null;
    }

    // ── Mouse handling ────────────────────────────────────────────────────────

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var xf      = _canvas.CurrentTransform;
        var pt      = e.GetPosition(_canvas);
        var slidePt = xf.ScreenToSlide(pt.X, pt.Y);

        // Is the click on the currently selected table?
        uint? hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        if (!hitId.HasValue) { CommitCellEdit(); return; }

        var shape = ShapeHitTester.FindShape(slide, hitId.Value);
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
        {
            // Clicked a non-table — commit any open cell edit.
            CommitCellEdit();
            return;
        }

        // Single click: set active cell.
        var cellHit = TableCellHitTester.HitTest(shape, slidePt.X, slidePt.Y);
        if (cellHit.HasValue)
        {
            // Only update the active cell if we own this shape (shape is being selected elsewhere
            // by CanvasGestureHandler first, which fires before us because it registered first).
            // We just set the active cell; the selection of the shape itself is handled by
            // CanvasGestureHandler.
            _editor.SetActiveTableCell(cellHit.Value.Row, cellHit.Value.Col);

            // Double-click → activate cell editor.
            if (e.ClickCount >= 2)
            {
                ActivateCellEdit(shape.Id, cellHit.Value.Row, cellHit.Value.Col);
                e.Handled = true;
            }
        }
    }

    // ── Ribbon format application (10A SEAM) ──────────────────────────────────
    // Called by the ribbon routing in FreePRibbonCommands when IsCellEditActive is true.

    /// <summary>True when a cell's RichTextBox is open and focused.</summary>
    public bool IsCellRichEditActive => _cellEditActive && _cellTextBox is not null;

    /// <summary>
    /// Executes a structural table command after committing the native rich-text transaction.
    /// Validation, ordering, and mutation are owned by the shared Presentation dispatcher.
    /// </summary>
    public bool TryExecuteActiveTableStructureAction(PresentationDomainContextActionKind kind)
    {
        if (!IsCellRichEditActive)
            return false;

        return PresentationTableStructureActionDispatcher.TryExecute(
            kind,
            TableCellEditPlanner.PlanSelectedCell(
                _editor.CurrentSlide,
                _editor.SelectedShapeIds,
                _editor.ActiveTableCell),
            _editShapeId,
            CommitCellEdit,
            _editor);
    }

    public bool TryNavigateActiveTableCell(TableCellNavigationDirection direction)
    {
        if (!_cellEditActive || _cellTextBox is null)
            return false;

        var plan = _cellEditSession?.PlanTableCellNavigation(
            _editor.CurrentSlide,
            _editor.SelectedShapeIds,
            _editor.ActiveTableCell,
            direction);
        if (plan is null)
            return false;
        if (!plan.IsReady || plan.ShapeId is null || plan.Row is null || plan.Col is null)
            return false;

        CommitCellEdit();
        ActivateCellEdit(plan.ShapeId.Value, plan.Row.Value, plan.Col.Value);
        return true;
    }

    /// <summary>Toggles bold on the current cell RichTextBox selection.</summary>
    public void ApplyBold() => ExecuteCellFormattingCommand(EditingCommands.ToggleBold);
    /// <summary>Toggles italic on the current cell RichTextBox selection.</summary>
    public void ApplyItalic() => ExecuteCellFormattingCommand(EditingCommands.ToggleItalic);
    /// <summary>Toggles underline on the current cell RichTextBox selection.</summary>
    public void ApplyUnderline() => ExecuteCellFormattingCommand(EditingCommands.ToggleUnderline);
    /// <summary>Toggles strikethrough on the current cell RichTextBox selection.</summary>
    public void ApplyStrikethrough()
    {
        if (_cellTextBox is null) return;
        ApplyWithPreservedSelection(() =>
        {
            var current = _cellTextBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            var decorations = new TextDecorationCollection();
            var hasStrikethrough = false;
            if (current is TextDecorationCollection existing)
            {
                foreach (var decoration in existing)
                {
                    if (decoration.Location == TextDecorationLocation.Strikethrough)
                        hasStrikethrough = true;
                    else
                        decorations.Add(decoration);
                }
            }

            if (!hasStrikethrough)
                decorations.Add(TextDecorations.Strikethrough[0]);

            _cellTextBox.Selection.ApplyPropertyValue(
                Inline.TextDecorationsProperty,
                decorations);
        });
    }
    /// <summary>Toggles superscript on the current cell RichTextBox selection.</summary>
    public void ApplySuperscript() => ApplyBaseline(BaselineAlignment.Superscript);
    /// <summary>Toggles subscript on the current cell RichTextBox selection.</summary>
    public void ApplySubscript() => ApplyBaseline(BaselineAlignment.Subscript);

    /// <summary>Sets font family on the current cell RichTextBox selection.</summary>
    public void ApplyFont(string? fontFamily)
    {
        if (_cellTextBox is null || string.IsNullOrEmpty(fontFamily)) return;
        ApplyWithPreservedSelection(() =>
            _cellTextBox.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(fontFamily)));
    }

    /// <summary>Sets font size (pt) on the current cell RichTextBox selection.</summary>
    public void ApplyFontSize(double? sizePt)
    {
        if (_cellTextBox is null || sizePt is null) return;
        ApplyWithPreservedSelection(() =>
            _cellTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, sizePt.Value * (96.0 / 72.0)));
    }

    /// <summary>Sets text color on the current cell RichTextBox selection.</summary>
    public void ApplyColor(ThemeAwareColor? color)
    {
        if (_cellTextBox is null || color is null) return;
        var wpfColor = TextBodyFlowDocumentConverter.ResolveModelColor(color);
        if (wpfColor is null) return;
        ApplyWithPreservedSelection(() =>
            _cellTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(wpfColor.Value)));
    }

    public bool TryApplyActiveTableCellParagraphAlignment(TextAlign alignment) =>
        ApplyCellParagraphMutation((body, selection) =>
            InCanvasTextEditPlanner.ApplyParagraphAlignment(body, alignment, selection));

    public bool TryApplyActiveTableCellParagraphBulletToggle() =>
        ApplyCellParagraphMutation((body, selection) =>
            InCanvasTextEditPlanner.ApplyParagraphBulletToggle(body, selection));

    public bool TryApplyActiveTableCellParagraphNumberingToggle() =>
        ApplyCellParagraphMutation((body, selection) =>
            InCanvasTextEditPlanner.ApplyParagraphNumberingToggle(body, selection));

    public bool TryApplyActiveTableCellParagraphListPreset(TableCellListPresetDescriptor preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyCellParagraphMutation((body, selection) =>
            InCanvasTextEditPlanner.ApplyParagraphListPreset(body, selection, preset));
    }

    public bool TryApplyActiveTableCellParagraphPictureBullet(PresentationPictureBulletPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.IsValid)
            return false;

        return ApplyCellParagraphMutation((body, selection) =>
            InCanvasTextEditPlanner.ApplyParagraphPictureBullet(
                body,
                selection,
                PresentationPictureBulletAuthoringPlanner.CreateImagePart(payload)));
    }

    public bool TryApplyActiveTableCellParagraphIndent() =>
        ApplyCellParagraphMutation((body, selection) =>
            InCanvasTextEditPlanner.ApplyParagraphIndent(body, increase: true, selection));

    public bool TryApplyActiveTableCellParagraphOutdent() =>
        ApplyCellParagraphMutation((body, selection) =>
            InCanvasTextEditPlanner.ApplyParagraphIndent(body, increase: false, selection));

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

    private bool TryApplyActiveTableCellCommand(Func<EditingSession, bool> apply)
    {
        if (!IsCellRichEditActive)
            return false;

        return PresentationTableCellOwnedActionDispatcher.TryExecute(
            TableCellEditPlanner.PlanSelectedCell(
                _editor.CurrentSlide,
                _editor.SelectedShapeIds,
                _editor.ActiveTableCell),
            _editShapeId,
            CommitCellEdit,
            () => apply(_editor));
    }

    /// <summary>
    /// Adapts the live WPF selection to the renderer-neutral paragraph mutation planner. The
    /// native document is rehydrated only after the shared model operation has run, preserving
    /// the user's selected subrange and keeping paragraph/list semantics identical to Avalonia.
    /// </summary>
    private bool ApplyCellParagraphMutation(
        Func<TextBody, (int Start, int End)?, TextBody> mutate)
    {
        if (!IsCellRichEditActive || _cellTextBox is null || TryGetCurrentCellTextBody() is not { } baseBody)
            return false;

        var current = TextBodyFlowDocumentConverter.FromFlowDocument(_cellTextBox.Document, baseBody);
        (int Start, int End)? selection = CurrentLogicalSelection();
        var updated = mutate(current, selection);
        int start = selection?.Start ?? 0;
        int end = selection?.End ?? InCanvasTextEditPlanner.ExtractPlainText(updated).Length;

        double fallbackPt = InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
            updated,
            InCanvasRichTextEditorDefaults.TableCellFallbackFontSizePt);
        _cellTextBox.Document = TextBodyFlowDocumentConverter.ToFlowDocument(updated, fallbackPt);

        var startPointer = TextBodyFlowDocumentConverter.TextPointerAtLogicalOffset(_cellTextBox.Document, start);
        var endPointer = TextBodyFlowDocumentConverter.TextPointerAtLogicalOffset(_cellTextBox.Document, end);
        if (startPointer is not null && endPointer is not null)
            _cellTextBox.Selection.Select(startPointer, endPointer);
        _cellTextBox.Focus();
        return true;
    }

    private (int Start, int End)? CurrentLogicalSelection()
    {
        if (_cellTextBox is null)
            return null;

        return (
            TextBodyFlowDocumentConverter.LogicalOffsetAt(
                _cellTextBox.Document,
                _cellTextBox.Selection.Start),
            TextBodyFlowDocumentConverter.LogicalOffsetAt(
                _cellTextBox.Document,
                _cellTextBox.Selection.End));
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    private void ExecuteCellFormattingCommand(RoutedCommand command)
    {
        if (_cellTextBox is null)
            return;

        ApplyWithPreservedSelection(() => command.Execute(null, _cellTextBox));
    }

    private void ApplyBaseline(BaselineAlignment alignment)
    {
        if (_cellTextBox is null)
            return;

        ApplyWithPreservedSelection(() =>
        {
            var current = _cellTextBox.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty);
            var next = current is BaselineAlignment currentAlignment && currentAlignment == alignment
                ? BaselineAlignment.Baseline
                : alignment;
            _cellTextBox.Selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, next);
        });
    }

    private void ApplyWithPreservedSelection(Action apply)
    {
        if (_cellTextBox is null)
            return;

        var selectionStart = _cellTextBox.Selection.Start;
        var selectionEnd = _cellTextBox.Selection.End;
        apply();
        _cellTextBox.Selection.Select(selectionStart, selectionEnd);
        _cellTextBox.Focus();
    }

    private void OnCellTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        var plan = TableCellEditPlanner.PlanKeyboard(
            ToTableCellEditKeyboardKey(e.Key),
            ToTableCellEditKeyboardModifiers(e.KeyboardDevice.Modifiers));

        if (plan.Action == TableCellEditKeyboardAction.Cancel)
        {
            CancelCellEdit();
            e.Handled = true;
        }
    }

    private async void OnCellTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0 &&
            _cellTextBox is not null &&
            TryGetCurrentCellTextBody() is { } currentBody)
        {
            if (e.Key is Key.C or Key.X or Key.V)
            {
                var result = await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(
                    e,
                    _cellTextBox,
                    currentBody);
                if (e.Key is Key.C or Key.X &&
                    !result.Handled &&
                    result.FailureMessage is { } failureMessage)
                    _onClipboardWriteFailed?.Invoke(
                        PresentationShellTextCatalog.Resolve(
                            e.Key == Key.X
                                ? PresentationShellTextCatalog.EditCutCommand
                                : PresentationShellTextCatalog.EditCopyCommand),
                        failureMessage);
                return;
            }
        }

        if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0 &&
            e.Key is Key.OemPlus or Key.Add)
        {
            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
                ApplySuperscript();
            else
                ApplySubscript();
            e.Handled = true;
            return;
        }

        var plan = TableCellEditPlanner.PlanKeyboard(
            ToTableCellEditKeyboardKey(e.Key),
            ToTableCellEditKeyboardModifiers(e.KeyboardDevice.Modifiers));

        if (plan.Action == TableCellEditKeyboardAction.Navigate &&
            plan.NavigationDirection is { } direction)
        {
            if (TryNavigateActiveTableCell(direction))
                e.Handled = true;
            return;
        }

        if (plan.Action == TableCellEditKeyboardAction.ToggleTextFormat &&
            plan.TextFormatKind is { } kind)
        {
            switch (kind)
            {
                case TableCellTextFormatKind.Bold:
                    ApplyBold();
                    break;
                case TableCellTextFormatKind.Italic:
                    ApplyItalic();
                    break;
                case TableCellTextFormatKind.Underline:
                    ApplyUnderline();
                    break;
                case TableCellTextFormatKind.Strikethrough:
                    ApplyStrikethrough();
                    break;
                case TableCellTextFormatKind.Superscript:
                    ApplySuperscript();
                    break;
                case TableCellTextFormatKind.Subscript:
                    ApplySubscript();
                    break;
            }
            e.Handled = true;
        }
    }

    private TextBody? TryGetCurrentCellTextBody()
    {
        var shape = _editor.CurrentSlide?.Shapes.FirstOrDefault(s => s.Id == _editShapeId);
        return shape?.Table?.Rows.ElementAtOrDefault(_editRow)?.Cells.ElementAtOrDefault(_editCol)?.TextBody;
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
        ModifierKeys modifiers)
    {
        var result = TableCellEditKeyboardModifiers.None;
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= TableCellEditKeyboardModifiers.Control;
        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= TableCellEditKeyboardModifiers.Shift;
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= TableCellEditKeyboardModifiers.Alt;
        if ((modifiers & ModifierKeys.Windows) != 0)
            result |= TableCellEditKeyboardModifiers.Platform;
        return result;
    }

    // ── Cell highlight overlay ─────────────────────────────────────────────────

    private static void ApplyInitialSelection(RichTextBox textBox, InCanvasEditorTextSelection selection)
    {
        if (selection.IsCollapsed)
        {
            textBox.CaretPosition = textBox.Document.ContentStart;
            return;
        }

        textBox.SelectAll();
    }

    private void RefreshHighlight()
    {
        // Remove old highlight.
        if (_cellHighlight is not null)
        {
            _overlay.Children.Remove(_cellHighlight);
            _cellHighlight = null;
        }

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;
        var cellState = TableCellEditPlanner.PlanSelectedCell(
            slide,
            _editor.SelectedShapeIds,
            _editor.ActiveTableCell);
        if (!cellState.CanEditText || cellState.ShapeId is null || cellState.Row is null || cellState.Col is null)
            return;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == cellState.ShapeId.Value);
        if (shape?.Kind != SlideShapeKind.Table) return;

        var cellRect = TableCellHitTester.GetCellRect(shape, cellState.Row.Value, cellState.Col.Value);
        if (cellRect is null) return;

        var placement = TableCellEditPlanner.PlanCellEditorPlacement(
            shape,
            cellRect.Value,
            _canvas.CurrentTransform.Core,
            minimumWidth: 0,
            minimumHeight: 0);

        _cellHighlight = new Rectangle
        {
            Width           = Math.Max(1, placement.Width),
            Height          = Math.Max(1, placement.Height),
            Stroke          = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            StrokeThickness = 2.0,
            Fill            = new SolidColorBrush(Color.FromArgb(0x18, 0x21, 0x96, 0xF3)),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(_cellHighlight, placement.Left);
        Canvas.SetTop (_cellHighlight, placement.Top);
        ApplyPlacementTransform(_cellHighlight, placement);
        _overlay.Children.Add(_cellHighlight);
    }

    private static void ApplyPlacementTransform(
        FrameworkElement editor,
        InCanvasEditorPlacement placement)
    {
        if (!placement.HasTransform)
            return;

        double originX = placement.EffectiveTransformOriginX;
        double originY = placement.EffectiveTransformOriginY;
        var transform = new TransformGroup();
        if (placement.FlipHorizontal || placement.FlipVertical)
        {
            transform.Children.Add(new ScaleTransform(
                placement.FlipHorizontal ? -1 : 1,
                placement.FlipVertical ? -1 : 1,
                originX,
                originY));
        }

        if (Math.Abs(placement.RotationDegrees) > 0.0001)
        {
            transform.Children.Add(new RotateTransform(
                placement.RotationDegrees,
                originX,
                originY));
        }

        editor.RenderTransform = transform;
    }

}
