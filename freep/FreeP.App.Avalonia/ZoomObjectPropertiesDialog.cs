using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ZoomObjectPropertiesDialog : FreePDialogWindow
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
    private readonly ZoomObjectPropertiesDialogNativeRendererSession<Control> _avaloniaRenderer;

    internal ZoomObjectProperties Properties => _avaloniaRenderer.Properties;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout =>
        _avaloniaRenderer.SummaryTileLayout;
    internal ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties =>
        _avaloniaRenderer.SummaryTileProperties;
    internal bool ApplySummaryPropertiesToAllTiles =>
        _avaloniaRenderer.ApplySummaryPropertiesToAllTiles;

    internal ZoomObjectPropertiesDialog(
        ZoomObjectProperties current,
        IReadOnlyList<SummaryZoomTarget>? summaryTargets = null,
        IReadOnlyList<ZoomObjectProperties>? summaryTileProperties = null)
    {
        _avaloniaRenderer = new(
            current,
            summaryTargets,
            summaryTileProperties,
            NativeBinding.ApplyFieldState,
            NativeBinding.Focus,
            new(CreateToggle, CreateText, CreateChoice));
        var layout = _avaloniaRenderer.Surface.Layout;
        Title = _avaloniaRenderer.Surface.Chrome.Title;
        Width = _avaloniaRenderer.Surface.Chrome.Width;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this);
        AutomationProperties.SetName(this, _avaloniaRenderer.Surface.Chrome.AccessibleName);
        AutomationProperties.SetAutomationId(this, _avaloniaRenderer.Surface.Chrome.AutomationId);

        _avaloniaRenderer.Form.RegisterFields(
            _avaloniaRenderer.Session.FieldCatalog,
            plan => _avaloniaRenderer.ControlFactory.Create(plan, layout.InputMinWidth));

        var children = new List<Control>();
        foreach (var plan in _avaloniaRenderer.Session.FieldCatalog)
        {
            var control = _avaloniaRenderer.Form.Control(plan.Field);
            children.Add(plan.Kind == ZoomObjectPropertiesDialogControlKind.Toggle
                ? control
                : Row(plan.Label, control, layout.LabelWidth));
        }

        var okAction = _avaloniaRenderer.Surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Accept);
        var cancelAction = _avaloniaRenderer.Surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Cancel);
        var ok = ZoomDialogChrome.MakeButton(okAction.Label, okAction.IsDefault, Apply);
        ApplyAction(ok, okAction);
        var cancel = ZoomDialogChrome.MakeButton(
            cancelAction.Label,
            cancelAction.IsDefault,
            () => Close(false));
        cancel.IsCancel = cancelAction.IsCancel;
        ApplyAction(cancel, cancelAction);
        children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                ok,
                cancel,
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
        _avaloniaRenderer.Form.ApplyState(_avaloniaRenderer.Session.State);
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
        checkBox.IsCheckedChanged += (_, _) => Dispatch(plan.Field, checkBox.IsChecked == true);
        return checkBox;
    }

    private Control CreateText(ZoomObjectPropertiesDialogControlPlan plan, double inputMinWidth)
    {
        var textBox = new TextBox { MinWidth = inputMinWidth, PlaceholderText = plan.PlaceholderText };
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
        => _avaloniaRenderer.Form.Dispatch(field, value);

    private async void Apply()
    {
        if (!_avaloniaRenderer.Session.TryAccept(out var validation))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                validation!.Message,
                _avaloniaRenderer.Surface.Chrome.Title);
            _avaloniaRenderer.Form.Focus(validation.Field);
            return;
        }

        Close(true);
    }

    private static void ApplyAction(
        Control control,
        PresentationDialogActionPlan<ZoomObjectPropertiesDialogChromeAction> action)
    {
        AutomationProperties.SetName(control, action.AccessibleName);
        AutomationProperties.SetAutomationId(control, action.AutomationId);
    }
}
