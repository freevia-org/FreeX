using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class ZoomObjectPropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
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
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, _surface.Chrome.AccessibleName);
        AutomationProperties.SetAutomationId(this, _surface.Chrome.AutomationId);

        foreach (var plan in _session.FieldCatalog)
            _controls.Add(plan.Field, CreateControl(plan, layout.InputMinWidth));

        var grid = new Grid { Margin = new Thickness(layout.ContentMargin) };
        for (var index = 0; index <= _session.FieldCatalog.Count; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        foreach (var plan in _session.FieldCatalog)
        {
            var control = _controls[plan.Field];
            if (plan.Kind == ZoomObjectPropertiesDialogControlKind.Toggle)
                AddToggleRow(grid, row++, control);
            else
                AddRow(grid, row++, plan.Label, control);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var ok = new Button
        {
            Content = _surface.Chrome.AcceptLabel,
            IsDefault = true,
            MinWidth = 75,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ApplyAction(
            ok,
            _surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Accept));
        ok.Click += (_, _) => Apply();
        buttons.Children.Add(ok);
        var cancel = new Button
        {
            Content = _surface.Chrome.CancelLabel,
            IsCancel = true,
            MinWidth = 75,
        };
        ApplyAction(
            cancel,
            _surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Cancel));
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, row);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
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
                ApplySemantic(checkBox, plan);
                checkBox.Checked += (_, _) => Dispatch(plan.Field, true);
                checkBox.Unchecked += (_, _) => Dispatch(plan.Field, false);
                return checkBox;
            }
            case ZoomObjectPropertiesDialogControlKind.Text:
            {
                var textBox = new TextBox
                {
                    MinWidth = inputMinWidth,
                    ToolTip = plan.ToolTipText,
                };
                ApplySemantic(textBox, plan);
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
                ApplySemantic(comboBox, plan);
                comboBox.SelectionChanged += (_, _) => Dispatch(plan.Field, comboBox.SelectedItem);
                return comboBox;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(plan));
        }
    }

    private static void AddToggleRow(Grid grid, int row, Control control)
    {
        Grid.SetRow(control, row);
        Grid.SetColumnSpan(control, 2);
        grid.Children.Add(control);
    }

    private static void AddRow(Grid grid, int row, string labelText, Control control)
    {
        var label = new Label { Content = labelText, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
    }

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

    private void Apply()
    {
        if (!_session.TryAccept(out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation!.Message,
                _surface.Chrome.Title);
            FocusValidationField(validation.Field);
            return;
        }

        DialogResult = true;
    }

    private void FocusValidationField(ZoomObjectPropertiesDialogField field)
    {
        if (!_controls.TryGetValue(field, out var control))
            return;

        control.Focus();
        if (control is TextBox textBox)
            textBox.SelectAll();
    }

    private static void ApplySemantic(
        DependencyObject control,
        ZoomObjectPropertiesDialogControlPlan plan)
    {
        AutomationProperties.SetName(control, plan.AccessibleName);
        AutomationProperties.SetAutomationId(control, plan.AutomationId);
        if (!string.IsNullOrWhiteSpace(plan.HelpText))
            AutomationProperties.SetHelpText(control, plan.HelpText);
    }

    private static void ApplyAction(
        DependencyObject control,
        PresentationDialogActionPlan<ZoomObjectPropertiesDialogChromeAction> action)
    {
        AutomationProperties.SetName(control, action.AccessibleName);
        AutomationProperties.SetAutomationId(control, action.AutomationId);
    }
}
