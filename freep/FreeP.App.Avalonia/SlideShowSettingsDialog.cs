using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class SlideShowSettingsDialog : FreePDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly SlideShowSettingsDialogSession _session;
    private readonly SlideShowSettingsDialogFormSession<Control> _formSession;
    private readonly CheckBox _useTimingsCheck;
    private readonly CheckBox _showAnimationCheck;
    private readonly CheckBox _showNarrationCheck;
    private readonly CheckBox _showMediaControlsCheck;
    private readonly CheckBox _showMasterShapesCheck;
    private readonly CheckBox _loopCheck;
    private readonly ComboBox _showTypeCombo;
    private readonly CheckBox _showScrollbarCheck;
    private readonly TextBox _kioskRestartText;

    internal SlideShowSettingsState InitialState => _session.InitialState;

    internal SlideShowSettingsDialogCommitPlan? LastCommitPlan => _session.LastCommitPlan;

    public SlideShowSettingsDialog(EditingSession editor)
    {
        _session = new SlideShowSettingsDialogSession(editor);
        var initial = _session.InitialInput;
        var surface = _session.Surface;

        Title = surface.Title;
        AutomationProperties.SetName(this, surface.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.AutomationId);
        Width = 345.3333333333333;
        Height = 240;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _useTimingsCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.UseTimings).Label,
            IsChecked = initial.UseSlideTimings,
        };
        _showAnimationCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowWithoutAnimation).Label,
            IsChecked = initial.ShowWithoutAnimation,
        };
        _showNarrationCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.PlayNarration).Label,
            IsChecked = initial.ShowWithNarration,
        };
        _showMediaControlsCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowMediaControls).Label,
            IsChecked = initial.ShowMediaControls,
        };
        _showMasterShapesCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowMasterGraphics).Label,
            IsChecked = initial.ShowMasterShapes,
        };
        _loopCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.LoopUntilStopped).Label,
            IsChecked = initial.LoopUntilStopped,
        };
        _showTypeCombo = new ComboBox
        {
            ItemsSource = SlideShowSettingsDialogSession.ShowTypeOptions,
            SelectedIndex = initial.ShowTypeIndex,
        };
        _showScrollbarCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowBrowseScrollbar).Label,
            IsChecked = initial.ShowBrowseScrollbar,
        };
        _kioskRestartText = new TextBox
        {
            Text = initial.KioskRestartMilliseconds,
            MinWidth = 76,
        };

        PresentationDialogControlAdapter.ApplySemantic(_useTimingsCheck, surface.Field(SlideShowSettingsDialogField.UseTimings));
        PresentationDialogControlAdapter.ApplySemantic(_showAnimationCheck, surface.Field(SlideShowSettingsDialogField.ShowWithoutAnimation));
        PresentationDialogControlAdapter.ApplySemantic(_showNarrationCheck, surface.Field(SlideShowSettingsDialogField.PlayNarration));
        PresentationDialogControlAdapter.ApplySemantic(_showMediaControlsCheck, surface.Field(SlideShowSettingsDialogField.ShowMediaControls));
        PresentationDialogControlAdapter.ApplySemantic(_showMasterShapesCheck, surface.Field(SlideShowSettingsDialogField.ShowMasterGraphics));
        PresentationDialogControlAdapter.ApplySemantic(_loopCheck, surface.Field(SlideShowSettingsDialogField.LoopUntilStopped));
        PresentationDialogControlAdapter.ApplySemantic(_showTypeCombo, surface.Field(SlideShowSettingsDialogField.ShowType));
        PresentationDialogControlAdapter.ApplySemantic(_showScrollbarCheck, surface.Field(SlideShowSettingsDialogField.ShowBrowseScrollbar));
        PresentationDialogControlAdapter.ApplySemantic(_kioskRestartText, surface.Field(SlideShowSettingsDialogField.KioskRestartMilliseconds));

        _formSession = new(
            PresentationDialogControlAdapter.CaptureValue,
            PresentationDialogControlAdapter.ApplyValue);
        _formSession.RegisterStandardControls(
            _useTimingsCheck, _showAnimationCheck, _showNarrationCheck,
            _showMediaControlsCheck, _showMasterShapesCheck, _loopCheck,
            _showTypeCombo, _showScrollbarCheck, _kioskRestartText);

        foreach (var check in new[] { _useTimingsCheck, _showAnimationCheck, _showNarrationCheck, _showMediaControlsCheck, _showMasterShapesCheck, _loopCheck })
        {
            AvaloniaCompactDialogChrome.ApplyCheckBox(check, DialogChromeStyle);
            check.Height = 22;
            check.MinHeight = 22;
            check.MaxHeight = 22;
            check.Padding = new Thickness(0);
        }

        Content = BuildContent();
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
        var panel = new StackPanel { Margin = new Thickness(14), Spacing = 4 };
        panel.Children.Add(_useTimingsCheck);
        panel.Children.Add(_showAnimationCheck);
        panel.Children.Add(_showNarrationCheck);
        panel.Children.Add(_showMediaControlsCheck);
        panel.Children.Add(_showMasterShapesCheck);
        panel.Children.Add(_loopCheck);
        panel.Children.Add(_showTypeCombo);
        panel.Children.Add(_showScrollbarCheck);
        panel.Children.Add(new TextBlock
        {
            Text = surface.Field(SlideShowSettingsDialogField.KioskRestartMilliseconds).Label,
        });
        panel.Children.Add(_kioskRestartText);

        var ok = BuildButton(
            surface.Action(SlideShowSettingsDialogAction.Accept),
            () => Apply());
        var cancel = BuildButton(
            surface.Action(SlideShowSettingsDialogAction.Cancel),
            () => Close(false));
        var actions = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0));
        actions.Spacing = 6;
        panel.Children.Add(actions);
        return panel;
    }

    private static Button BuildButton(
        PresentationDialogActionPlan<SlideShowSettingsDialogAction> plan,
        Action action)
    {
        var button = new Button
        {
            Content = plan.Label,
            MinWidth = 76,
            IsDefault = plan.IsDefault,
            IsCancel = plan.IsCancel,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: 76,
            isDefault: plan.IsDefault);
        button.Click += (_, _) => action();
        return button;
    }

    private bool Apply()
    {
        var applied = _session.TryApply(_formSession.CaptureInput());
        if (applied && IsVisible)
            Close(true);
        return applied;
    }

}
