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
    private readonly ZoomObjectPropertiesDialogSession _session;
    private readonly ZoomObjectPropertiesDialogSurfacePlan _surface;
    private readonly ZoomObjectPropertiesDialogFormSession<Control> _formSession;

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
        _formSession = new(_session.Dispatch, NativeBinding.ApplyFieldState, NativeBinding.Focus);
        var layout = _surface.Layout;
        Title = _surface.Chrome.Title;
        Width = _surface.Chrome.Width;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ZoomDialogChrome.Apply(this);
        AutomationProperties.SetName(this, _surface.Chrome.AccessibleName);
        AutomationProperties.SetAutomationId(this, _surface.Chrome.AutomationId);

        _formSession.RegisterFields(
            _session.FieldCatalog,
            plan => CreateControl(plan, layout.InputMinWidth));

        var children = new List<Control>();
        foreach (var plan in _session.FieldCatalog)
        {
            var control = _formSession.Control(plan.Field);
            children.Add(plan.Kind == ZoomObjectPropertiesDialogControlKind.Toggle
                ? control
                : Row(plan.Label, control, layout.LabelWidth));
        }

        var okAction = _surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Accept);
        var cancelAction = _surface.Chrome.Action(ZoomObjectPropertiesDialogChromeAction.Cancel);
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
        _formSession.ApplyState(_session.State);
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
                PresentationDialogControlAdapter.ApplySemantic(
                    checkBox,
                    plan.AccessibleName,
                    plan.AutomationId,
                    plan.HelpText);
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
                PresentationDialogControlAdapter.ApplySemantic(
                    textBox,
                    plan.AccessibleName,
                    plan.AutomationId,
                    plan.HelpText);
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
                PresentationDialogControlAdapter.ApplySemantic(
                    comboBox,
                    plan.AccessibleName,
                    plan.AutomationId,
                    plan.HelpText);
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
        => _formSession.Dispatch(field, value);

    private async void Apply()
    {
        if (!_session.TryAccept(out var validation))
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                validation!.Message,
                _surface.Chrome.Title);
            _formSession.Focus(validation.Field);
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
