using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Small, host-local editor for the normalized segments of a motion path.</summary>
public sealed class MotionPathEditorDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly MotionPathEditorDialogSession _session;
    private readonly StackPanel _rowsPanel = new();
    private readonly List<Row> _rows = new();

    public MotionPathEditorDialog(EditingSession editor, int animationIndex)
    {
        _session = new MotionPathEditorDialogSession(editor, animationIndex);
        Title = "Edit Motion Path";
        Width = 720;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        foreach (var segment in _session.InitialSegments)
            _rows.Add(new Row(segment));

        var addLine = new Button { Content = "Add line", Margin = new Thickness(4), MinWidth = 80 };
        addLine.Click += (_, _) =>
        {
            _rows.Add(new Row(_session.CreateLineAfter(ReadRowsOrEmpty())));
            RenderRows();
        };
        var addCurve = new Button { Content = "Add curve", Margin = new Thickness(4), MinWidth = 80 };
        addCurve.Click += (_, _) =>
        {
            _rows.Add(new Row(_session.CreateCubicAfter(ReadRowsOrEmpty())));
            RenderRows();
        };

        var ok = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(4), MinWidth = 80 };
        ok.Click += (_, _) => Apply();
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(4), MinWidth = 80 };
        cancel.Click += (_, _) => Close();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(addLine);
        actions.Children.Add(addCurve);
        actions.Children.Add(ok);
        actions.Children.Add(cancel);

        var root = new DockPanel { Margin = new Thickness(10) };
        var intro = new TextBlock
        {
            Text = "Coordinates are relative to the animated shape. Edit endpoints and curve control points, then press OK.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(intro);
        root.Children.Add(actions);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rowsPanel });
        Content = root;
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
                if (!MotionPathEditorRowProjection.CanRemove(index))
                    return;
                ReadRows();
                _rows.RemoveAt(index);
                RenderRows();
            });
            _rowsPanel.Children.Add(row.Control!);
        }
    }

    private IReadOnlyList<MotionPathSegmentEdit> ReadRowsOrEmpty()
    {
        try
        {
            ReadRows();
            return _rows.Select(row => row.Value).ToArray();
        }
        catch (FormatException)
        {
            return Array.Empty<MotionPathSegmentEdit>();
        }
    }

    private void ReadRows()
    {
        foreach (var row in _rows)
            row.Read();
    }

    private void Apply()
    {
        try
        {
            ReadRows();
            if (!_session.TryApply(_rows.Select(row => row.Value), out var error))
            {
                MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        private bool _isFirst;
        public UIElement? Control { get; private set; }
        public MotionPathSegmentEdit Value => _value;

        public Row(MotionPathSegmentEdit value)
        {
            _value = value;
        }

        public void Build(bool first, Action remove)
        {
            _isFirst = first;
            _kind.ItemsSource = Enum.GetValues<MotionPathSegmentKind>();
            _kind.SelectedItem = _value.Kind;
            _kind.IsEnabled = MotionPathEditorRowProjection
                .BuildEnablement(_value.Kind, first)
                .KindEnabled;
            _kind.Width = 76;
            _kind.Margin = new Thickness(2);
            _kind.SelectionChanged += (_, _) => UpdateControlState();
            Set(_x, _value.X);
            Set(_y, _value.Y);
            Set(_x1, _value.X1);
            Set(_y1, _value.Y1);
            Set(_x2, _value.X2);
            Set(_y2, _value.Y2);

            var grid = new Grid { Margin = new Thickness(2) };
            foreach (var width in new[] { 76.0, 78.0, 78.0, 78.0, 78.0, 78.0, 78.0, 52.0, 58.0 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            Add(grid, new TextBlock { Text = first ? "Start" : "Segment", VerticalAlignment = VerticalAlignment.Center }, 0);
            Add(grid, _kind, 1);
            Add(grid, Labeled("X", _x), 2);
            Add(grid, Labeled("Y", _y), 3);
            Add(grid, Labeled("X1", _x1), 4);
            Add(grid, Labeled("Y1", _y1), 5);
            Add(grid, Labeled("X2", _x2), 6);
            Add(grid, Labeled("Y2", _y2), 7);
            var removeButton = new Button
            {
                Content = "Delete",
                Margin = new Thickness(2),
                IsEnabled = MotionPathEditorRowProjection
                    .BuildEnablement(_value.Kind, first)
                    .DeleteEnabled,
            };
            removeButton.Click += (_, _) => remove();
            Grid.SetColumn(removeButton, 8);
            grid.Children.Add(removeButton);
            Control = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
            UpdateControlState();
        }

        public void Read()
        {
            var kind = _kind.SelectedItem is MotionPathSegmentKind selected ? selected : MotionPathSegmentKind.Line;
            if (!MotionPathEditorRowProjection.TryParse(
                    kind,
                    _x.Text,
                    _y.Text,
                    _x1.Text,
                    _y1.Text,
                    _x2.Text,
                    _y2.Text,
                    out _value,
                    out var error))
            {
                throw new FormatException(error);
            }
        }

        private void UpdateControlState()
        {
            var kind = _kind.SelectedItem is MotionPathSegmentKind selected
                ? selected
                : MotionPathSegmentKind.Line;
            var enablement = MotionPathEditorRowProjection.BuildEnablement(
                kind,
                _isFirst);
            _kind.IsEnabled = enablement.KindEnabled;
            foreach (var box in new[] { _x1, _y1, _x2, _y2 })
                box.IsEnabled = enablement.ControlPointsEnabled;
            _x.IsEnabled = enablement.EndpointEnabled;
            _y.IsEnabled = enablement.EndpointEnabled;
        }

        private static TextBox Box() => new() { Width = 52, Margin = new Thickness(1), Padding = new Thickness(2, 1, 2, 1) };
        private static void Set(TextBox box, double value) =>
            box.Text = MotionPathEditorRowProjection.Format(value);

        private static void Add(Grid grid, UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
        private static StackPanel Labeled(string label, TextBox box) => new() { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, box } };
    }
}
