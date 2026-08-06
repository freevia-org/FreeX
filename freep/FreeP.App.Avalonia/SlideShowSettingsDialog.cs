using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class SlideShowSettingsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly SlideShowSettingsDialogSession _session;
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
        Background = Brushes.White;

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

        ApplySemantic(_useTimingsCheck, surface.Field(SlideShowSettingsDialogField.UseTimings));
        ApplySemantic(_showAnimationCheck, surface.Field(SlideShowSettingsDialogField.ShowWithoutAnimation));
        ApplySemantic(_showNarrationCheck, surface.Field(SlideShowSettingsDialogField.PlayNarration));
        ApplySemantic(_showMediaControlsCheck, surface.Field(SlideShowSettingsDialogField.ShowMediaControls));
        ApplySemantic(_showMasterShapesCheck, surface.Field(SlideShowSettingsDialogField.ShowMasterGraphics));
        ApplySemantic(_loopCheck, surface.Field(SlideShowSettingsDialogField.LoopUntilStopped));
        ApplySemantic(_showTypeCombo, surface.Field(SlideShowSettingsDialogField.ShowType));
        ApplySemantic(_showScrollbarCheck, surface.Field(SlideShowSettingsDialogField.ShowBrowseScrollbar));
        ApplySemantic(_kioskRestartText, surface.Field(SlideShowSettingsDialogField.KioskRestartMilliseconds));

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

    internal bool ApplyForTests(
        bool useSlideTimings,
        bool showWithAnimation,
        bool loopUntilStopped,
        PresentationShowType showType = PresentationShowType.PresentedBySpeaker,
        bool showBrowseScrollbar = true,
        uint? kioskRestartAfterMilliseconds = null,
        bool showWithNarration = true,
        bool showMediaControls = true,
        bool showMasterShapes = true)
    {
        _useTimingsCheck.IsChecked = useSlideTimings;
        _showAnimationCheck.IsChecked = !showWithAnimation;
        _loopCheck.IsChecked = loopUntilStopped;
        _showNarrationCheck.IsChecked = showWithNarration;
        _showMediaControlsCheck.IsChecked = showMediaControls;
        _showMasterShapesCheck.IsChecked = showMasterShapes;
        _showTypeCombo.SelectedIndex = SlideShowSettingsDialogSession.ShowTypeIndex(showType);
        _showScrollbarCheck.IsChecked = showBrowseScrollbar;
        _kioskRestartText.Text = SlideShowSettingsDialogSession.FormatRestartMilliseconds(
            kioskRestartAfterMilliseconds);
        return Apply();
    }

    private bool Apply()
    {
        var applied = _session.TryApply(SlideShowSettingsDialogSession.CreateInput(
            _useTimingsCheck.IsChecked == true,
            _showAnimationCheck.IsChecked == true,
            _loopCheck.IsChecked == true,
            _showTypeCombo.SelectedIndex,
            _showScrollbarCheck.IsChecked == true,
            _kioskRestartText.Text ?? string.Empty,
            _showNarrationCheck.IsChecked == true,
            _showMediaControlsCheck.IsChecked == true,
            _showMasterShapesCheck.IsChecked == true));
        if (applied && IsVisible)
            Close(true);
        return applied;
    }

    private static void ApplySemantic(
        Control control,
        PresentationDialogFieldPlan<SlideShowSettingsDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
    }

}
