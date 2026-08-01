using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class MotionPathEditorDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly int _animationIndex;
    private readonly string _origin;
    private readonly string? _ptsTypes;
    private readonly StackPanel _rowsPanel = new();
    private readonly List<Row> _rows = new();

    public MotionPathEditorDialog(EditingSession editor, int animationIndex)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var plan = MotionPathEditingPlanner.BuildPlan(editor.CurrentSlideAnimations, animationIndex);
        if (!plan.CanEdit)
            throw new InvalidOperationException(plan.Message);

        _animationIndex = animationIndex;
        _origin = plan.Origin;
        _ptsTypes = plan.PtsTypes;
        Title = "Edit Motion Path";
        Width = 760;
        Height = 520;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var segment in plan.Segments)
            _rows.Add(new Row(segment));

        Content = BuildContent();
        RenderRows();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(false);
            e.Handled = true;
        };
    }

    private Control BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        var intro = new TextBlock
        {
            Text = "Coordinates are relative to the animated shape. Edit endpoints and curve control points, then press OK.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom);
        var addLine = Button("Add line", 82);
        addLine.Click += (_, _) => AddSegment(MotionPathEditingPlanner.CreateLineAfter);
        var addCurve = Button("Add curve", 82);
        addCurve.Click += (_, _) => AddSegment(MotionPathEditingPlanner.CreateCubicAfter);
        var ok = Button("OK", 80);
        ok.Click += (_, _) => Apply();
        var cancel = Button("Cancel", 80);
        cancel.Click += (_, _) => Close(false);
        actions.Children.Add(addLine);
        actions.Children.Add(addCurve);
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        root.Children.Add(actions);

        root.Children.Add(new ScrollViewer
        {
            Content = _rowsPanel,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        return root;
    }

    private void AddSegment(Func<IReadOnlyList<MotionPathSegmentEdit>, MotionPathSegmentEdit> factory)
    {
        ReadRowsOrEmpty();
        _rows.Add(new Row(factory(_rows.Select(row => row.Value).ToArray())));
        RenderRows();
    }

    private void RenderRows()
    {
        _rowsPanel.Children.Clear();
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            row.Build(index == 0, () =>
            {
                if (index == 0)
                    return;
                ReadRowsOrEmpty();
                _rows.RemoveAt(index);
                RenderRows();
            });
            _rowsPanel.Children.Add(row.Control!);
        }
    }

    private bool ReadRowsOrEmpty()
    {
        try
        {
            foreach (var row in _rows)
                row.Read();
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void Apply()
    {
        try
        {
            foreach (var row in _rows)
                row.Read();
            if (!MotionPathEditingPlanner.TryApply(_editor, _animationIndex, _rows.Select(row => row.Value), _origin, _ptsTypes, out _))
                return;
            Close(true);
        }
        catch (FormatException)
        {
            // Leave invalid input visible for correction.
        }
    }

    private static Button Button(string text, double minWidth)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), MinWidth = minWidth };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: minWidth);
        return button;
    }

    private sealed class Row
    {
        private readonly ComboBox _kind = new();
        private readonly TextBox _x = Box();
        private readonly TextBox _y = Box();
        private readonly TextBox _x1 = Box();
        private readonly TextBox _y1 = Box();
        private readonly TextBox _x2 = Box();
        private readonly TextBox _y2 = Box();
        private MotionPathSegmentEdit _value;
        public Control? Control { get; private set; }
        public MotionPathSegmentEdit Value => _value;

        public Row(MotionPathSegmentEdit value) => _value = value;

        public void Build(bool first, Action remove)
        {
            _kind.ItemsSource = Enum.GetValues<MotionPathSegmentKind>();
            _kind.SelectedItem = _value.Kind;
            _kind.IsEnabled = !first;
            _kind.Width = 78;
            _kind.Margin = new Thickness(2);
            _kind.SelectionChanged += (_, _) => UpdateControlState();
            Set(_x, _value.X);
            Set(_y, _value.Y);
            Set(_x1, _value.X1);
            Set(_y1, _value.Y1);
            Set(_x2, _value.X2);
            Set(_y2, _value.Y2);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            panel.Children.Add(new TextBlock { Text = first ? "Start" : "Segment", Width = 62, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(_kind);
            panel.Children.Add(Labeled("X", _x));
            panel.Children.Add(Labeled("Y", _y));
            panel.Children.Add(Labeled("X1", _x1));
            panel.Children.Add(Labeled("Y1", _y1));
            panel.Children.Add(Labeled("X2", _x2));
            panel.Children.Add(Labeled("Y2", _y2));
            var delete = Button("Delete", 58);
            delete.IsEnabled = !first;
            delete.Click += (_, _) => remove();
            panel.Children.Add(delete);
            Control = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = panel,
            };
            UpdateControlState();
        }

        public void Read()
        {
            var kind = _kind.SelectedItem is MotionPathSegmentKind selected ? selected : MotionPathSegmentKind.Line;
            _value = new MotionPathSegmentEdit(kind, Parse(_x, "X"), Parse(_y, "Y"), Parse(_x1, "X1"), Parse(_y1, "Y1"), Parse(_x2, "X2"), Parse(_y2, "Y2"));
        }

        private void UpdateControlState()
        {
            var curve = _kind.SelectedItem is MotionPathSegmentKind.Cubic;
            foreach (var box in new[] { _x1, _y1, _x2, _y2 })
                box.IsEnabled = curve;
            var close = _kind.SelectedItem is MotionPathSegmentKind.Close;
            _x.IsEnabled = !close;
            _y.IsEnabled = !close;
        }

        private static TextBox Box() => new() { Width = 54, Margin = new Thickness(1) };
        private static void Set(TextBox box, double value) => box.Text = value.ToString("G", CultureInfo.CurrentCulture);
        private static double Parse(TextBox box, string name)
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
                return value;
            throw new FormatException($"{name} must be a number.");
        }
        private static StackPanel Labeled(string label, TextBox box) => new() { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, box } };
    }
}
