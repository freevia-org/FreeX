using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal static class ChartOptionsDialogChrome
{
    private static readonly AvaloniaCompactDialogChromeStyle Style = new(FontFamily.Default);

    public static ChartOptionsDialogForm CreateForm(
        ChartOptionsDialogPlan plan,
        Action accept,
        Action cancel,
        Action<ChartOptionsDialogFieldId>? valueChanged = null) =>
        new(plan, accept, cancel, valueChanged);

    public static Grid CreateRow(string label, Control control, double labelWidth = 150)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(labelWidth, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    public static Grid CreateValueModeRow(
        string label,
        Control value,
        Control mode,
        double labelWidth,
        double valueWidth)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(labelWidth, GridUnitType.Pixel),
                new ColumnDefinition(valueWidth, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        Grid.SetColumn(mode, 2);
        row.Children.Add(mode);
        return row;
    }

    public static StackPanel CreateActionRow(
        string acceptLabel,
        Action accept,
        string cancelLabel,
        Action cancel)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };
        row.Children.Add(CreateButton(acceptLabel, isDefault: true, accept));
        row.Children.Add(CreateButton(cancelLabel, isDefault: false, cancel));
        return row;
    }

    private static Button CreateButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, Style, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}

internal sealed class ChartOptionsDialogForm
{
    private readonly Dictionary<ChartOptionsDialogFieldId, Control> _controls = [];
    private readonly Dictionary<ChartOptionsDialogFieldId, Control> _rows = [];
    private readonly Action<ChartOptionsDialogFieldId>? _valueChanged;
    private bool _applyingPlan;

    public ChartOptionsDialogForm(
        ChartOptionsDialogPlan plan,
        Action accept,
        Action cancel,
        Action<ChartOptionsDialogFieldId>? valueChanged)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentNullException.ThrowIfNull(cancel);

        _valueChanged = valueChanged;
        _applyingPlan = true;
        var body = new StackPanel { Spacing = 8 };
        foreach (var group in plan.Groups)
        {
            if (!string.IsNullOrWhiteSpace(group.Header))
            {
                body.Children.Add(new TextBlock
                {
                    Text = group.Header,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, body.Children.Count == 0 ? 0 : 8, 0, 0),
                });
            }

            foreach (var field in group.Fields)
                body.Children.Add(CreateField(field));
        }

        if (!string.IsNullOrWhiteSpace(plan.Hint))
        {
            body.Children.Add(new TextBlock
            {
                Text = plan.Hint,
                Opacity = 0.7,
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            });
        }

        var root = new Grid
        {
            Margin = new Thickness(14),
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        Control bodyHost = plan.IsScrollable
            ? new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            }
            : body;
        root.Children.Add(bodyHost);

        var actions = ChartOptionsDialogChrome.CreateActionRow(
            plan.AcceptLabel,
            accept,
            plan.CancelLabel,
            cancel);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        Content = root;
        _applyingPlan = false;
    }

    public Control Content { get; }

    public ChartOptionsDialogValues CaptureValues() => new(
        _controls.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                TextBox textBox => new ChartOptionsDialogFieldValue(Text: textBox.Text ?? string.Empty),
                ComboBox comboBox => new ChartOptionsDialogFieldValue(SelectedIndex: comboBox.SelectedIndex),
                CheckBox checkBox => new ChartOptionsDialogFieldValue(IsChecked: checkBox.IsChecked == true),
                _ => throw new InvalidOperationException($"Unsupported chart dialog control: {pair.Value.GetType().Name}."),
            }));

    public string Text(ChartOptionsDialogFieldId fieldId) =>
        Control<TextBox>(fieldId).Text ?? string.Empty;

    public int SelectedIndex(ChartOptionsDialogFieldId fieldId) =>
        Control<ComboBox>(fieldId).SelectedIndex;

    public bool IsChecked(ChartOptionsDialogFieldId fieldId) =>
        Control<CheckBox>(fieldId).IsChecked == true;

    public void SetText(ChartOptionsDialogFieldId fieldId, string? value) =>
        Control<TextBox>(fieldId).Text = value ?? string.Empty;

    public void SetSelectedIndex(ChartOptionsDialogFieldId fieldId, int value) =>
        Control<ComboBox>(fieldId).SelectedIndex = value;

    public void SetChecked(ChartOptionsDialogFieldId fieldId, bool value) =>
        Control<CheckBox>(fieldId).IsChecked = value;

    public void ApplyPlan(ChartOptionsDialogPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _applyingPlan = true;
        try
        {
            foreach (var field in plan.Fields.Values)
            {
                if (!_controls.TryGetValue(field.Id, out var control))
                    continue;

                control.IsEnabled = field.IsEnabled;
                _rows[field.Id].IsVisible = field.IsVisible;
                switch (control)
                {
                    case TextBox textBox:
                        textBox.Text = field.Text;
                        break;
                    case ComboBox comboBox:
                        comboBox.ItemsSource = field.ChoiceLabels;
                        comboBox.SelectedIndex = field.SelectedIndex;
                        break;
                    case CheckBox checkBox:
                        checkBox.IsChecked = field.IsChecked;
                        break;
                }
            }
        }
        finally
        {
            _applyingPlan = false;
        }
    }

    public void Focus(ChartOptionsDialogFieldId fieldId)
    {
        if (_controls.TryGetValue(fieldId, out var control))
            control.Focus();
    }

    private Control CreateField(ChartOptionsDialogFieldPlan field)
    {
        Control control = field.ControlKind switch
        {
            ChartOptionsDialogControlKind.Text => new TextBox { Text = field.Text },
            ChartOptionsDialogControlKind.Choice => CreateChoice(field),
            ChartOptionsDialogControlKind.Toggle => new CheckBox
            {
                Content = field.Label,
                IsChecked = field.IsChecked,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field.ControlKind)),
        };
        control.MinWidth = field.MinimumControlWidth;
        control.IsEnabled = field.IsEnabled;
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        _controls.Add(field.Id, control);

        Control row = field.IsStandalone
            ? control
            : ChartOptionsDialogChrome.CreateRow(field.Label, control, field.LabelWidth);
        row.IsVisible = field.IsVisible;
        _rows.Add(field.Id, row);
        return row;
    }

    private ComboBox CreateChoice(ChartOptionsDialogFieldPlan field)
    {
        var combo = new ComboBox
        {
            ItemsSource = field.ChoiceLabels,
            SelectedIndex = field.SelectedIndex,
        };
        combo.SelectionChanged += (_, _) => RaiseValueChanged(field.Id);
        return combo;
    }

    private TControl Control<TControl>(ChartOptionsDialogFieldId fieldId)
        where TControl : Control =>
        _controls.TryGetValue(fieldId, out var control) && control is TControl typed
            ? typed
            : throw new InvalidOperationException($"{fieldId} is not a {typeof(TControl).Name} field.");

    private void RaiseValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (!_applyingPlan)
            _valueChanged?.Invoke(fieldId);
    }
}
