using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class ZoomObjectPropertiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly ZoomObjectPropertiesDialogNativeBinding<Control, CheckBox, TextBox, ComboBox>
        NativeBinding = new(
            static (control, value) => control.IsEnabled = value,
            static control => control.IsChecked,
            static (control, value) => control.IsChecked = value,
            static control => control.Text,
            static (control, value) => control.Text = value,
            static control => control.SelectedItem,
            static (control, value) => control.SelectedItem = value,
            static control => control.Focus(),
            static control => control.SelectAll());
    private readonly ZoomObjectPropertiesDialogNativeRendererSession<Control> _wpfRenderer;

    internal ZoomObjectProperties Properties => _wpfRenderer.Properties;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout =>
        _wpfRenderer.SummaryTileLayout;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties =>
        _wpfRenderer.SummaryTileProperties;
    internal bool ApplySummaryPropertiesToAllTiles =>
        _wpfRenderer.ApplySummaryPropertiesToAllTiles;

    internal ZoomObjectPropertiesDialog(
        ZoomObjectProperties current,
        IReadOnlyList<SummaryZoomTarget>? summaryTargets = null,
        IReadOnlyList<ZoomObjectProperties>? summaryTileProperties = null)
    {
        _wpfRenderer = new(
            current,
            summaryTargets,
            summaryTileProperties,
            NativeBinding.ApplyFieldState,
            NativeBinding.Focus,
            new(CreateToggle, CreateText, CreateChoice));
        var layout = _wpfRenderer.Surface.Layout;
        Title = _wpfRenderer.Surface.Chrome.Title;
        Width = _wpfRenderer.Surface.Chrome.Width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, _wpfRenderer.Surface.Chrome.AccessibleName);
        AutomationProperties.SetAutomationId(this, _wpfRenderer.Surface.Chrome.AutomationId);

        _wpfRenderer.Form.RegisterFields(
            _wpfRenderer.Session.FieldCatalog,
            plan => _wpfRenderer.ControlFactory.Create(plan, layout.InputMinWidth));

        var grid = new Grid { Margin = new Thickness(layout.ContentMargin) };
        for (var index = 0; index <= _wpfRenderer.Session.FieldCatalog.Count; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        foreach (var plan in _wpfRenderer.Session.FieldCatalog)
        {
            var control = _wpfRenderer.Form.Control(plan.Field);
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
            Content = _wpfRenderer.Surface.Chrome.AcceptLabel,
            IsDefault = true,
            MinWidth = 75,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ApplyAction(
            ok,
            _wpfRenderer.Surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Accept));
        ok.Click += (_, _) => Apply();
        buttons.Children.Add(ok);
        var cancel = new Button
        {
            Content = _wpfRenderer.Surface.Chrome.CancelLabel,
            IsCancel = true,
            MinWidth = 75,
        };
        ApplyAction(
            cancel,
            _wpfRenderer.Surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Cancel));
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, row);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;
        _wpfRenderer.Form.ApplyState(_wpfRenderer.Session.State);
    }

    private Control CreateToggle(ZoomObjectPropertiesDialogControlPlan plan)
    {
        var checkBox = new CheckBox
        {
            Content = plan.Label,
            Margin = plan.Field == ZoomObjectPropertiesDialogField.ApplySummaryPropertiesToAllTiles
                ? new Thickness(0, 4, 0, 0)
                : default,
        };
        ApplySemantic(checkBox, plan);
        checkBox.Checked += (_, _) => Dispatch(plan.Field, true);
        checkBox.Unchecked += (_, _) => Dispatch(plan.Field, false);
        return checkBox;
    }

    private Control CreateText(ZoomObjectPropertiesDialogControlPlan plan, double inputMinWidth)
    {
        var textBox = new TextBox { MinWidth = inputMinWidth, ToolTip = plan.ToolTipText };
        ApplySemantic(textBox, plan);
        textBox.TextChanged += (_, _) => Dispatch(plan.Field, textBox.Text);
        return textBox;
    }

    private Control CreateChoice(ZoomObjectPropertiesDialogControlPlan plan, double inputMinWidth)
    {
        var comboBox = new ComboBox { ItemsSource = plan.Options, MinWidth = inputMinWidth };
        ApplySemantic(comboBox, plan);
        comboBox.SelectionChanged += (_, _) => Dispatch(plan.Field, comboBox.SelectedItem);
        return comboBox;
    }

    private static void ApplySemantic(Control control, ZoomObjectPropertiesDialogControlPlan plan) =>
        PresentationDialogControlAdapter.ApplySemantic(
            control,
            plan.AccessibleName,
            plan.AutomationId,
            plan.HelpText);

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
        => _wpfRenderer.Form.Dispatch(field, value);

    private void Apply()
    {
        if (!_wpfRenderer.Session.TryAccept(out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation!.Message,
                _wpfRenderer.Surface.Chrome.Title);
            _wpfRenderer.Form.Focus(validation.Field);
            return;
        }

        DialogResult = true;
    }

    private static void ApplyAction(
        DependencyObject control,
        PresentationDialogActionPlan<ZoomObjectPropertiesDialogChromeAction> action)
    {
        AutomationProperties.SetName(control, action.AccessibleName);
        AutomationProperties.SetAutomationId(control, action.AutomationId);
    }
}
