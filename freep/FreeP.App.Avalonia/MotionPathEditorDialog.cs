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

internal sealed class MotionPathEditorDialog : FreePDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly MotionPathEditorDialogSession _session;
    private readonly MotionPathEditorDialogFormSession<Row> _formSession;
    private readonly StackPanel _rowsPanel = new();
    private readonly TextBlock _validationText = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };

    public MotionPathEditorDialog(EditingSession editor, int animationIndex)
    {
        _session = new MotionPathEditorDialogSession(editor, animationIndex);
        _formSession = new(
            _session,
            segment => new Row(segment, _session.Surface),
            row => row.ReadInput(),
            (row, index, remove) => row.Build(index, remove),
            _rowsPanel.Children.Clear,
            row => _rowsPanel.Children.Add(row.Control!),
            (message, _) => _validationText.Text = message,
            () => Close(true));
        var surface = _session.Surface;
        Title = surface.Title;
        Width = 760;
        Height = 520;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);
        PresentationDialogControlAdapter.ApplySemantic(
            _validationText,
            surface.Field(MotionPathEditorDialogField.Validation));
        Content = BuildContent();
        _formSession.RenderInitial();
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
        PresentationDialogControlAdapter.ApplySemantic(intro, surface.Field(MotionPathEditorDialogField.Introduction));
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom);
        var addLine = Button(surface.Action(MotionPathEditorDialogAction.AddLine), 82);
        addLine.Click += (_, _) => _formSession.AddLine();
        var addCurve = Button(surface.Action(MotionPathEditorDialogAction.AddCurve), 82);
        addCurve.Click += (_, _) => _formSession.AddCurve();
        var ok = Button(surface.Action(MotionPathEditorDialogAction.Accept), 80);
        ok.Click += (_, _) => _formSession.Submit();
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
        private readonly MotionPathEditorNativeRowSession<ComboBox, TextBox> _native;
        private MotionPathSegmentEdit _value;
        public Control? Control { get; private set; }

        public Row(
            MotionPathSegmentEdit value,
            MotionPathEditorDialogSurfacePlan surface)
        {
            _value = value;
            _surface = surface;
            _native = new(
                _kind,
                [_x, _y, _x1, _y1, _x2, _y2],
                static control => control.SelectedItem as MotionPathSegmentKind?,
                static (control, kinds) => control.ItemsSource = kinds,
                static (control, kind) => control.SelectedItem = kind,
                static control => control.Text,
                static (control, text) => control.Text = text,
                static (control, enabled) => ((Control)control).IsEnabled = enabled);
        }

        public void Build(int rowIndex, Action remove)
        {
            var plan = _native.Initialize(_surface, _value, rowIndex);
            _kind.Width = 78;
            _kind.Margin = new Thickness(2);
            _kind.SelectionChanged += (_, _) => _native.RefreshEnablement();
            PresentationDialogControlAdapter.ApplySemantic(
                _kind,
                _surface.Field(MotionPathEditorDialogField.SegmentKind, plan.RowIndex));
            PresentationDialogControlAdapter.ApplySemantic(_x, _surface.Field(MotionPathEditorDialogField.X, plan.RowIndex));
            PresentationDialogControlAdapter.ApplySemantic(_y, _surface.Field(MotionPathEditorDialogField.Y, plan.RowIndex));
            PresentationDialogControlAdapter.ApplySemantic(_x1, _surface.Field(MotionPathEditorDialogField.X1, plan.RowIndex));
            PresentationDialogControlAdapter.ApplySemantic(_y1, _surface.Field(MotionPathEditorDialogField.Y1, plan.RowIndex));
            PresentationDialogControlAdapter.ApplySemantic(_x2, _surface.Field(MotionPathEditorDialogField.X2, plan.RowIndex));
            PresentationDialogControlAdapter.ApplySemantic(_y2, _surface.Field(MotionPathEditorDialogField.Y2, plan.RowIndex));

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
        }

        public MotionPathEditorRowInput ReadInput() => _native.CaptureInput();

        private static TextBox Box() => new() { Width = 54, Margin = new Thickness(1) };

        private static StackPanel Labeled(string label, TextBox box) => new() { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, box } };
    }
}
