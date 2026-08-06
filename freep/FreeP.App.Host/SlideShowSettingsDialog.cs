using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class SlideShowSettingsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
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
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _useTimingsCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.UseTimings).Label,
            IsChecked = initial.UseSlideTimings,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showAnimationCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowWithoutAnimation).Label,
            IsChecked = initial.ShowWithoutAnimation,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showNarrationCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.PlayNarration).Label,
            IsChecked = initial.ShowWithNarration,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showMediaControlsCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowMediaControls).Label,
            IsChecked = initial.ShowMediaControls,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showMasterShapesCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowMasterGraphics).Label,
            IsChecked = initial.ShowMasterShapes,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _loopCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.LoopUntilStopped).Label,
            IsChecked = initial.LoopUntilStopped,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _showTypeCombo = new ComboBox
        {
            ItemsSource = SlideShowSettingsDialogSession.ShowTypeOptions,
            SelectedIndex = initial.ShowTypeIndex,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _showScrollbarCheck = new CheckBox
        {
            Content = surface.Field(SlideShowSettingsDialogField.ShowBrowseScrollbar).Label,
            IsChecked = initial.ShowBrowseScrollbar,
            Margin = new Thickness(0, 0, 0, 8),
        };

        ApplySemantic(_useTimingsCheck, surface.Field(SlideShowSettingsDialogField.UseTimings));
        ApplySemantic(_showAnimationCheck, surface.Field(SlideShowSettingsDialogField.ShowWithoutAnimation));
        ApplySemantic(_showNarrationCheck, surface.Field(SlideShowSettingsDialogField.PlayNarration));
        ApplySemantic(_showMediaControlsCheck, surface.Field(SlideShowSettingsDialogField.ShowMediaControls));
        ApplySemantic(_showMasterShapesCheck, surface.Field(SlideShowSettingsDialogField.ShowMasterGraphics));
        ApplySemantic(_loopCheck, surface.Field(SlideShowSettingsDialogField.LoopUntilStopped));
        ApplySemantic(_showTypeCombo, surface.Field(SlideShowSettingsDialogField.ShowType));
        ApplySemantic(_showScrollbarCheck, surface.Field(SlideShowSettingsDialogField.ShowBrowseScrollbar));
        _kioskRestartText = new TextBox
        {
            Text = initial.KioskRestartMilliseconds,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 0, 12),
        };
        ApplySemantic(_kioskRestartText, surface.Field(SlideShowSettingsDialogField.KioskRestartMilliseconds));

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(_useTimingsCheck);
        panel.Children.Add(_showAnimationCheck);
        panel.Children.Add(_showNarrationCheck);
        panel.Children.Add(_showMediaControlsCheck);
        panel.Children.Add(_showMasterShapesCheck);
        panel.Children.Add(_loopCheck);
        panel.Children.Add(_showTypeCombo);
        panel.Children.Add(_showScrollbarCheck);
        panel.Children.Add(new Label
        {
            Content = surface.Field(SlideShowSettingsDialogField.KioskRestartMilliseconds).Label,
        });
        panel.Children.Add(_kioskRestartText);
        panel.Children.Add(BuildButtonRow());
        Content = panel;
    }

    private UIElement BuildButtonRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(MakeButton(
            _session.Surface.Action(SlideShowSettingsDialogAction.Accept),
            () => Apply()));
        row.Children.Add(MakeButton(
            _session.Surface.Action(SlideShowSettingsDialogAction.Cancel),
            () => DialogResult = false));
        return row;
    }

    private static Button MakeButton(
        PresentationDialogActionPlan<SlideShowSettingsDialogAction> plan,
        Action action)
    {
        var button = new Button
        {
            Content = plan.Label,
            MinWidth = 76,
            Margin = new Thickness(6, 0, 0, 0),
            IsDefault = plan.IsDefault,
            IsCancel = plan.IsCancel,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
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
        if (applied && IsLoaded)
            DialogResult = true;
        return applied;
    }

    private static void ApplySemantic(
        DependencyObject control,
        PresentationDialogFieldPlan<SlideShowSettingsDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
    }

}
