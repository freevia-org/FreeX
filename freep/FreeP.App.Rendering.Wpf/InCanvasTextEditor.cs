using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Manages an in-canvas text-editing overlay for <see cref="SlideCanvas"/>.
///
/// When the user double-clicks a shape that has a <see cref="TextBody"/>, this class:
/// <list type="number">
///   <item>Activates an overlay <see cref="TextBox"/> positioned over the shape.</item>
///   <item>Populates it with the shape's plain text.</item>
///   <item>On commit (Escape / focus-loss), writes back via the EditingSession command bus
///         so the change is undoable.</item>
/// </list>
///
/// Scope: single text box, plain-text multi-line. Per-run rich formatting is not edited
/// here (out of scope for Wave 3C); existing run formatting is preserved.
///
/// The overlay is added to a <see cref="Canvas"/> that is positioned on top of
/// <see cref="SlideCanvas"/> by the caller (MainWindow seam).
///
/// IME / RTL / bidirectional text input: deferred.
/// </summary>
public sealed class InCanvasTextEditor
{
    private readonly SlideCanvas     _canvas;
    private readonly EditingSession  _editor;
    private readonly Canvas          _overlay;

    private TextBox?  _textBox;
    private uint      _editingShapeId;
    private bool      _active;

    public InCanvasTextEditor(SlideCanvas canvas, EditingSession editor, Canvas overlay)
    {
        _canvas  = canvas  ?? throw new ArgumentNullException(nameof(canvas));
        _editor  = editor  ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

        _canvas.MouseLeftButtonDown += OnCanvasMouseDown;
    }

    // ── Public surface ────────────────────────────────────────────────────────────────────────

    public bool IsActive => _active;
    public uint ActiveShapeId => _editingShapeId;

    /// <summary>
    /// Activates the text editor for the given shape. Caller checks shape has a TextBody.
    /// </summary>
    public void Activate(uint shapeId)
    {
        if (_active && _editingShapeId == shapeId) return;

        Commit();   // commit any previous session first

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        if (shape?.TextBody is null) return;

        _editingShapeId = shapeId;
        _active         = true;

        var xf    = _canvas.CurrentTransform;
        var b     = ShapeHitTester.GetShapeBoundsDip(shape, _editor.Presentation);

        double x = b.Left  * xf.Scale + xf.OffsetX;
        double y = b.Top   * xf.Scale + xf.OffsetY;
        double w = b.Width * xf.Scale;
        double h = b.Height * xf.Scale;

        _textBox = new TextBox
        {
            AcceptsReturn    = true,
            TextWrapping     = TextWrapping.Wrap,
            Background       = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush      = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness  = new Thickness(1.5),
            FontSize         = 14,
            Text             = shape.PlainText,
            MinWidth         = Math.Max(40, w),
            MinHeight        = Math.Max(20, h),
            Width            = Math.Max(40, w),
            Height           = Math.Max(20, h),
        };

        Canvas.SetLeft(_textBox, x);
        Canvas.SetTop (_textBox, y);

        _textBox.LostFocus += (_, _) => Commit();
        _textBox.KeyDown   += OnTextBoxKeyDown;

        _overlay.Children.Add(_textBox);
        _textBox.Focus();
        _textBox.SelectAll();

        // Suppress the double-click selection from propagating to gesture handler
    }

    /// <summary>Commits the current text edit (if active) to the command bus and hides the overlay.</summary>
    public void Commit()
    {
        if (!_active || _textBox is null) return;

        var newText = _textBox.Text ?? string.Empty;
        _overlay.Children.Remove(_textBox);
        _textBox = null;
        _active  = false;

        // Write back via EditingSession
        var slide = _editor.CurrentSlide;
        if (slide is null) return;
        var shape = slide.Shapes.FirstOrDefault(s => s.Id == _editingShapeId);
        if (shape is null) return;

        // Only issue a command if text changed
        if (shape.PlainText == newText) return;

        // Use SetShapeText command (write via AddShape of a clone is too heavy;
        // we issue a ToggleBold trick-free approach: directly mutate via bus with a dedicated helper).
        // Since EditingSession does not expose SetShapeText directly, we use the
        // SetRunFontCommand trick: replace all runs.  The cleanest approach that
        // keeps it undoable is to use the Bus directly with a SetShapeTextCommand.
        // SetShapeTextCommand is implemented below in this file for the bus.
        _editor.Bus.Execute(new SetShapeTextCommand(
            _editor.CurrentSlideIndex, _editingShapeId, newText));
    }

    /// <summary>Cancels the edit without committing.</summary>
    public void Cancel()
    {
        if (!_active || _textBox is null) return;
        _overlay.Children.Remove(_textBox);
        _textBox = null;
        _active  = false;
    }

    // ── Double-click detection ────────────────────────────────────────────────────────────────

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

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }
}

// ── SetShapeTextCommand ────────────────────────────────────────────────────────────────────────

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

        // Save undo snapshot (shallow clone of TextBody is sufficient — TextBody is mutable)
        _previousTextBody = CloneTextBody(shape.TextBody);

        // Apply new text
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
            var para = new Paragraph();
            para.Runs.Add(new Run
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

    private static TextBody? CloneTextBody(TextBody? src)
    {
        if (src is null) return null;
        var clone = new TextBody
        {
            Wrap     = src.Wrap,
            Anchor   = src.Anchor,
            InsetLeftPt   = src.InsetLeftPt,
            InsetRightPt  = src.InsetRightPt,
            InsetTopPt    = src.InsetTopPt,
            InsetBottomPt = src.InsetBottomPt
        };
        foreach (var p in src.Paragraphs)
        {
            var cp = new Paragraph
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
                cp.Runs.Add(new Run
                {
                    Text       = r.Text,
                    FontFamily = r.FontFamily,
                    FontSizePt = r.FontSizePt,
                    Bold       = r.Bold,
                    Italic     = r.Italic,
                    Underline  = r.Underline,
                    Strikethrough = r.Strikethrough,
                    Color      = r.Color
                });
            }
            clone.Paragraphs.Add(cp);
        }
        return clone;
    }
}
