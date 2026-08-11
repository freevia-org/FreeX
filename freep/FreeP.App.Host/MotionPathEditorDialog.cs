using System.Windows;
using System.Windows.Automation;
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
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 720;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);

        foreach (var segment in _session.InitialSegments)
            _rows.Add(new Row(segment, surface));

        var addLine = MakeActionButton(
            surface.Action(MotionPathEditorDialogAction.AddLine),
            () => ApplyTransition(_session.AddLine(ReadRowInputs())));
        var addCurve = MakeActionButton(
            surface.Action(MotionPathEditorDialogAction.AddCurve),
            () => ApplyTransition(_session.AddCurve(ReadRowInputs())));
        var ok = MakeActionButton(
            surface.Action(MotionPathEditorDialogAction.Accept),
            () => ApplyTransition(_session.Submit(ReadRowInputs())));
        var cancel = MakeActionButton(
            surface.Action(MotionPathEditorDialogAction.Cancel),
            Close);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(addLine);
        actions.Children.Add(addCurve);
        actions.Children.Add(ok);
        actions.Children.Add(cancel);

        var root = new DockPanel { Margin = new Thickness(10) };
        var intro = new TextBlock
        {
            Text = surface.Introduction,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        ApplySemantic(intro, surface.Field(MotionPathEditorDialogField.Introduction));
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
            var rowIndex = index;
            row.Build(index, () =>
                ApplyTransition(_session.Remove(ReadRowInputs(), rowIndex)));
            _rowsPanel.Children.Add(row.Control!);
        }
    }

    private IReadOnlyList<MotionPathEditorRowInput> ReadRowInputs() =>
        _rows.Select(row => row.ReadInput()).ToArray();

    private void ApplyTransition(MotionPathEditorDialogTransition transition)
    {
        if (transition.ShouldRenderRows)
        {
            _rows.Clear();
            foreach (var segment in transition.Segments)
                _rows.Add(new Row(segment, _session.Surface));
            RenderRows();
        }

        if (!transition.Succeeded)
        {
            DialogMessageHelper.ShowWarning(this, transition.ValidationMessage, Title);
            return;
        }

        if (transition.ShouldClose)
            DialogResult = true;
    }

    private static Button MakeActionButton(
        PresentationDialogActionPlan<MotionPathEditorDialogAction> action,
        Action handler)
    {
        var button = new Button
        {
            Content = action.Label,
            IsDefault = action.IsDefault,
            IsCancel = action.IsCancel,
            Margin = new Thickness(4),
            MinWidth = action.Id == MotionPathEditorDialogAction.Delete ? 58 : 80,
        };
        ApplyAction(button, action);
        button.Click += (_, _) => handler();
        return button;
    }

    private static void ApplySemantic(
        DependencyObject control,
        PresentationDialogFieldPlan<MotionPathEditorDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
    }

    private static void ApplyAction(
        DependencyObject control,
        PresentationDialogActionPlan<MotionPathEditorDialogAction> action)
    {
        AutomationProperties.SetName(control, action.AccessibleName);
        AutomationProperties.SetAutomationId(control, action.AutomationId);
    }

    private sealed class Row
    {
        private readonly MotionPathEditorDialogSurfacePlan _surface;
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

        public Row(
            MotionPathSegmentEdit value,
            MotionPathEditorDialogSurfacePlan surface)
        {
            _value = value;
            _surface = surface;
        }

        public void Build(int rowIndex, Action remove)
        {
            var plan = MotionPathEditorRowProjection.BuildPlan(_surface, _value, rowIndex);
            _isFirst = plan.RowIndex == 0;
            _kind.ItemsSource = _surface.SegmentKinds;
            _kind.SelectedItem = plan.Kind;
            _kind.IsEnabled = plan.Enablement.KindEnabled;
            _kind.Width = 76;
            _kind.Margin = new Thickness(2);
            _kind.SelectionChanged += (_, _) => UpdateControlState();
            ApplySemantic(
                _kind,
                _surface.Field(MotionPathEditorDialogField.SegmentKind, plan.RowIndex));
            Set(_x, plan.X);
            Set(_y, plan.Y);
            Set(_x1, plan.X1);
            Set(_y1, plan.Y1);
            Set(_x2, plan.X2);
            Set(_y2, plan.Y2);
            ApplySemantic(_x, _surface.Field(MotionPathEditorDialogField.X, plan.RowIndex));
            ApplySemantic(_y, _surface.Field(MotionPathEditorDialogField.Y, plan.RowIndex));
            ApplySemantic(_x1, _surface.Field(MotionPathEditorDialogField.X1, plan.RowIndex));
            ApplySemantic(_y1, _surface.Field(MotionPathEditorDialogField.Y1, plan.RowIndex));
            ApplySemantic(_x2, _surface.Field(MotionPathEditorDialogField.X2, plan.RowIndex));
            ApplySemantic(_y2, _surface.Field(MotionPathEditorDialogField.Y2, plan.RowIndex));

            var grid = new Grid { Margin = new Thickness(2) };
            foreach (var width in new[] { 76.0, 78.0, 78.0, 78.0, 78.0, 78.0, 78.0, 52.0, 58.0 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            Add(grid, new TextBlock { Text = plan.RowLabel, VerticalAlignment = VerticalAlignment.Center }, 0);
            Add(grid, _kind, 1);
            Add(grid, Labeled(_surface.XLabel, _x), 2);
            Add(grid, Labeled(_surface.YLabel, _y), 3);
            Add(grid, Labeled(_surface.X1Label, _x1), 4);
            Add(grid, Labeled(_surface.Y1Label, _y1), 5);
            Add(grid, Labeled(_surface.X2Label, _x2), 6);
            Add(grid, Labeled(_surface.Y2Label, _y2), 7);
            var removeButton = MakeActionButton(
                _surface.Action(MotionPathEditorDialogAction.Delete, plan.RowIndex),
                remove);
            removeButton.Margin = new Thickness(2);
            removeButton.IsEnabled = plan.Enablement.DeleteEnabled;
            Grid.SetColumn(removeButton, 8);
            grid.Children.Add(removeButton);
            Control = new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
            UpdateControlState();
        }

        public MotionPathEditorRowInput ReadInput() => new(
            _kind.SelectedItem is MotionPathSegmentKind selected ? selected : null,
            _x.Text,
            _y.Text,
            _x1.Text,
            _y1.Text,
            _x2.Text,
            _y2.Text);

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
        private static void Set(TextBox box, string value) => box.Text = value;

        private static void Add(Grid grid, UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
        private static StackPanel Labeled(string label, TextBox box) => new() { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, box } };
    }
}
