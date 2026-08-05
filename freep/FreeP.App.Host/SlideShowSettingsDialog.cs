using System.Windows;
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

    public SlideShowSettingsDialog(EditingSession editor)
    {
        _session = new SlideShowSettingsDialogSession(editor);
        var initial = _session.InitialInput;

        Title = "Set Up Slide Show";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _useTimingsCheck = new CheckBox
        {
            Content = "Use timings, if present",
            IsChecked = initial.UseSlideTimings,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showAnimationCheck = new CheckBox
        {
            Content = "Show without animation",
            IsChecked = initial.ShowWithoutAnimation,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showNarrationCheck = new CheckBox
        {
            Content = "Play narration",
            IsChecked = initial.ShowWithNarration,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showMediaControlsCheck = new CheckBox
        {
            Content = "Show media controls",
            IsChecked = initial.ShowMediaControls,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showMasterShapesCheck = new CheckBox
        {
            Content = "Show master graphics",
            IsChecked = initial.ShowMasterShapes,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _loopCheck = new CheckBox
        {
            Content = "Loop until stopped",
            IsChecked = initial.LoopUntilStopped,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _showTypeCombo = new ComboBox
        {
            ItemsSource = new[] { "Presented by a speaker", "Browsed by an individual", "Browsed at a kiosk" },
            SelectedIndex = initial.ShowTypeIndex,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _showScrollbarCheck = new CheckBox
        {
            Content = "Show scrollbar when browsing",
            IsChecked = initial.ShowBrowseScrollbar,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _kioskRestartText = new TextBox
        {
            Text = initial.KioskRestartMilliseconds,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(_useTimingsCheck);
        panel.Children.Add(_showAnimationCheck);
        panel.Children.Add(_showNarrationCheck);
        panel.Children.Add(_showMediaControlsCheck);
        panel.Children.Add(_showMasterShapesCheck);
        panel.Children.Add(_loopCheck);
        panel.Children.Add(_showTypeCombo);
        panel.Children.Add(_showScrollbarCheck);
        panel.Children.Add(new Label { Content = "Kiosk restart milliseconds (optional)" });
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
        row.Children.Add(MakeButton("OK", isDefault: true, () => Apply()));
        row.Children.Add(MakeButton("Cancel", isDefault: false, () => DialogResult = false, isCancel: true));
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action, bool isCancel = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 76,
            Margin = new Thickness(6, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
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
        _showTypeCombo.SelectedIndex = (int)showType;
        _showScrollbarCheck.IsChecked = showBrowseScrollbar;
        _kioskRestartText.Text = SlideShowSettingsDialogSession.FormatRestartMilliseconds(
            kioskRestartAfterMilliseconds);
        return Apply();
    }

    private bool Apply()
    {
        var applied = _session.TryApply(new SlideShowSettingsDialogInput(
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

}
