using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

// ══════════════════════════════════════════════════════════════════════════════
// Wave 16B: Animation Pane
//
// A right-side collapsible panel that lists the current slide's animations in
// play order.  Each row shows:
//   • Order number (1-based)
//   • Target shape name (looked up by ShapeId in CurrentSlide.Shapes)
//   • Effect (Kind + Preset)
//   • Trigger (OnClick / WithPrevious / AfterPrevious)
//
// Per-row controls:
//   ▲ / ▼  Move up / Move down (Editor.MoveAnimation)
//   ✕       Remove (Editor.RemoveAnimation)
//   Trigger ComboBox + Duration field → Editor.SetAnimation (undoable)
//
// Selecting a row selects the target shape on the canvas
// (Editor.Select(shapeId)).
//
// A "▶ Preview" button at the top launches the slide show at the current slide
// (calls MainWindow.StartSlideShow via the provided callback so we never touch
// SlideShowWindow directly — stays within the 16B scope).
//
// Refreshes on Editor.CurrentSlideChanged and Editor.Changed.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// WPF control that displays and edits the animation list for the current slide.
/// Host it in MainWindow as a right-side dock (see 16B SEAM regions in MainWindow).
/// </summary>
public sealed class AnimationPane : Border
{
    // ── Colors (mirrors the FreeP orange theme) ───────────────────────────────────

    private static readonly Brush BackBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
    private static readonly Brush HeaderBg     = Freeze(new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)));
    private static readonly Brush HeaderFg     = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush RowNormal    = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)));
    private static readonly Brush RowSelected  = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xD6)));
    private static readonly Brush RowBorder    = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));
    private static readonly Brush TextBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)));
    private static readonly Brush MutedBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));
    private static readonly Brush ButtonBg     = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)));

    // ── Fields ────────────────────────────────────────────────────────────────────

    private readonly EditingSession _editor;
    private readonly Action?        _onPreview;   // callback → MainWindow.StartSlideShow(false)

    private readonly StackPanel _listPanel;
    private int _selectedRowIndex = -1;   // -1 = none

    // ── Construction ──────────────────────────────────────────────────────────────

    /// <param name="editor">Active editing session.</param>
    /// <param name="onPreview">
    ///   Optional callback called when the "▶ Preview" button is clicked.
    ///   Typically <c>() => mainWindow.StartSlideShow(false)</c>.
    ///   May be null (Preview button is hidden in that case).
    /// </param>
    public AnimationPane(EditingSession editor, Action? onPreview = null)
    {
        _editor    = editor    ?? throw new ArgumentNullException(nameof(editor));
        _onPreview = onPreview;

        Background      = BackBrush;
        BorderBrush     = RowBorder;
        BorderThickness = new Thickness(1, 0, 0, 0);

        _listPanel = new StackPanel { Orientation = Orientation.Vertical };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = _listPanel,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(header);
        root.Children.Add(scroll);

        Child = root;

        // Subscribe to model events.
        _editor.CurrentSlideChanged += (_, _) => Rebuild();
        _editor.Changed             += Rebuild;

        Rebuild();
    }

    // ── Header ────────────────────────────────────────────────────────────────────

    private UIElement BuildHeader()
    {
        var title = new TextBlock
        {
            Text              = "Animation Pane",
            FontSize          = 12,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = HeaderFg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 0, 0),
        };

        var headerPanel = new DockPanel { LastChildFill = true };

        if (_onPreview is not null)
        {
            var previewBtn = new Button
            {
                Content         = "▶",
                ToolTip         = "Preview animations for this slide",
                Padding         = new Thickness(6, 2, 6, 2),
                Margin          = new Thickness(0, 4, 6, 4),
                Background      = Freeze(new SolidColorBrush(Color.FromRgb(0x8F, 0x37, 0x21))),
                Foreground      = Freeze(new SolidColorBrush(Colors.White)),
                BorderThickness = new Thickness(0),
                FontSize        = 12,
            };
            previewBtn.Click += (_, _) => _onPreview();
            DockPanel.SetDock(previewBtn, Dock.Right);
            headerPanel.Children.Add(previewBtn);
        }

        headerPanel.Children.Add(title);

        return new Border
        {
            Background = HeaderBg,
            Padding    = new Thickness(0, 4, 4, 4),
            Child      = headerPanel,
        };
    }

    // ── List rebuild ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fully rebuilds the animation list from the current slide.
    /// Called on CurrentSlideChanged and Changed events.
    /// </summary>
    internal void Rebuild()
    {
        _listPanel.Children.Clear();

        var animations = _editor.CurrentSlideAnimations;
        if (animations.Count == 0)
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text       = "No animations on this slide.",
                FontSize   = 11,
                Foreground = MutedBrush,
                Margin     = new Thickness(10, 12, 10, 12),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        // Clamp selected index to valid range.
        if (_selectedRowIndex >= animations.Count)
            _selectedRowIndex = animations.Count - 1;

        for (int i = 0; i < animations.Count; i++)
        {
            var row = BuildRow(i, animations[i]);
            _listPanel.Children.Add(row);
        }
    }

    // ── Row construction ──────────────────────────────────────────────────────────

    private UIElement BuildRow(int index, ShapeAnimation anim)
    {
        bool selected = index == _selectedRowIndex;

        // ── Order number ────────────────────────────────────────────────────────
        var orderLabel = new TextBlock
        {
            Text              = (index + 1).ToString(),
            FontSize          = 11,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = TextBrush,
            Width             = 20,
            TextAlignment     = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(4, 0, 4, 0),
        };

        // ── Shape name ──────────────────────────────────────────────────────────
        var shapeName = ResolveShapeName(anim.ShapeId);
        var nameLabel = new TextBlock
        {
            Text              = shapeName,
            FontSize          = 11,
            Foreground        = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            MaxWidth          = 80,
            ToolTip           = shapeName,
        };

        // ── Effect label (Kind + Preset) ────────────────────────────────────────
        var effectText = FormatEffect(anim);
        var effectLabel = new TextBlock
        {
            Text              = effectText,
            FontSize          = 10,
            Foreground        = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            MaxWidth          = 70,
            ToolTip           = effectText,
            Margin            = new Thickness(4, 0, 4, 0),
        };

        // ── Trigger dropdown ────────────────────────────────────────────────────
        var triggerCombo = new ComboBox
        {
            FontSize          = 10,
            Width             = 110,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(2, 2, 2, 2),
            ToolTip           = "Trigger",
        };
        triggerCombo.Items.Add("On Click");
        triggerCombo.Items.Add("With Previous");
        triggerCombo.Items.Add("After Previous");
        triggerCombo.SelectedIndex = (int)anim.Trigger;

        // Capture by value for the closure.
        int capturedIndex = index;
        triggerCombo.SelectionChanged += (_, _) =>
        {
            var anims = _editor.CurrentSlideAnimations;
            if (capturedIndex >= anims.Count) return;
            var current = anims[capturedIndex];
            var newTrigger = (AnimationTrigger)triggerCombo.SelectedIndex;
            if (current.Trigger == newTrigger) return;
            var updated = CloneAnimation(current);
            updated.Trigger = newTrigger;
            _editor.SetAnimation(capturedIndex, updated);
        };

        // ── Duration field ──────────────────────────────────────────────────────
        var durationBox = new TextBox
        {
            Text              = FormatDuration(anim.DurationMs),
            FontSize          = 10,
            Width             = 48,
            VerticalAlignment = VerticalAlignment.Center,
            Padding           = new Thickness(2, 1, 2, 1),
            Margin            = new Thickness(2, 2, 2, 2),
            ToolTip           = "Duration (seconds)",
        };
        durationBox.LostFocus += (_, _) =>
        {
            var anims = _editor.CurrentSlideAnimations;
            if (capturedIndex >= anims.Count) return;
            var current = anims[capturedIndex];
            if (TryParseDuration(durationBox.Text, out int ms) && ms != current.DurationMs)
            {
                var updated = CloneAnimation(current);
                updated.DurationMs = ms;
                _editor.SetAnimation(capturedIndex, updated);
            }
            else
            {
                // Revert to current value on parse error.
                durationBox.Text = FormatDuration(current.DurationMs);
            }
        };

        // ── Move up button ──────────────────────────────────────────────────────
        var upBtn = new Button
        {
            Content             = "▲",
            FontSize            = 9,
            Width               = 18,
            Height              = 18,
            Padding             = new Thickness(0),
            Margin              = new Thickness(1),
            Background          = ButtonBg,
            BorderThickness     = new Thickness(1),
            IsEnabled           = index > 0,
            ToolTip             = "Move earlier",
            VerticalAlignment   = VerticalAlignment.Center,
        };
        upBtn.Click += (_, _) =>
        {
            if (capturedIndex > 0)
            {
                _editor.MoveAnimation(capturedIndex, capturedIndex - 1);
                _selectedRowIndex = capturedIndex - 1;
            }
        };

        // ── Move down button ────────────────────────────────────────────────────
        var downBtn = new Button
        {
            Content             = "▼",
            FontSize            = 9,
            Width               = 18,
            Height              = 18,
            Padding             = new Thickness(0),
            Margin              = new Thickness(1),
            Background          = ButtonBg,
            BorderThickness     = new Thickness(1),
            IsEnabled           = index < _editor.CurrentSlideAnimations.Count - 1,
            ToolTip             = "Move later",
            VerticalAlignment   = VerticalAlignment.Center,
        };
        downBtn.Click += (_, _) =>
        {
            var anims = _editor.CurrentSlideAnimations;
            if (capturedIndex < anims.Count - 1)
            {
                _editor.MoveAnimation(capturedIndex, capturedIndex + 1);
                _selectedRowIndex = capturedIndex + 1;
            }
        };

        // ── Remove button ───────────────────────────────────────────────────────
        var removeBtn = new Button
        {
            Content             = "✕",
            FontSize            = 9,
            Width               = 18,
            Height              = 18,
            Padding             = new Thickness(0),
            Margin              = new Thickness(1),
            Foreground          = Freeze(new SolidColorBrush(Color.FromRgb(0xC0, 0x20, 0x20))),
            Background          = ButtonBg,
            BorderThickness     = new Thickness(1),
            ToolTip             = "Remove animation",
            VerticalAlignment   = VerticalAlignment.Center,
        };
        removeBtn.Click += (_, _) =>
        {
            _editor.RemoveAnimation(capturedIndex);
            if (_selectedRowIndex >= _editor.CurrentSlideAnimations.Count)
                _selectedRowIndex = _editor.CurrentSlideAnimations.Count - 1;
        };

        // ── Assemble button cluster ──────────────────────────────────────────────
        var btnPanel = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        btnPanel.Children.Add(upBtn);
        btnPanel.Children.Add(downBtn);
        btnPanel.Children.Add(removeBtn);

        // ── Inner content panel ──────────────────────────────────────────────────
        var innerGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // order
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // name
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // effect
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // trigger
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // duration
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // buttons

        Grid.SetColumn(orderLabel,  0);
        Grid.SetColumn(nameLabel,   1);
        Grid.SetColumn(effectLabel, 2);
        Grid.SetColumn(triggerCombo, 3);
        Grid.SetColumn(durationBox, 4);
        Grid.SetColumn(btnPanel,    5);

        innerGrid.Children.Add(orderLabel);
        innerGrid.Children.Add(nameLabel);
        innerGrid.Children.Add(effectLabel);
        innerGrid.Children.Add(triggerCombo);
        innerGrid.Children.Add(durationBox);
        innerGrid.Children.Add(btnPanel);

        // ── Row border ───────────────────────────────────────────────────────────
        var row = new Border
        {
            Tag             = index,
            Background      = selected ? RowSelected : RowNormal,
            BorderBrush     = RowBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(4, 4, 4, 4),
            Child           = innerGrid,
            Cursor          = System.Windows.Input.Cursors.Hand,
        };

        // Click → select this row and select the shape on the canvas.
        row.MouseLeftButtonDown += (sender, _) =>
        {
            _selectedRowIndex = capturedIndex;
            UpdateRowHighlights();

            var anims = _editor.CurrentSlideAnimations;
            if (capturedIndex < anims.Count)
                _editor.Select(anims[capturedIndex].ShapeId);
        };

        return row;
    }

    // ── Highlight update ──────────────────────────────────────────────────────────

    /// <summary>Updates row backgrounds after a selection change without a full rebuild.</summary>
    private void UpdateRowHighlights()
    {
        for (int i = 0; i < _listPanel.Children.Count; i++)
        {
            if (_listPanel.Children[i] is Border b && b.Tag is int rowIdx)
                b.Background = rowIdx == _selectedRowIndex ? RowSelected : RowNormal;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Looks up the shape name for <paramref name="shapeId"/> on the current slide.</summary>
    private string ResolveShapeName(uint shapeId)
    {
        var slide = _editor.CurrentSlide;
        if (slide is null) return $"Shape {shapeId}";
        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        return string.IsNullOrWhiteSpace(shape?.Name) ? $"Shape {shapeId}" : shape!.Name;
    }

    private static string FormatEffect(ShapeAnimation anim)
    {
        var kindPrefix = anim.Kind switch
        {
            AnimationKind.Entrance  => "In",
            AnimationKind.Exit      => "Out",
            AnimationKind.Emphasis  => "Em",
            AnimationKind.Motion    => "Mv",
            _                       => "?"
        };
        return anim.Kind == AnimationKind.Motion
            ? "Mv: Motion"
            : $"{kindPrefix}: {anim.Preset}";
    }

    private static string FormatDuration(int ms)
    {
        double sec = ms / 1000.0;
        return sec.ToString("0.##");
    }

    private static bool TryParseDuration(string text, out int ms)
    {
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double sec)
            && sec > 0)
        {
            ms = (int)(sec * 1000.0);
            return true;
        }
        ms = 0;
        return false;
    }

    /// <summary>Creates a mutable shallow copy of <paramref name="src"/> for SetAnimation.</summary>
    private static ShapeAnimation CloneAnimation(ShapeAnimation src)
        => new ShapeAnimation
        {
            ShapeId        = src.ShapeId,
            Kind           = src.Kind,
            Preset         = src.Preset,
            Trigger        = src.Trigger,
            DelayMs        = src.DelayMs,
            DurationMs     = src.DurationMs,
            Direction      = src.Direction,
            Motion         = src.Motion,        // motion path is shared (read-only in practice)
            TriggerShapeId = src.TriggerShapeId,
        };

    // ── Static freeze helper ──────────────────────────────────────────────────────

    private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
