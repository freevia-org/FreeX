using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using ModelHyperlink = FreeP.Core.Model.Hyperlink;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Manages an in-canvas rich-text editing overlay for <see cref="SlideCanvas"/>.
/// </summary>
public sealed class InCanvasTextEditor : IDisposable
{
    private readonly SlideCanvas _canvas;
    private readonly EditingSession _editor;
    private readonly Canvas _overlay;
    private readonly Action<string, string>? _onClipboardWriteFailed;

    private RichTextBox? _richBox;
    private InCanvasRichTextEditSession? _editSession;
    private TextBody? _shapeParagraphBody;
    // Layout/master inherited-run-style context for the shape being edited, resolved once at
    // BeginShapeEdit (same chain SlideCompositor uses for the static render) and reused for
    // every re-render of this edit session, so the preview never drifts back to a shape-only
    // resolution partway through a single edit (indent/bullet toggle, paste, ...).
    private TextBody? _inheritedLayoutBody;
    private MasterTextStyles? _inheritedMasterTextStyles;
    private SlideCompositor.TextStyleCategory _inheritedStyleCategory;
    private uint _editingShapeId;
    private bool _active;
    private bool _canceling;

    /// <param name="onClipboardWriteFailed">
    /// Invoked with (command, message) when an in-place Copy/Cut fails to write to the OS
    /// clipboard, so callers can surface it (e.g. to the status bar) instead of the failure
    /// vanishing silently while the user believes the copy succeeded.
    /// </param>
    public InCanvasTextEditor(
        SlideCanvas canvas,
        EditingSession editor,
        Canvas overlay,
        Action<string, string>? onClipboardWriteFailed = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _onClipboardWriteFailed = onClipboardWriteFailed;

        _canvas.MouseLeftButtonDown += OnCanvasMouseDown;
        _editor.CurrentSlideChanged += OnEditorCurrentSlideChanged;
    }

    /// <summary>True while a shape's text is being edited in the RichTextBox overlay.</summary>
    public bool IsActive => _active;

    /// <summary>The id of the shape currently being edited, or 0 if not active.</summary>
    public uint ActiveShapeId => _editingShapeId;

    /// <summary>True when the active editor owns keyboard focus.</summary>
    public bool IsEditorFocused => _richBox?.IsKeyboardFocusWithin == true;

    /// <summary>The text selected by the active editor.</summary>
    public string SelectedText => _richBox?.Selection.Text ?? string.Empty;

    public bool TryGetSelectedShapeRunHyperlink(out ModelHyperlink? hyperlink)
    {
        hyperlink = null;
        if (!_active || _richBox is null || _richBox.Selection.IsEmpty)
            return false;

        var selection = CurrentSelection();
        if (_editSession is null || selection is null)
            return false;

        var body = TextBodyFlowDocumentConverter.FromFlowDocument(
            _richBox.Document,
            _shapeParagraphBody);
        _editSession.SynchronizeBody(body);
        hyperlink = _editSession.GetSelectedRunHyperlink(
            new InCanvasEditorTextSelection(selection.Value.Start, selection.Value.End));
        return true;
    }

    public bool TryApplySelectedShapeRunHyperlink(ModelHyperlink? hyperlink)
    {
        if (!_active || _richBox is null || _richBox.Selection.IsEmpty)
            return false;

        return ApplyShapeParagraphMutation((session, selection) =>
        {
            if (selection is null)
                return false;
            return session.ApplyHyperlink(
                hyperlink,
                new InCanvasEditorTextSelection(selection.Value.Start, selection.Value.End));
        });
    }

    /// <summary>Selects a logical model-text range in the active editor.</summary>
    public bool TrySelectTextRange(int start, int end)
    {
        if (_richBox is null || start < 0 || end < start)
            return false;

        var startPointer = TextBodyFlowDocumentConverter.TextPointerAtLogicalOffset(_richBox.Document, start);
        var endPointer = TextBodyFlowDocumentConverter.TextPointerAtLogicalOffset(_richBox.Document, end);
        if (startPointer is null || endPointer is null)
            return false;

        _richBox.Selection.Select(startPointer, endPointer);
        _richBox.Focus();
        return true;
    }

    /// <summary>
    /// Activates the inline embedded object at the current caret/selection position. The
    /// existing external activation session writes edited bytes back into the live text model.
    /// </summary>
    public bool TryActivateInlineOleObject()
    {
        if (!_active || _richBox is null)
            return false;

        int position = LogicalOffsetAt(
            _richBox.Document,
            _richBox.Selection.IsEmpty ? _richBox.CaretPosition : _richBox.Selection.Start);

        return TryActivateInlineOleAt(position)
            || (position > 0 && TryActivateInlineOleAt(position - 1));
    }

    private bool TryActivateInlineOleAt(int logicalPosition) =>
        _editor.TryActivateInlineOleObject(
            _editingShapeId,
            logicalPosition,
            updatedBytes =>
            {
                if (_shapeParagraphBody is not null
                    && InCanvasRichTextEditBuffer.FindInlineOleObjectAt(
                        _shapeParagraphBody,
                        logicalPosition,
                        out var snapshot)
                    && snapshot is not null)
                {
                    snapshot.EmbeddedBytes = updatedBytes.ToArray();
                }
            });

    /// <summary>Activates the rich-text editor for the given shape.</summary>
    public void Activate(uint shapeId)
    {
        if (_active && _editingShapeId == shapeId)
            return;

        Commit();

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
            return;

        var startPlan = InCanvasTextEditPlanner.BeginShapeEdit(
            _editor.CurrentSlideIndex,
            _editor.Presentation,
            slide,
            shapeId,
            _canvas.CurrentTransform.Core,
            minimumWidth: 40,
            minimumHeight: 20,
            InCanvasTextEditKind.RichText);
        if (!startPlan.IsReady || startPlan.Placement is null || startPlan.OriginalBody is null)
            return;

        _editingShapeId = shapeId;
        _active = true;
        _editSession = InCanvasRichTextEditSession.BeginShape(startPlan);
        _shapeParagraphBody = startPlan.OriginalBody;
        _inheritedLayoutBody = startPlan.InheritedLayoutBody;
        _inheritedMasterTextStyles = startPlan.InheritedMasterTextStyles;
        _inheritedStyleCategory = startPlan.InheritedStyleCategory;

        var placement = startPlan.Placement.Value;

        double fallbackPt = InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
            startPlan.OriginalBody,
            InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt);

        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(
            startPlan.OriginalBody,
            fallbackPt,
            _inheritedLayoutBody,
            _inheritedMasterTextStyles,
            _inheritedStyleCategory);

        _richBox = new RichTextBox(doc)
        {
            AcceptsReturn = true,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(1.5),
            MinWidth = placement.Width,
            MinHeight = placement.Height,
            Width = placement.Width,
            Height = placement.Height,
            SpellCheck = { IsEnabled = false },
            IsUndoEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            SelectionOpacity = InCanvasRichTextSelectionVisualContract.SelectionOpacity,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(
                InCanvasRichTextSelectionVisualContract.BackgroundAlpha,
                InCanvasRichTextSelectionVisualContract.BackgroundRed,
                InCanvasRichTextSelectionVisualContract.BackgroundGreen,
                InCanvasRichTextSelectionVisualContract.BackgroundBlue)),
            SelectionTextBrush = new SolidColorBrush(Color.FromArgb(
                InCanvasRichTextSelectionVisualContract.ForegroundAlpha,
                InCanvasRichTextSelectionVisualContract.ForegroundRed,
                InCanvasRichTextSelectionVisualContract.ForegroundGreen,
                InCanvasRichTextSelectionVisualContract.ForegroundBlue)),
        };
        AutomationProperties.SetAutomationId(
            _richBox,
            PresentationSemanticIdentityCatalog.RichTextEditorInputAutomationId);

        Canvas.SetLeft(_richBox, placement.Left);
        Canvas.SetTop(_richBox, placement.Top);
        ApplyPlacementTransform(_richBox, placement);

        _richBox.LostFocus += OnRichBoxLostFocus;
        _richBox.KeyDown += OnRichBoxKeyDown;
        _richBox.PreviewKeyDown += OnRichBoxPreviewKeyDown;
        _richBox.PreviewMouseLeftButtonDown += OnRichBoxPreviewMouseLeftButtonDown;

        _overlay.IsHitTestVisible = true;
        _overlay.Children.Add(_richBox);
        _canvas.ActiveTextEditShapeId = shapeId;
        _richBox.Focus();
        _richBox.SelectAll();
    }

    /// <summary>The native WPF visual used for the active rich-text selection evidence.</summary>
    public FrameworkElement? ActiveRichTextVisual => _richBox;

    /// <summary>Commits the current text edit, if active, to the command bus and hides the overlay.</summary>
    public void Commit()
    {
        if (!_active || _richBox is null)
        {
            _canvas.ActiveTextEditShapeId = null;
            return;
        }

        var doc = _richBox.Document;
        _overlay.Children.Remove(_richBox);
        _overlay.IsHitTestVisible = false;
        _richBox = null;
        _active = false;
        _canvas.ActiveTextEditShapeId = null;

        var editSession = _editSession;
        _editSession = null;

        var newBody = TextBodyFlowDocumentConverter.FromFlowDocument(
            doc,
            _shapeParagraphBody);
        var decision = editSession?.Commit(newBody)
            ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);

        if (decision.Command is not null)
            _editor.Bus.Execute(decision.Command);

        _shapeParagraphBody = null;
        _inheritedLayoutBody = null;
        _inheritedMasterTextStyles = null;
        _inheritedStyleCategory = default;
    }

    /// <summary>Cancels the edit without committing.</summary>
    public void Cancel()
    {
        if (!_active || _richBox is null)
        {
            _canvas.ActiveTextEditShapeId = null;
            return;
        }

        _canceling = true;
        try
        {
            _overlay.Children.Remove(_richBox);
            _overlay.IsHitTestVisible = false;
            _richBox = null;
            _active = false;
            _canvas.ActiveTextEditShapeId = null;
            _ = _editSession?.Cancel();
            _editSession = null;
            _shapeParagraphBody = null;
            _inheritedLayoutBody = null;
            _inheritedMasterTextStyles = null;
            _inheritedStyleCategory = default;
        }
        finally
        {
            _canceling = false;
        }
    }

    public void Dispose()
    {
        _canvas.MouseLeftButtonDown -= OnCanvasMouseDown;
        _editor.CurrentSlideChanged -= OnEditorCurrentSlideChanged;
        Cancel();
        _canvas.ActiveTextEditShapeId = null;
    }

    private void OnEditorCurrentSlideChanged(object? sender, EventArgs e) => Commit();

    /// <summary>Toggles bold on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyBold()
    {
        ApplyShapeRunMutation((session, selection) =>
            session.ToggleTextFormat(TableCellTextFormatKind.Bold, selection));
    }

    /// <summary>Toggles italic on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyItalic()
    {
        ApplyShapeRunMutation((session, selection) =>
            session.ToggleTextFormat(TableCellTextFormatKind.Italic, selection));
    }

    /// <summary>Toggles underline on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyUnderline()
    {
        ApplyShapeRunMutation((session, selection) =>
            session.ToggleTextFormat(TableCellTextFormatKind.Underline, selection));
    }

    /// <summary>Toggles strikethrough on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyStrikethrough()
    {
        ApplyShapeRunMutation((session, selection) =>
            session.ToggleTextFormat(TableCellTextFormatKind.Strikethrough, selection));
    }

    /// <summary>Applies superscript to the current RichTextBox selection.</summary>
    public void ApplySuperscript()
    {
        ApplyBaseline(BaselineAlignment.Superscript);
    }

    /// <summary>Applies subscript to the current RichTextBox selection.</summary>
    public void ApplySubscript()
    {
        ApplyBaseline(BaselineAlignment.Subscript);
    }

    private void ApplyBaseline(BaselineAlignment alignment)
    {
        ApplyShapeRunMutation((session, selection) =>
            session.ToggleTextFormat(
                alignment == BaselineAlignment.Superscript
                    ? TableCellTextFormatKind.Superscript
                    : TableCellTextFormatKind.Subscript,
                selection));
    }

    /// <summary>Sets font family on the current RichTextBox selection. No-op if not active or null.</summary>
    public void ApplyFont(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
            return;

        ApplyShapeRunMutation((session, selection) =>
            session.ApplyValueFormat(
                TableCellTextValueFormatKind.FontFamily,
                fontFamily,
                selection));
    }

    /// <summary>Sets font size in points on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyFontSize(double? sizePt)
    {
        if (sizePt is null)
            return;

        ApplyShapeRunMutation((session, selection) =>
            session.ApplyValueFormat(
                TableCellTextValueFormatKind.FontSize,
                sizePt,
                selection));
    }

    /// <summary>
    /// Sets text color on the current RichTextBox selection. A null color is the shared
    /// ribbon's Automatic choice and clears the explicit run color so theme inheritance resumes.
    /// </summary>
    public bool ApplyColor(ThemeAwareColor? color) =>
        ApplyShapeRunMutation((session, selection) =>
            session.ApplyValueFormat(
                TableCellTextValueFormatKind.Color,
                color,
                selection));

    public bool TryApplyActiveShapeParagraphAlignment(TextAlign alignment) =>
        ApplyShapeParagraphMutation((session, selection) =>
            session.ApplyParagraphAlignment(alignment, selection));

    public bool TryApplyActiveShapeParagraphBulletToggle() =>
        ApplyShapeParagraphMutation((session, selection) =>
            session.ToggleParagraphBullets(selection));

    public bool TryApplyActiveShapeParagraphNumberingToggle() =>
        ApplyShapeParagraphMutation((session, selection) =>
            session.ToggleParagraphNumbering(selection));

    public bool TryApplyActiveShapeParagraphListPreset(TableCellListPresetDescriptor preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyShapeParagraphMutation((session, selection) =>
            session.ApplyParagraphListPreset(preset, selection));
    }

    public bool TryApplyActiveShapeParagraphPictureBullet(PresentationPictureBulletPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.IsValid)
            return false;

        return ApplyShapeParagraphMutation((session, selection) =>
            session.ApplyParagraphPictureBullet(payload, selection));
    }

    public bool TryApplyActiveShapeParagraphIndent() =>
        ApplyShapeParagraphMutation((session, selection) =>
            session.ApplyParagraphIndent(increase: true, selection));

    public bool TryApplyActiveShapeParagraphOutdent() =>
        ApplyShapeParagraphMutation((session, selection) =>
            session.ApplyParagraphIndent(increase: false, selection));

    private bool ApplyShapeParagraphMutation(
        Func<InCanvasRichTextEditSession, (int Start, int End)?, bool> mutate)
    {
        if (!_active || _richBox is null || _editSession is null)
            return false;

        var current = TextBodyFlowDocumentConverter.FromFlowDocument(
            _richBox.Document,
            _shapeParagraphBody);
        (int Start, int End)? selection = CurrentSelection();
        _editSession.SynchronizeBody(current);
        if (!mutate(_editSession, selection))
            return false;
        var updated = _editSession.Body;
        int start = selection?.Start ?? 0;
        int end = selection?.End ?? InCanvasTextEditPlanner.ExtractPlainText(updated).Length;
        _shapeParagraphBody = updated;

        double fallbackPt = InCanvasRichTextEditorDefaults.ResolveFallbackFontSize(
            updated,
            InCanvasRichTextEditorDefaults.ShapeFallbackFontSizePt);
        _richBox.Document = TextBodyFlowDocumentConverter.ToFlowDocument(
            updated,
            fallbackPt,
            _inheritedLayoutBody,
            _inheritedMasterTextStyles,
            _inheritedStyleCategory);

        var startPointer = TextBodyFlowDocumentConverter.TextPointerAtLogicalOffset(_richBox.Document, start);
        var endPointer = TextBodyFlowDocumentConverter.TextPointerAtLogicalOffset(_richBox.Document, end);
        if (startPointer is not null && endPointer is not null)
            _richBox.Selection.Select(startPointer, endPointer);
        _richBox.Focus();
        return true;
    }

    private bool ApplyShapeRunMutation(
        Func<InCanvasRichTextEditSession, (int Start, int End)?, bool> mutate) =>
        ApplyShapeParagraphMutation(mutate);

    private (int Start, int End)? CurrentSelection()
    {
        if (_richBox is null)
            return null;

        return (
            LogicalOffsetAt(_richBox.Document, _richBox.Selection.Start),
            LogicalOffsetAt(_richBox.Document, _richBox.Selection.End));
    }

    private static int LogicalOffsetAt(FlowDocument document, TextPointer position) =>
        TextBodyFlowDocumentConverter.LogicalOffsetAt(document, position);

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
            return;

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
            return;

        var xf = _canvas.CurrentTransform;
        var pt = e.GetPosition(_canvas);
        var slidePt = xf.ScreenToSlide(pt.X, pt.Y);

        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        if (!hitId.HasValue)
            return;

        var shape = ShapeHitTester.FindShape(slide, hitId.Value);
        if (shape?.TextBody is null)
            return;

        Activate(hitId.Value);
        e.Handled = true;
    }

    private void OnRichBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || e.ClickCount < 2
            || _richBox is null)
            return;

        var pointer = _richBox.GetPositionFromPoint(
            e.GetPosition(_richBox),
            snapToText: true);
        if (pointer is null)
            return;

        int logicalPosition = LogicalOffsetAt(_richBox.Document, pointer);
        if (TryActivateInlineOleAt(logicalPosition)
            || (logicalPosition > 0 && TryActivateInlineOleAt(logicalPosition - 1)))
        {
            e.Handled = true;
        }
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

    private void OnRichBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void OnRichBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_canceling)
            Commit();
    }

    private async void OnRichBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Key is Key.C or Key.X or Key.V)
        {
            var result = await WpfRichTextClipboardAdapter.HandlePreviewKeyDownAsync(
                e,
                _richBox!,
                _shapeParagraphBody,
                clipboard: null,
                cancellationToken: default,
                layoutBody: _inheritedLayoutBody,
                masterTextStyles: _inheritedMasterTextStyles,
                category: _inheritedStyleCategory);
            if (e.Key == Key.V && result.Handled)
                _shapeParagraphBody = result.UpdatedBody;
            else if (e.Key is Key.C or Key.X &&
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

        if (e.Key == Key.B)
        {
            ApplyBold();
            e.Handled = true;
        }
        else if (e.Key == Key.I)
        {
            ApplyItalic();
            e.Handled = true;
        }
        else if (e.Key == Key.U)
        {
            ApplyUnderline();
            e.Handled = true;
        }
        else if (e.Key == Key.D5)
        {
            ApplyStrikethrough();
            e.Handled = true;
        }
        else if (e.Key is Key.OemPlus or Key.Add)
        {
            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
                ApplySuperscript();
            else
                ApplySubscript();
            e.Handled = true;
        }
    }
}
