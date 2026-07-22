using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Manages an in-canvas rich-text editing overlay for <see cref="SlideCanvas"/>.
/// </summary>
public sealed class InCanvasTextEditor
{
    private readonly SlideCanvas _canvas;
    private readonly EditingSession _editor;
    private readonly Canvas _overlay;

    private RichTextBox? _richBox;
    private InCanvasTextEditPlanner? _editPlan;
    private uint _editingShapeId;
    private bool _active;

    public InCanvasTextEditor(SlideCanvas canvas, EditingSession editor, Canvas overlay)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

        _canvas.MouseLeftButtonDown += OnCanvasMouseDown;
    }

    /// <summary>True while a shape's text is being edited in the RichTextBox overlay.</summary>
    public bool IsActive => _active;

    /// <summary>The id of the shape currently being edited, or 0 if not active.</summary>
    public uint ActiveShapeId => _editingShapeId;

    /// <summary>True when the active editor owns keyboard focus.</summary>
    public bool IsEditorFocused => _richBox?.IsKeyboardFocusWithin == true;

    /// <summary>The text selected by the active editor.</summary>
    public string SelectedText => _richBox?.Selection.Text ?? string.Empty;

    /// <summary>Selects a logical model-text range in the active editor.</summary>
    public bool TrySelectTextRange(int start, int end)
    {
        if (_richBox is null || start < 0 || end < start)
            return false;

        var startPointer = TextPointerAtLogicalOffset(_richBox.Document, start);
        var endPointer = TextPointerAtLogicalOffset(_richBox.Document, end);
        if (startPointer is null || endPointer is null)
            return false;

        _richBox.Selection.Select(startPointer, endPointer);
        _richBox.Focus();
        return true;
    }

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
        _editPlan = startPlan.EditPlanner;

        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            new SlideScreenRect(
                startPlan.Placement.Value.Left,
                startPlan.Placement.Value.Top,
                startPlan.Placement.Value.Width,
                startPlan.Placement.Value.Height),
            minimumWidth: 40,
            minimumHeight: 20);

        double fallbackPt = startPlan.OriginalBody.Paragraphs
            .SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.FontSizePt.HasValue)?.FontSizePt ?? 14.0;

        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(startPlan.OriginalBody, fallbackPt);

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
        };

        Canvas.SetLeft(_richBox, placement.Left);
        Canvas.SetTop(_richBox, placement.Top);

        _richBox.LostFocus += (_, _) => Commit();
        _richBox.KeyDown += OnRichBoxKeyDown;
        _richBox.PreviewKeyDown += OnRichBoxPreviewKeyDown;

        _overlay.IsHitTestVisible = true;
        _overlay.Children.Add(_richBox);
        _richBox.Focus();
        _richBox.SelectAll();
    }

    /// <summary>Commits the current text edit, if active, to the command bus and hides the overlay.</summary>
    public void Commit()
    {
        if (!_active || _richBox is null)
            return;

        var doc = _richBox.Document;
        _overlay.Children.Remove(_richBox);
        _overlay.IsHitTestVisible = false;
        _richBox = null;
        _active = false;

        var editPlan = _editPlan;
        _editPlan = null;

        var slide = _editor.CurrentSlide;
        if (slide is null)
            return;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == _editingShapeId);
        if (shape is null)
            return;

        var newBody = TextBodyFlowDocumentConverter.FromFlowDocument(doc, shape.TextBody);
        var decision = editPlan?.CommitRichText(newBody)
            ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);

        if (decision.Command is not null)
            _editor.Bus.Execute(decision.Command);
    }

    /// <summary>Cancels the edit without committing.</summary>
    public void Cancel()
    {
        if (!_active || _richBox is null)
            return;

        _overlay.Children.Remove(_richBox);
        _overlay.IsHitTestVisible = false;
        _richBox = null;
        _active = false;
        _ = _editPlan?.Cancel();
        _editPlan = null;
    }

    /// <summary>Toggles bold on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyBold()
    {
        if (_richBox is null)
            return;

        EditingCommands.ToggleBold.Execute(null, _richBox);
    }

    /// <summary>Toggles italic on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyItalic()
    {
        if (_richBox is null)
            return;

        EditingCommands.ToggleItalic.Execute(null, _richBox);
    }

    /// <summary>Toggles underline on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyUnderline()
    {
        if (_richBox is null)
            return;

        EditingCommands.ToggleUnderline.Execute(null, _richBox);
    }

    /// <summary>Sets font family on the current RichTextBox selection. No-op if not active or null.</summary>
    public void ApplyFont(string? fontFamily)
    {
        if (_richBox is null || string.IsNullOrEmpty(fontFamily))
            return;

        _richBox.Selection.ApplyPropertyValue(
            TextElement.FontFamilyProperty,
            new FontFamily(fontFamily));
    }

    /// <summary>Sets font size in points on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyFontSize(double? sizePt)
    {
        if (_richBox is null || sizePt is null)
            return;

        _richBox.Selection.ApplyPropertyValue(
            TextElement.FontSizeProperty,
            sizePt.Value * (96.0 / 72.0));
    }

    /// <summary>Sets text color on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyColor(ThemeAwareColor? color)
    {
        if (_richBox is null || color is null)
            return;

        var wpfColor = TextBodyFlowDocumentConverter.ResolveModelColor(color);
        if (wpfColor is null)
            return;

        _richBox.Selection.ApplyPropertyValue(
            TextElement.ForegroundProperty,
            new SolidColorBrush(wpfColor.Value));
    }

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

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == hitId.Value);
        if (shape?.TextBody is null)
            return;

        Activate(hitId.Value);
        e.Handled = true;
    }

    private static TextPointer? TextPointerAtLogicalOffset(FlowDocument document, int logicalOffset)
    {
        int remaining = logicalOffset;
        bool firstParagraph = true;
        foreach (var paragraph in document.Blocks.OfType<System.Windows.Documents.Paragraph>())
        {
            if (!firstParagraph)
            {
                if (remaining == 0)
                    return paragraph.ContentStart;
                remaining--;
            }
            firstParagraph = false;

            foreach (var inline in TextBodyFlowDocumentConverter.EnumerateLeafInlines(paragraph.Inlines))
            {
                if (inline is System.Windows.Documents.Run run)
                {
                    int length = run.Text?.Length ?? 0;
                    if (remaining <= length)
                        return run.ContentStart.GetPositionAtOffset(remaining) ?? run.ContentEnd;
                    remaining -= length;
                }
                else if (inline is LineBreak)
                {
                    if (remaining == 0)
                        return inline.ElementStart;
                    remaining--;
                }
            }
        }

        return remaining == 0 ? document.ContentEnd : null;
    }

    private void OnRichBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void OnRichBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == 0)
            return;

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
    }
}
