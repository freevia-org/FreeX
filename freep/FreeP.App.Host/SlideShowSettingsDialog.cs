using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed partial class SlideShowSettingsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
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
        Width = SlideShowSettingsDialogVisualMetrics.WpfWindowWidth;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var checks = new Dictionary<SlideShowSettingsDialogField, CheckBox>();
        foreach (var toggle in SlideShowSettingsDialogSession.ToggleFields)
        {
            var check = new CheckBox
            {
                Content = surface.Field(toggle.Field).Label,
                IsChecked = toggle.Initial(initial),
                Margin = new Thickness(0, 0, 0, SlideShowSettingsDialogVisualMetrics.WpfToggleBottomMargin(toggle.Field)),
            };
            PresentationDialogControlAdapter.ApplySemantic(check, surface.Field(toggle.Field));
            checks[toggle.Field] = check;
        }

        _useTimingsCheck = checks[SlideShowSettingsDialogField.UseTimings];
        _showAnimationCheck = checks[SlideShowSettingsDialogField.ShowWithoutAnimation];
        _showNarrationCheck = checks[SlideShowSettingsDialogField.PlayNarration];
        _showMediaControlsCheck = checks[SlideShowSettingsDialogField.ShowMediaControls];
        _showMasterShapesCheck = checks[SlideShowSettingsDialogField.ShowMasterGraphics];
        _loopCheck = checks[SlideShowSettingsDialogField.LoopUntilStopped];
        _showScrollbarCheck = checks[SlideShowSettingsDialogField.ShowBrowseScrollbar];
        _showTypeCombo = new ComboBox
        {
            ItemsSource = SlideShowSettingsDialogSession.ShowTypeOptions,
            SelectedIndex = initial.ShowTypeIndex,
            Margin = new Thickness(0, 0, 0, SlideShowSettingsDialogVisualMetrics.WpfShowTypeBottomMargin),
        };
        PresentationDialogControlAdapter.ApplySemantic(_showTypeCombo, surface.Field(SlideShowSettingsDialogField.ShowType));
        _kioskRestartText = new TextBox
        {
            Text = initial.KioskRestartMilliseconds,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 0, SlideShowSettingsDialogVisualMetrics.WpfKioskRestartBottomMargin),
        };
        PresentationDialogControlAdapter.ApplySemantic(_kioskRestartText, surface.Field(SlideShowSettingsDialogField.KioskRestartMilliseconds));

        _formSession = new(
            PresentationDialogControlAdapter.CaptureValue,
            PresentationDialogControlAdapter.ApplyValue);
        _formSession.RegisterStandardControls(
            _useTimingsCheck, _showAnimationCheck, _showNarrationCheck,
            _showMediaControlsCheck, _showMasterShapesCheck, _loopCheck,
            _showTypeCombo, _showScrollbarCheck, _kioskRestartText);

        // PowerPoint forces "Loop until 'Esc'" whenever "Browsed at a kiosk" is
        // selected and the user cannot turn it off in that mode: the checkbox shows
        // checked and is disabled for as long as kiosk is the selected show type.
        _showTypeCombo.SelectionChanged += (_, _) => SyncLoopCheckForShowType();
        SyncLoopCheckForShowType();

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

    private bool Apply()
    {
        var applied = _session.TryApply(_formSession.CaptureInput());
        if (applied && IsLoaded)
            DialogResult = true;
        return applied;
    }

    private void SyncLoopCheckForShowType()
    {
        var loop = SlideShowSettingsDialogSession.ResolveLoopState(
            (_showTypeCombo.SelectedItem as SlideShowSettingsShowTypeOption)?.ShowType);
        if (loop.ForceChecked)
            _loopCheck.IsChecked = true;
        _loopCheck.IsEnabled = loop.IsEnabled;
    }
}
