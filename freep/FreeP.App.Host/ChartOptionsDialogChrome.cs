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
        PresentationDialogActionPlan<ChartOptionsDialogActionId> acceptPlan,
        Action accept,
        PresentationDialogActionPlan<ChartOptionsDialogActionId> cancelPlan,
        Action cancel,
        Thickness rowMargin)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = rowMargin,
        };
        row.Children.Add(CreateButton(acceptPlan, accept));
        row.Children.Add(CreateButton(cancelPlan, cancel));
        return row;
    }

    private static Button CreateButton(
        PresentationDialogActionPlan<ChartOptionsDialogActionId> plan,
        Action action)
    {
        var button = new Button
        {
            Content = plan.Label,
            IsDefault = plan.IsDefault,
            IsCancel = plan.IsCancel,
            MinWidth = 80,
            Margin = new Thickness(4),
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        button.Click += (_, _) => action();
        return button;
    }
}

internal sealed class ChartOptionsDialogForm :
    ChartOptionsDialogFormAdapter<Control, FrameworkElement>
{
    private static readonly ChartOptionsDialogNativeFieldBinding<Control, TextBox, ComboBox, CheckBox>
        FieldBinding = new(
            static (control, value) => control.IsEnabled = value,
            static (control, value) => control.Text = value,
            static (control, value) => control.ItemsSource = value,
            static (control, value) => control.SelectedIndex = value,
            static (control, value) => control.IsChecked = value);

    public ChartOptionsDialogForm(
        ChartOptionsDialogPlan plan,
        Action accept,
        Action cancel,
        Action<ChartOptionsDialogFieldId>? valueChanged)
        : base(
            PresentationDialogControlAdapter.CaptureValue,
            PresentationDialogControlAdapter.ApplyValue,
            FieldBinding.ApplyPlan,
            static (row, isVisible) =>
                row.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed,
            static control => control.Focus(),
            valueChanged)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentNullException.ThrowIfNull(cancel);

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
            plan.AcceptAction,
            accept,
            plan.CancelAction,
            cancel,
            new Thickness(8, 14, 8, 0));
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        Content = root;
        FormSession.CompleteInitialRender();
    }

    public FrameworkElement Content { get; }

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
        PresentationDialogControlAdapter.ApplySemantic(
            control,
            field.AccessibleName,
            field.AutomationId);

        FrameworkElement row = field.IsStandalone
            ? control
            : ChartOptionsDialogChrome.CreateRow(field.Label, control, field.LabelWidth);
        row.Visibility = field.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (field.IsStandalone)
            row.Margin = new Thickness(0, 0, 0, 8);
        FormSession.Register(field.Id, control, row);
        return row;
    }

    private ComboBox CreateChoice(ChartOptionsDialogFieldPlan field)
    {
        var combo = new ComboBox
        {
            ItemsSource = field.ChoiceLabels,
            SelectedIndex = field.SelectedIndex,
        };
        combo.SelectionChanged += (_, _) => NotifyValueChanged(field.Id);
        return combo;
    }
}
