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
    private static readonly AvaloniaCompactDialogChromeStyle Style =
        AvaloniaCompactDialogChrome.WindowsStyle;

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
        PresentationDialogActionPlan<ChartOptionsDialogActionId> acceptPlan,
        Action accept,
        PresentationDialogActionPlan<ChartOptionsDialogActionId> cancelPlan,
        Action cancel)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
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
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            Style,
            minWidth: 80,
            isDefault: plan.IsDefault);
        button.Click += (_, _) => action();
        return button;
    }
}

internal sealed class ChartOptionsDialogForm :
    ChartOptionsDialogFormAdapter<Control, Control>
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
            static (row, isVisible) => row.IsVisible = isVisible,
            static control => control.Focus(),
            valueChanged)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentNullException.ThrowIfNull(cancel);

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
            plan.AcceptAction,
            accept,
            plan.CancelAction,
            cancel);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        Content = root;
        FormSession.CompleteInitialRender();
    }

    public Control Content { get; }

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

        Control row = field.IsStandalone
            ? control
            : ChartOptionsDialogChrome.CreateRow(field.Label, control, field.LabelWidth);
        row.IsVisible = field.IsVisible;
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
