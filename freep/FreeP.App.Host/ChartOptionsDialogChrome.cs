using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class ChartOptionsDialogChrome
{
    public static ChartOptionsDialogForm CreateForm(
        ChartOptionsDialogPlan plan,
        Action accept,
        Action cancel,
        Action<ChartOptionsDialogFieldId>? valueChanged = null) =>
        new(plan, accept, cancel, valueChanged);

    public static StackPanel CreateRow(string label, Control control, double labelWidth = 150)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        row.Children.Add(new Label
        {
            Content = label,
            Width = labelWidth,
            VerticalContentAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(control);
        return row;
    }

    public static StackPanel CreateValueModeRow(
        string valueLabel,
        Control value,
        double valueLabelWidth,
        string modeLabel,
        Control mode,
        double modeLabelWidth)
    {
        var row = CreateRow(valueLabel, value, valueLabelWidth);
        row.Children.Add(new Label
        {
            Content = modeLabel,
            Width = modeLabelWidth,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(mode);
        return row;
    }

    public static Grid CreateTrailingFieldRow(string label, Control control, double fieldWidth)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fieldWidth) });
        row.Children.Add(new Label { Content = label, Padding = new Thickness(0, 2, 8, 2) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    public static StackPanel CreateActionRow(
        string acceptLabel,
        Action accept,
        string cancelLabel,
        Action cancel,
        Thickness rowMargin)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin,
        };
        var ok = new Button
        {
            Content = acceptLabel,
            IsDefault = true,
            MinWidth = 80,
            Margin = new Thickness(4),
        };
        var cancelButton = new Button
        {
            Content = cancelLabel,
            IsCancel = true,
            MinWidth = 80,
            Margin = new Thickness(4),
        };
        ok.Click += (_, _) => accept();
        cancelButton.Click += (_, _) => cancel();
        row.Children.Add(ok);
        row.Children.Add(cancelButton);
        return row;
    }
}

internal sealed class ChartOptionsDialogForm
{
    private readonly Dictionary<ChartOptionsDialogFieldId, Control> _controls = [];
    private readonly Dictionary<ChartOptionsDialogFieldId, FrameworkElement> _rows = [];
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
        var body = new StackPanel();
        foreach (var group in plan.Groups)
        {
            if (!string.IsNullOrWhiteSpace(group.Header))
            {
                body.Children.Add(new TextBlock
                {
                    Text = group.Header,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, body.Children.Count == 0 ? 0 : 8, 0, 8),
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
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                Opacity = 0.7,
            });
        }

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        FrameworkElement bodyHost = plan.IsScrollable
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
            cancel,
            new Thickness(8, 14, 8, 0));
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        Content = root;
        _applyingPlan = false;
    }

    public FrameworkElement Content { get; }

    public ChartOptionsDialogValues CaptureValues() => new(
        _controls.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                TextBox textBox => new ChartOptionsDialogFieldValue(Text: textBox.Text),
                ComboBox comboBox => new ChartOptionsDialogFieldValue(SelectedIndex: comboBox.SelectedIndex),
                CheckBox checkBox => new ChartOptionsDialogFieldValue(IsChecked: checkBox.IsChecked),
                _ => throw new InvalidOperationException($"Unsupported chart dialog control: {pair.Value.GetType().Name}."),
            }));

    public string Text(ChartOptionsDialogFieldId fieldId) =>
        Control<TextBox>(fieldId).Text;

    public int SelectedIndex(ChartOptionsDialogFieldId fieldId) =>
        Control<ComboBox>(fieldId).SelectedIndex;

    public bool IsChecked(ChartOptionsDialogFieldId fieldId) =>
        Control<CheckBox>(fieldId).IsChecked == true;

    public bool? NullableChecked(ChartOptionsDialogFieldId fieldId) =>
        Control<CheckBox>(fieldId).IsChecked;

    public void SetText(ChartOptionsDialogFieldId fieldId, string? value) =>
        Control<TextBox>(fieldId).Text = value ?? string.Empty;

    public void SetSelectedIndex(ChartOptionsDialogFieldId fieldId, int value) =>
        Control<ComboBox>(fieldId).SelectedIndex = value;

    public void SetChecked(ChartOptionsDialogFieldId fieldId, bool? value) =>
        Control<CheckBox>(fieldId).IsChecked = value;

    public void SetChoices(
        ChartOptionsDialogFieldId fieldId,
        IReadOnlyList<string> choices,
        int selectedIndex)
    {
        var combo = Control<ComboBox>(fieldId);
        combo.ItemsSource = choices;
        combo.SelectedIndex = selectedIndex;
    }

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
                _rows[field.Id].Visibility = field.IsVisible ? Visibility.Visible : Visibility.Collapsed;
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

    private FrameworkElement CreateField(ChartOptionsDialogFieldPlan field)
    {
        Control control = field.ControlKind switch
        {
            ChartOptionsDialogControlKind.Text => new TextBox { Text = field.Text },
            ChartOptionsDialogControlKind.Choice => CreateChoice(field),
            ChartOptionsDialogControlKind.Toggle => new CheckBox
            {
                Content = field.Label,
                IsChecked = field.IsChecked,
                IsThreeState = field.IsThreeState,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field.ControlKind)),
        };
        control.MinWidth = field.MinimumControlWidth;
        control.IsEnabled = field.IsEnabled;
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        _controls.Add(field.Id, control);

        FrameworkElement row = field.IsStandalone
            ? control
            : ChartOptionsDialogChrome.CreateRow(field.Label, control, field.LabelWidth);
        row.Visibility = field.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (field.IsStandalone)
            row.Margin = new Thickness(0, 0, 0, 8);
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
