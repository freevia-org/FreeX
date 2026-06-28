using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FreeP.App.Compositor;

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

    /// <summary>True while a shape's text is being edited in the overlay TextBox.</summary>
    public bool IsActive => _active;

    /// <summary>The id of the shape currently being edited, or 0 if not active.</summary>
    public uint ActiveShapeId => _editingShapeId;

    public AvaloniaInCanvasTextEditor(SlideCanvas canvas, EditingSession editor, Panel overlay)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));

        _canvas.PointerPressed += OnCanvasPointerPressed;
    }

    /// <summary>Activates the text editor for the given shape.</summary>
    public void Activate(uint shapeId)
    {
        if (_active && _editingShapeId == shapeId)
            return;

        Commit();

        var slide = _editor.CurrentSlide;
        if (slide is null || _editor.Presentation is null)
            return;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        if (shape?.TextBody is null)
            return;

        _editingShapeId = shapeId;
        _active = true;
        _editPlan = InCanvasTextEditPlanner.BeginPlainText(
            _editor.CurrentSlideIndex,
            shapeId,
            shape.TextBody);

        var xf = _canvas.CurrentTransform;
        var b = ShapeHitTester.GetShapeBoundsDip(shape, _editor.Presentation);
        double x = b.Left * xf.Scale + xf.OffsetX;
        double y = b.Top * xf.Scale + xf.OffsetY;
        double w = b.Width * xf.Scale;
        double h = b.Height * xf.Scale;

        _textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            Text = _editPlan.OriginalPlainText,
            MinWidth = Math.Max(40, w),
            MinHeight = Math.Max(20, h),
            Width = Math.Max(40, w),
            Height = Math.Max(20, h),
            Padding = new Thickness(2),
            Background = new global::Avalonia.Media.SolidColorBrush(
                global::Avalonia.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new global::Avalonia.Media.SolidColorBrush(
                global::Avalonia.Media.Color.FromRgb(0x21, 0x96, 0xF3)),
            BorderThickness = new Thickness(1.5),
        };

        Canvas.SetLeft(_textBox, x);
        Canvas.SetTop(_textBox, y);

        _textBox.LostFocus += (_, _) => Commit();
        _textBox.KeyDown += OnTextBoxKeyDown;

        _overlay.IsVisible = true;
        _overlay.Children.Add(_textBox);
        _textBox.Focus();
        _textBox.SelectAll();
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
            _overlay.IsVisible = false;
            _textBox = null;
            _active = false;
            _editPlan = null;

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

    /// <summary>Cancels the edit without committing.</summary>
    public void Cancel()
    {
        if (!_active || _textBox is null)
            return;

        _overlay.Children.Remove(_textBox);
        _overlay.IsVisible = false;
        _textBox = null;
        _active = false;
        _ = _editPlan?.Cancel();
        _editPlan = null;
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount < 2)
            return;
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
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

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }
}
