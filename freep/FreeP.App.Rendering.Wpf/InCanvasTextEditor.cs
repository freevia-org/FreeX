using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun       = FreeP.Core.Model.Run;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Manages an in-canvas rich-text editing overlay for <see cref="SlideCanvas"/>.
///
/// Wave 10A: replaced the plain <see cref="TextBox"/> with a WPF <see cref="RichTextBox"/> so
/// per-run formatting (bold/italic/underline/font/size/color) is preserved during editing.
///
/// When the user double-clicks a shape that has a <see cref="TextBody"/>, this class:
/// <list type="number">
///   <item>Converts the shape's <see cref="TextBody"/> → <see cref="FlowDocument"/> and loads
///         it into a <see cref="RichTextBox"/> positioned over the shape.</item>
///   <item>While focused, the ribbon's Bold/Italic/Underline/Font/Size/Color commands are
///         routed here (via <see cref="ApplyBold"/>, <see cref="ApplyItalic"/>,
///         <see cref="ApplyUnderline"/>, <see cref="ApplyFont"/>, <see cref="ApplyFontSize"/>,
///         <see cref="ApplyColor"/>) and applied to the current Selection.</item>
///   <item>On commit (Escape / focus-loss), converts the <see cref="FlowDocument"/> back to
///         a <see cref="TextBody"/> and issues a <see cref="SetShapeTextBodyCommand"/> so
///         the change is one undoable step.</item>
/// </list>
///
/// IME / RTL / per-run super-subscript: deferred.
/// </summary>
public sealed class InCanvasTextEditor
{
    private readonly SlideCanvas     _canvas;
    private readonly EditingSession  _editor;
    private readonly Canvas          _overlay;

    private RichTextBox?  _richBox;
    private TextBody?     _originalTextBody;   // snapshot for change detection
    private uint          _editingShapeId;
    private bool          _active;

    public InCanvasTextEditor(SlideCanvas canvas, EditingSession editor, Canvas overlay)
    {
        _canvas  = canvas  ?? throw new ArgumentNullException(nameof(canvas));
        _editor  = editor  ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

        _canvas.MouseLeftButtonDown += OnCanvasMouseDown;
    }

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>True while a shape's text is being edited in the RichTextBox overlay.</summary>
    public bool IsActive => _active;

    /// <summary>The id of the shape currently being edited, or 0 if not active.</summary>
    public uint ActiveShapeId => _editingShapeId;

    /// <summary>
    /// Activates the rich-text editor for the given shape. Caller checks shape has a TextBody.
    /// </summary>
    public void Activate(uint shapeId)
    {
        if (_active && _editingShapeId == shapeId) return;

        Commit();   // commit any previous session first

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        if (shape?.TextBody is null) return;

        _editingShapeId   = shapeId;
        _active           = true;
        _originalTextBody = TextBodyFlowDocumentConverter.ToFlowDocument(shape.TextBody)
                            is var _ ? CloneTextBody(shape.TextBody) : null;
        // Simpler: just keep a deep clone for change detection.
        _originalTextBody = CloneTextBody(shape.TextBody);

        var xf = _canvas.CurrentTransform;
        var b  = ShapeHitTester.GetShapeBoundsDip(shape, _editor.Presentation);

        double x = b.Left   * xf.Scale + xf.OffsetX;
        double y = b.Top    * xf.Scale + xf.OffsetY;
        double w = b.Width  * xf.Scale;
        double h = b.Height * xf.Scale;

        // Determine fallback font size from first run, or 14pt.
        double fallbackPt = shape.TextBody.Paragraphs
            .SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.FontSizePt.HasValue)?.FontSizePt ?? 14.0;

        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(shape.TextBody, fallbackPt);

        _richBox = new RichTextBox(doc)
        {
            AcceptsReturn         = true,
            Background            = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush           = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness       = new Thickness(1.5),
            MinWidth              = Math.Max(40, w),
            MinHeight             = Math.Max(20, h),
            Width                 = Math.Max(40, w),
            Height                = Math.Max(20, h),
            // Disable the spell-check red squiggles (distracting in a slide editor).
            SpellCheck            = { IsEnabled = false },
            // Disable the built-in formatting shortcuts (Ctrl+B/I/U) so the ribbon stays in control.
            IsUndoEnabled         = false,   // undo is managed by our bus, not the RTB undo stack
            VerticalScrollBarVisibility   = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };

        Canvas.SetLeft(_richBox, x);
        Canvas.SetTop (_richBox, y);

        _richBox.LostFocus += (_, _) => Commit();
        _richBox.KeyDown   += OnRichBoxKeyDown;
        // Intercept built-in Ctrl+B/I/U so they go through our format methods.
        _richBox.PreviewKeyDown += OnRichBoxPreviewKeyDown;

        _overlay.IsHitTestVisible = true;
        _overlay.Children.Add(_richBox);
        _richBox.Focus();
        _richBox.SelectAll();
    }

    /// <summary>Commits the current text edit (if active) to the command bus and hides the overlay.</summary>
    public void Commit()
    {
        if (!_active || _richBox is null) return;

        var doc  = _richBox.Document;
        _overlay.Children.Remove(_richBox);
        _overlay.IsHitTestVisible = false;
        _richBox = null;
        _active  = false;

        // Rebuild model TextBody from the FlowDocument.
        var slide = _editor.CurrentSlide;
        if (slide is null) return;
        var shape = slide.Shapes.FirstOrDefault(s => s.Id == _editingShapeId);
        if (shape is null) return;

        var newBody = TextBodyFlowDocumentConverter.FromFlowDocument(doc, shape.TextBody);

        // Only issue a command if content actually changed.
        if (TextBodiesEqual(_originalTextBody, newBody)) return;

        _editor.Bus.Execute(new SetShapeTextBodyCommand(
            _editor.CurrentSlideIndex, _editingShapeId, newBody));
    }

    /// <summary>Cancels the edit without committing.</summary>
    public void Cancel()
    {
        if (!_active || _richBox is null) return;
        _overlay.Children.Remove(_richBox);
        _overlay.IsHitTestVisible = false;
        _richBox = null;
        _active  = false;
    }

    // ── Ribbon format application (10A SEAM) ──────────────────────────────────
    // These are called by the ribbon routing in MainWindow / FreePRibbonCommands
    // when IsActive is true, so formatting applies to the selection inside the RTB.

    /// <summary>Toggles bold on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyBold()
    {
        if (_richBox is null) return;
        EditingCommands.ToggleBold.Execute(null, _richBox);
    }

    /// <summary>Toggles italic on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyItalic()
    {
        if (_richBox is null) return;
        EditingCommands.ToggleItalic.Execute(null, _richBox);
    }

    /// <summary>Toggles underline on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyUnderline()
    {
        if (_richBox is null) return;
        EditingCommands.ToggleUnderline.Execute(null, _richBox);
    }

    /// <summary>Sets font family on the current RichTextBox selection. No-op if not active or null.</summary>
    public void ApplyFont(string? fontFamily)
    {
        if (_richBox is null || string.IsNullOrEmpty(fontFamily)) return;
        _richBox.Selection.ApplyPropertyValue(
            TextElement.FontFamilyProperty,
            new FontFamily(fontFamily));
    }

    /// <summary>Sets font size (pt) on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyFontSize(double? sizePt)
    {
        if (_richBox is null || sizePt is null) return;
        _richBox.Selection.ApplyPropertyValue(
            TextElement.FontSizeProperty,
            sizePt.Value * (96.0 / 72.0));
    }

    /// <summary>Sets text color on the current RichTextBox selection. No-op if not active.</summary>
    public void ApplyColor(ThemeAwareColor? color)
    {
        if (_richBox is null || color is null) return;
        var wpfColor = TextBodyFlowDocumentConverter.ResolveModelColor(color);
        if (wpfColor is null) return;
        _richBox.Selection.ApplyPropertyValue(
            TextElement.ForegroundProperty,
            new SolidColorBrush(wpfColor.Value));
    }

    // ── Double-click detection ────────────────────────────────────────────────

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return; // only double-click

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var xf      = _canvas.CurrentTransform;
        var pt      = e.GetPosition(_canvas);
        var slidePt = xf.ScreenToSlide(pt.X, pt.Y);

        var hitId = ShapeHitTester.HitTest(slide, _editor.Presentation, slidePt.X, slidePt.Y);
        if (!hitId.HasValue) return;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == hitId.Value);
        if (shape?.TextBody is null) return;

        Activate(hitId.Value);
        e.Handled = true;
    }

    private void OnRichBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Intercept Ctrl+B/I/U before the RichTextBox processes them, so formatting goes
    /// through the ribbon-aware Apply* methods (which keep the ribbon toggle state in sync).
    /// </summary>
    private void OnRichBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == 0) return;

        if (e.Key == Key.B) { ApplyBold();      e.Handled = true; }
        else if (e.Key == Key.I) { ApplyItalic();    e.Handled = true; }
        else if (e.Key == Key.U) { ApplyUnderline(); e.Handled = true; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TextBodiesEqual(TextBody? a, TextBody? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Paragraphs.Count != b.Paragraphs.Count) return false;

        for (int pi = 0; pi < a.Paragraphs.Count; pi++)
        {
            var pa = a.Paragraphs[pi];
            var pb = b.Paragraphs[pi];
            if (pa.Runs.Count != pb.Runs.Count) return false;
            if (pa.Align != pb.Align) return false;

            for (int ri = 0; ri < pa.Runs.Count; ri++)
            {
                var ra = pa.Runs[ri];
                var rb = pb.Runs[ri];
                if (ra.Text != rb.Text
                    || ra.Bold != rb.Bold
                    || ra.Italic != rb.Italic
                    || ra.Underline != rb.Underline
                    || ra.Strikethrough != rb.Strikethrough
                    || ra.FontFamily != rb.FontFamily
                    || ra.FontSizePt != rb.FontSizePt)
                    return false;
            }
        }
        return true;
    }

    private static TextBody? CloneTextBody(TextBody? src) =>
        SetShapeTextBodyCommand.CloneTextBody(src);
}

// ── SetShapeTextBodyCommand ───────────────────────────────────────────────────────────────────────

/// <summary>
/// Replaces the entire <see cref="TextBody"/> of a shape with a new one (preserving per-run
/// formatting). Stores the previous body for undo. Undoable (one step per edit session).
/// </summary>
internal sealed class SetShapeTextBodyCommand : IPresentationCommand
{
    private readonly int      _slideIndex;
    private readonly uint     _shapeId;
    private readonly TextBody _newBody;

    // Undo snapshot.
    private TextBody? _previousBody;

    public SetShapeTextBodyCommand(int slideIndex, uint shapeId, TextBody newBody)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newBody    = newBody ?? throw new ArgumentNullException(nameof(newBody));
    }

    public string Label => "Edit Rich Text";

    public void Apply(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null) return;

        _previousBody = CloneTextBody(shape.TextBody);
        shape.TextBody = CloneTextBody(_newBody);
    }

    public void Revert(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null) return;
        shape.TextBody = CloneTextBody(_previousBody);
    }

    private SlideShape? GetShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count) return null;
        return presentation.Slides[_slideIndex].Shapes.FirstOrDefault(s => s.Id == _shapeId);
    }

    /// <summary>Deep-clones a <see cref="TextBody"/>. Exposed internally for change-detection in the editor.</summary>
    internal static TextBody? CloneTextBody(TextBody? src)
    {
        if (src is null) return null;
        var clone = new TextBody
        {
            Wrap          = src.Wrap,
            Anchor        = src.Anchor,
            AutoFit       = src.AutoFit,
            InsetLeftPt   = src.InsetLeftPt,
            InsetRightPt  = src.InsetRightPt,
            InsetTopPt    = src.InsetTopPt,
            InsetBottomPt = src.InsetBottomPt,
        };
        foreach (var p in src.Paragraphs)
        {
            var cp = new ModelParagraph
            {
                Align         = p.Align,
                Level         = p.Level,
                BulletKind    = p.BulletKind,
                BulletChar    = p.BulletChar,
                SpaceBeforePt = p.SpaceBeforePt,
                SpaceAfterPt  = p.SpaceAfterPt,
            };
            foreach (var r in p.Runs)
            {
                cp.Runs.Add(new ModelRun
                {
                    Text          = r.Text,
                    FontFamily    = r.FontFamily,
                    FontSizePt    = r.FontSizePt,
                    Bold          = r.Bold,
                    Italic        = r.Italic,
                    Underline     = r.Underline,
                    Strikethrough = r.Strikethrough,
                    Color         = r.Color,
                });
            }
            clone.Paragraphs.Add(cp);
        }
        return clone;
    }
}

// ── SetShapeTextCommand (legacy — kept for compatibility) ─────────────────────────────────────────

/// <summary>
/// Replaces the entire text content of a shape's TextBody with a new plain-text string.
/// Preserves the first run's formatting from the first paragraph (run 0 of para 0) and
/// resets all other runs to plain text with the same format.
/// Undoable (stores previous state).
/// </summary>
internal sealed class SetShapeTextCommand : IPresentationCommand
{
    private readonly int    _slideIndex;
    private readonly uint   _shapeId;
    private readonly string _newText;

    // Undo data
    private TextBody? _previousTextBody;

    public SetShapeTextCommand(int slideIndex, uint shapeId, string newText)
    {
        _slideIndex = slideIndex;
        _shapeId    = newText == null ? throw new ArgumentNullException(nameof(newText)) : shapeId;
        _newText    = newText;
    }

    public string Label => "Edit Text";

    public void Apply(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null) return;

        _previousTextBody = SetShapeTextBodyCommand.CloneTextBody(shape.TextBody);
        ApplyText(shape, _newText);
    }

    public void Revert(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null) return;
        shape.TextBody = _previousTextBody;
    }

    private SlideShape? GetShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count) return null;
        return presentation.Slides[_slideIndex].Shapes.FirstOrDefault(s => s.Id == _shapeId);
    }

    private static void ApplyText(SlideShape shape, string text)
    {
        // Split on newlines → paragraphs
        var lines = text.Split('\n');

        // Capture first run properties for formatting
        string fontFamily = "Calibri";
        double? fontSize  = null;
        bool bold = false, italic = false, underline = false;
        var    color = (FreeP.Core.Model.ThemeAwareColor?)null;

        if (shape.TextBody?.Paragraphs.Count > 0 &&
            shape.TextBody.Paragraphs[0].Runs.Count > 0)
        {
            var r0 = shape.TextBody.Paragraphs[0].Runs[0];
            fontFamily = r0.FontFamily ?? fontFamily;
            fontSize   = r0.FontSizePt;
            bold       = r0.Bold;
            italic     = r0.Italic;
            underline  = r0.Underline;
            color      = r0.Color;
        }

        if (shape.TextBody is null)
            shape.TextBody = new TextBody { Wrap = true };

        shape.TextBody.Paragraphs.Clear();

        foreach (var line in lines)
        {
            var para = new ModelParagraph();
            para.Runs.Add(new ModelRun
            {
                Text       = line,
                FontFamily = fontFamily,
                FontSizePt = fontSize,
                Bold       = bold,
                Italic     = italic,
                Underline  = underline,
                Color      = color
            });
            shape.TextBody.Paragraphs.Add(para);
        }
    }
}
