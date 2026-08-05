using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ZoomObjectPropertiesDialog : Window
{
    private readonly ZoomObjectPropertiesDialogSession _session;
    private readonly ZoomObjectPropertiesDialogSurfacePlan _surface;
    private readonly Dictionary<ZoomObjectPropertiesDialogField, Control> _controls = [];
    private bool _applyingState;

    internal ZoomObjectProperties Properties => _session.CommitPlan.Properties;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout =>
        _session.CommitPlan.SummaryTileLayout;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties =>
        _session.CommitPlan.SummaryTileProperties;
    internal bool ApplySummaryPropertiesToAllTiles =>
        _session.CommitPlan.ApplySummaryPropertiesToAllTiles;

    internal ZoomObjectPropertiesDialog(
        ZoomObjectProperties current,
        IReadOnlyList<SummaryZoomTarget>? summaryTargets = null,
        IReadOnlyList<ZoomObjectProperties>? summaryTileProperties = null)
    {
        _session = new ZoomObjectPropertiesDialogSession(current, summaryTargets, summaryTileProperties);
        _surface = _session.Surface;
        var layout = _surface.Layout;
        Title = _surface.Chrome.Title;
        Width = _surface.Chrome.Width;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this);

        foreach (var plan in _session.FieldCatalog)
            _controls.Add(plan.Field, CreateControl(plan, layout.InputMinWidth));

        var children = new List<Control>();
        foreach (var plan in _session.FieldCatalog)
        {
            var control = _controls[plan.Field];
            children.Add(plan.Kind == ZoomObjectPropertiesDialogControlKind.Toggle
                ? control
                : Row(plan.Label, control, layout.LabelWidth));
        }

        var ok = ZoomDialogChrome.MakeButton(_surface.Chrome.AcceptLabel, true, Apply);
        children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                ok,
                ZoomDialogChrome.MakeButton(
                    _surface.Chrome.CancelLabel,
                    false,
                    () => Close(false)),
            },
        });
        var content = new StackPanel
        {
            Margin = new Thickness(layout.ContentMargin),
            Spacing = 8,
        };
        foreach (var child in children)
            content.Children.Add(child);
        Content = content;
        ApplyState(_session.State);
    }

    private Control CreateControl(
        ZoomObjectPropertiesDialogControlPlan plan,
        double inputMinWidth)
    {
        switch (plan.Kind)
        {
            case ZoomObjectPropertiesDialogControlKind.Toggle:
            {
                var checkBox = new CheckBox
                {
                    Content = plan.Label,
                    Margin = plan.Field ==
                        ZoomObjectPropertiesDialogField.ApplySummaryPropertiesToAllTiles
                            ? new Thickness(0, 4, 0, 0)
                            : default,
                };
                checkBox.IsCheckedChanged += (_, _) =>
                    Dispatch(plan.Field, checkBox.IsChecked == true);
                return checkBox;
            }
            case ZoomObjectPropertiesDialogControlKind.Text:
            {
                var textBox = new TextBox
                {
                    MinWidth = inputMinWidth,
                    PlaceholderText = plan.PlaceholderText,
                };
                textBox.TextChanged += (_, _) => Dispatch(plan.Field, textBox.Text);
                return textBox;
            }
            case ZoomObjectPropertiesDialogControlKind.Choice:
            {
                var comboBox = new ComboBox
                {
                    ItemsSource = plan.Options,
                    MinWidth = inputMinWidth,
                };
                comboBox.SelectionChanged += (_, _) => Dispatch(plan.Field, comboBox.SelectedItem);
                return comboBox;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(plan));
        }
    }

    private static StackPanel Row(string label, Control control, double labelWidth) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new TextBlock
            {
                Text = label,
                Width = labelWidth,
                VerticalAlignment = VerticalAlignment.Center,
            },
            control,
        },
    };

    private void Dispatch(ZoomObjectPropertiesDialogField field, object? value)
    {
        if (_applyingState)
            return;

        ApplyState(_session.Dispatch(new ZoomObjectPropertiesDialogAction(field, value)));
    }

    private void ApplyState(ZoomObjectPropertiesDialogState state)
    {
        _applyingState = true;
        try
        {
            foreach (var fieldState in state.Fields)
            {
                if (!_controls.TryGetValue(fieldState.Field, out var control))
                    continue;

                control.IsEnabled = fieldState.IsEnabled;
                switch (control)
                {
                    case CheckBox checkBox when fieldState.Value is bool isChecked:
                        if (checkBox.IsChecked != isChecked)
                            checkBox.IsChecked = isChecked;
                        break;
                    case TextBox textBox:
                    {
                        var text = fieldState.Value?.ToString() ?? string.Empty;
                        if (!string.Equals(textBox.Text, text, StringComparison.Ordinal))
                            textBox.Text = text;
                        break;
                    }
                    case ComboBox comboBox:
                        if (!Equals(comboBox.SelectedItem, fieldState.Value))
                            comboBox.SelectedItem = fieldState.Value;
                        break;
                }
            }
        }
        finally
        {
            _applyingState = false;
        }
    }

    private async void Apply()
    {
        if (!_session.TryAccept(out var validation))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                validation!.Message,
                _surface.Chrome.Title);
            FocusValidationField(validation.Field);
            return;
        }

        Close(true);
    }

    private void FocusValidationField(ZoomObjectPropertiesDialogField field)
    {
        if (!_controls.TryGetValue(field, out var control))
            return;

        control.Focus();
        if (control is TextBox textBox)
            textBox.SelectAll();
    }
}
