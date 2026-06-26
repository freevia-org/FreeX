using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun       = FreeP.Core.Model.Run;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Manages a plain-text in-canvas editing overlay for the Avalonia <see cref="SlideCanvas"/>.
///
/// When the user double-clicks a shape that has a <see cref="TextBody"/>, this class:
/// <list type="number">
///   <item>Extracts the shape's text into a single multi-line string (newlines = paragraphs).</item>
///   <item>Shows an Avalonia <see cref="TextBox"/> positioned over the shape bounds.</item>
///   <item>On commit (Escape / focus-loss), rebuilds a <see cref="TextBody"/> from the
///         edited text and issues a <see cref="SetShapeTextCommand"/> (one undoable step).</item>
/// </list>
///
/// Per-run rich-text editing (bold/italic/color per run) is deferred for Avalonia v1.
/// The WPF host uses <c>InCanvasTextEditor</c> (with RichTextBox) for that capability.
/// </summary>
public sealed class AvaloniaInCanvasTextEditor
{
    private readonly SlideCanvas    _canvas;
    private readonly EditingSession _editor;
    private readonly Panel          _overlay;  // Canvas or Grid that hosts the TextBox

    private TextBox?  _textBox;
    private uint      _editingShapeId;
    private string?   _originalText;   // snapshot for change detection
    private bool      _active;
    private bool      _committing;     // re-entrancy guard

    /// <summary>True while a shape's text is being edited in the overlay TextBox.</summary>
    public bool IsActive => _active;

    /// <summary>The id of the shape currently being edited, or 0 if not active.</summary>
    public uint ActiveShapeId => _editingShapeId;

    public AvaloniaInCanvasTextEditor(SlideCanvas canvas, EditingSession editor, Panel overlay)
    {
        _canvas  = canvas  ?? throw new ArgumentNullException(nameof(canvas));
        _editor  = editor  ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

        _canvas.PointerPressed += OnCanvasPointerPressed;
    }

    // ── Public surface ─────────────────────────────────────────────────────────

    /// <summary>Activates the text editor for the given shape (must have a TextBody).</summary>
    public void Activate(uint shapeId)
    {
        if (_active && _editingShapeId == shapeId) return;
        Commit(); // commit previous session first

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null) return;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        if (shape?.TextBody is null) return;

        _editingShapeId = shapeId;
        _active         = true;
        _originalText   = ExtractPlainText(shape.TextBody);

        var xf = _canvas.CurrentTransform;
        var b  = ShapeHitTester.GetShapeBoundsDip(shape, _editor.Presentation);
        double x = b.Left   * xf.Scale + xf.OffsetX;
        double y = b.Top    * xf.Scale + xf.OffsetY;
        double w = b.Width  * xf.Scale;
        double h = b.Height * xf.Scale;

        _textBox = new TextBox
        {
            AcceptsReturn   = true,
            TextWrapping    = global::Avalonia.Media.TextWrapping.Wrap,
            Text            = _originalText,
            MinWidth        = Math.Max(40, w),
            MinHeight       = Math.Max(20, h),
            Width           = Math.Max(40, w),
            Height          = Math.Max(20, h),
            Padding         = new Thickness(2),
            Background      = new global::Avalonia.Media.SolidColorBrush(
                                  global::Avalonia.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush     = new global::Avalonia.Media.SolidColorBrush(
                                  global::Avalonia.Media.Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(1.5),
        };

        Canvas.SetLeft(_textBox, x);
        Canvas.SetTop (_textBox, y);

        _textBox.LostFocus += (_, _) => Commit();
        _textBox.KeyDown   += OnTextBoxKeyDown;

        _overlay.IsVisible = true;
        _overlay.Children.Add(_textBox);
        _textBox.Focus();
        _textBox.SelectAll();
    }

    /// <summary>Commits the current text edit (if active) to the command bus and hides the overlay.</summary>
    public void Commit()
    {
        if (!_active || _textBox is null || _committing) return;
        _committing = true;
        try
        {
            var newText = _textBox.Text ?? string.Empty;

            _overlay.Children.Remove(_textBox);
            _overlay.IsVisible = false;
            _textBox = null;
            _active  = false;

            // Only issue a command if content changed.
            if (newText == _originalText) return;

            var slide = _editor.CurrentSlide;
            if (slide is null) return;
            var shape = slide.Shapes.FirstOrDefault(s => s.Id == _editingShapeId);
            if (shape is null) return;

            // Build TextBody from edited text (preserve first-run formatting).
            var newBody = BuildTextBody(shape.TextBody, newText);
            _editor.Bus.Execute(new SetShapeTextBodyCommand(_editor.CurrentSlideIndex, _editingShapeId, newBody));
        }
        finally
        {
            _committing = false;
        }
    }

    /// <summary>Cancels the edit without committing.</summary>
    public void Cancel()
    {
        if (!_active || _textBox is null) return;
        _overlay.Children.Remove(_textBox);
        _overlay.IsVisible = false;
        _textBox = null;
        _active  = false;
    }

    // ── Double-click detection ──────────────────────────────────────────────────

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;

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

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string ExtractPlainText(TextBody body)
    {
        var sb = new System.Text.StringBuilder();
        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            if (pi > 0) sb.Append('\n');
            foreach (var run in body.Paragraphs[pi].Runs)
                sb.Append(run.Text);
        }
        return sb.ToString();
    }

    private static TextBody BuildTextBody(TextBody? original, string text)
    {
        // Capture first-run formatting to preserve it.
        string  fontFamily = "Calibri";
        double? fontSize   = null;
        bool    bold = false, italic = false, underline = false;
        ThemeAwareColor? color = null;

        if (original?.Paragraphs.Count > 0 && original.Paragraphs[0].Runs.Count > 0)
        {
            var r0 = original.Paragraphs[0].Runs[0];
            fontFamily = r0.FontFamily ?? fontFamily;
            fontSize   = r0.FontSizePt;
            bold       = r0.Bold;
            italic     = r0.Italic;
            underline  = r0.Underline;
            color      = r0.Color;
        }

        var body = new TextBody
        {
            Wrap          = original?.Wrap ?? true,
            Anchor        = original?.Anchor ?? VerticalAnchor.Top,
            InsetLeftPt   = original?.InsetLeftPt,
            InsetRightPt  = original?.InsetRightPt,
            InsetTopPt    = original?.InsetTopPt,
            InsetBottomPt = original?.InsetBottomPt,
        };

        var lines = text.Split('\n');
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
                Color      = color,
            });
            body.Paragraphs.Add(para);
        }

        if (body.Paragraphs.Count == 0)
        {
            var para = new ModelParagraph();
            para.Runs.Add(new ModelRun { Text = string.Empty });
            body.Paragraphs.Add(para);
        }

        return body;
    }
}

// ── SetShapeTextBodyCommand ─────────────────────────────────────────────────────────────────

/// <summary>
/// Replaces the entire <see cref="TextBody"/> of a shape.  One undoable step per edit session.
/// Reuses the same command name as the WPF version so the undo history is consistent.
/// </summary>
internal sealed class SetShapeTextBodyCommand : IPresentationCommand
{
    private readonly int      _slideIndex;
    private readonly uint     _shapeId;
    private readonly TextBody _newBody;
    private TextBody?         _previousBody;

    public SetShapeTextBodyCommand(int slideIndex, uint shapeId, TextBody newBody)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _newBody    = newBody ?? throw new ArgumentNullException(nameof(newBody));
    }

    public string Label => "Edit Text";

    public void Apply(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null) return;
        _previousBody  = CloneTextBody(shape.TextBody);
        shape.TextBody = CloneTextBody(_newBody);
    }

    public void Revert(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null) return;
        shape.TextBody = CloneTextBody(_previousBody);
    }

    private SlideShape? GetShape(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return null;
        return p.Slides[_slideIndex].Shapes.FirstOrDefault(s => s.Id == _shapeId);
    }

    private static TextBody? CloneTextBody(TextBody? src)
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
