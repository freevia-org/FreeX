using Avalonia;
using Avalonia.Automation;
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
    private readonly MotionPathEditorDialogSession _session;
    private readonly StackPanel _rowsPanel = new();
    private readonly List<Row> _rows = new();
    private readonly TextBlock _validationText = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };

    public MotionPathEditorDialog(EditingSession editor, int animationIndex)
    {
        _session = new MotionPathEditorDialogSession(editor, animationIndex);
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 760;
        Height = 520;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);
        ApplySemantic(
            _validationText,
            surface.Field(MotionPathEditorDialogField.Validation));
        foreach (var segment in _session.InitialSegments)
            _rows.Add(new Row(segment, surface));

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
        var surface = _session.Surface;
        var root = new DockPanel { Margin = new Thickness(12) };
        var intro = new TextBlock
        {
            Text = surface.Introduction,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        ApplySemantic(intro, surface.Field(MotionPathEditorDialogField.Introduction));
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom);
        var addLine = Button(surface.Action(MotionPathEditorDialogAction.AddLine), 82);
        addLine.Click += (_, _) => ApplyTransition(_session.AddLine(ReadRowInputs()));
        var addCurve = Button(surface.Action(MotionPathEditorDialogAction.AddCurve), 82);
        addCurve.Click += (_, _) => ApplyTransition(_session.AddCurve(ReadRowInputs()));
        var ok = Button(surface.Action(MotionPathEditorDialogAction.Accept), 80);
        ok.Click += (_, _) => ApplyTransition(_session.Submit(ReadRowInputs()));
        var cancel = Button(surface.Action(MotionPathEditorDialogAction.Cancel), 80);
        cancel.Click += (_, _) => Close(false);
        actions.Children.Add(addLine);
        actions.Children.Add(addCurve);
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        root.Children.Add(actions);

        DockPanel.SetDock(_validationText, Dock.Bottom);
        root.Children.Add(_validationText);

        root.Children.Add(new ScrollViewer
        {
            Content = _rowsPanel,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        return root;
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

        _validationText.Text = transition.ValidationMessage;
        if (transition.ShouldClose)
            Close(true);
    }

    private static Button Button(
        PresentationDialogActionPlan<MotionPathEditorDialogAction> action,
        double minWidth)
    {
        var button = new Button
        {
            Content = action.Label,
            IsDefault = action.IsDefault,
            IsCancel = action.IsCancel,
            Margin = new Thickness(4),
            MinWidth = minWidth,
        };
        ApplyAction(button, action);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: minWidth,
            isDefault: action.IsDefault);
        return button;
    }

    private static void ApplySemantic(
        Control control,
        PresentationDialogFieldPlan<MotionPathEditorDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
    }

    private static void ApplyAction(
        Control control,
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
        public Control? Control { get; private set; }

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
            _kind.Width = 78;
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

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            panel.Children.Add(new TextBlock { Text = plan.RowLabel, Width = 62, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(_kind);
            panel.Children.Add(Labeled(_surface.XLabel, _x));
            panel.Children.Add(Labeled(_surface.YLabel, _y));
            panel.Children.Add(Labeled(_surface.X1Label, _x1));
            panel.Children.Add(Labeled(_surface.Y1Label, _y1));
            panel.Children.Add(Labeled(_surface.X2Label, _x2));
            panel.Children.Add(Labeled(_surface.Y2Label, _y2));
            var delete = Button(
                _surface.Action(MotionPathEditorDialogAction.Delete, plan.RowIndex),
                58);
            delete.IsEnabled = plan.Enablement.DeleteEnabled;
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

        private static TextBox Box() => new() { Width = 54, Margin = new Thickness(1) };
        private static void Set(TextBox box, string value) => box.Text = value;

        private static StackPanel Labeled(string label, TextBox box) => new() { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, box } };
    }
}
