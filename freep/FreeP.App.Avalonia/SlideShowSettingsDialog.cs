using Avalonia;
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

        Title = "Set Up Slide Show";
        Width = 345.3333333333333;
        Height = 240;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        _useTimingsCheck = new CheckBox
        {
            Content = "Use timings, if present",
            IsChecked = initial.UseSlideTimings,
        };
        _showAnimationCheck = new CheckBox
        {
            Content = "Show without animation",
            IsChecked = initial.ShowWithoutAnimation,
        };
        _showNarrationCheck = new CheckBox
        {
            Content = "Play narration",
            IsChecked = initial.ShowWithNarration,
        };
        _showMediaControlsCheck = new CheckBox
        {
            Content = "Show media controls",
            IsChecked = initial.ShowMediaControls,
        };
        _showMasterShapesCheck = new CheckBox
        {
            Content = "Show master graphics",
            IsChecked = initial.ShowMasterShapes,
        };
        _loopCheck = new CheckBox
        {
            Content = "Loop until stopped",
            IsChecked = initial.LoopUntilStopped,
        };
        _showTypeCombo = new ComboBox
        {
            ItemsSource = SlideShowSettingsDialogSession.ShowTypeOptions,
            SelectedIndex = initial.ShowTypeIndex,
        };
        _showScrollbarCheck = new CheckBox
        {
            Content = "Show scrollbar when browsing",
            IsChecked = initial.ShowBrowseScrollbar,
        };
        _kioskRestartText = new TextBox
        {
            Text = initial.KioskRestartMilliseconds,
            MinWidth = 76,
        };

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
        var panel = new StackPanel { Margin = new Thickness(14), Spacing = 4 };
        panel.Children.Add(_useTimingsCheck);
        panel.Children.Add(_showAnimationCheck);
        panel.Children.Add(_showNarrationCheck);
        panel.Children.Add(_showMediaControlsCheck);
        panel.Children.Add(_showMasterShapesCheck);
        panel.Children.Add(_loopCheck);
        panel.Children.Add(_showTypeCombo);
        panel.Children.Add(_showScrollbarCheck);
        panel.Children.Add(new TextBlock { Text = "Kiosk restart milliseconds (optional)" });
        panel.Children.Add(_kioskRestartText);

        var ok = BuildButton("OK", () => Apply(), isDefault: true);
        var cancel = BuildButton("Cancel", () => Close(false), isCancel: true);
        var actions = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0));
        actions.Spacing = 6;
        panel.Children.Add(actions);
        return panel;
    }

    private static Button BuildButton(string label, Action action, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 76,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 76, isDefault: isDefault);
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

}
