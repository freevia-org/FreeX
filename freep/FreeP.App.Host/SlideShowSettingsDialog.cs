using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class SlideShowSettingsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly CheckBox _useTimingsCheck;
    private readonly CheckBox _showAnimationCheck;
    private readonly CheckBox _showNarrationCheck;
    private readonly CheckBox _loopCheck;
    private readonly ComboBox _showTypeCombo;
    private readonly CheckBox _showScrollbarCheck;
    private readonly TextBox _kioskRestartText;

    internal SlideShowSettingsState InitialState { get; }

    public SlideShowSettingsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        InitialState = SlideShowSettingsPlanner.BuildState(editor.Presentation);

        Title = "Set Up Slide Show";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _useTimingsCheck = new CheckBox
        {
            Content = "Use timings, if present",
            IsChecked = InitialState.UseSlideTimings,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showAnimationCheck = new CheckBox
        {
            Content = "Show without animation",
            IsChecked = !InitialState.ShowWithAnimation,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _showNarrationCheck = new CheckBox
        {
            Content = "Play narration",
            IsChecked = InitialState.ShowWithNarration,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _loopCheck = new CheckBox
        {
            Content = "Loop until stopped",
            IsChecked = InitialState.LoopUntilStopped,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _showTypeCombo = new ComboBox
        {
            ItemsSource = new[] { "Presented by a speaker", "Browsed by an individual", "Browsed at a kiosk" },
            SelectedIndex = (int)InitialState.ShowType,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _showScrollbarCheck = new CheckBox
        {
            Content = "Show scrollbar when browsing",
            IsChecked = InitialState.ShowBrowseScrollbar,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _kioskRestartText = new TextBox
        {
            Text = InitialState.KioskRestartAfterMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(_useTimingsCheck);
        panel.Children.Add(_showAnimationCheck);
        panel.Children.Add(_showNarrationCheck);
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
        bool showWithNarration = true)
    {
        _useTimingsCheck.IsChecked = useSlideTimings;
        _showAnimationCheck.IsChecked = !showWithAnimation;
        _loopCheck.IsChecked = loopUntilStopped;
        _showNarrationCheck.IsChecked = showWithNarration;
        _showTypeCombo.SelectedIndex = (int)showType;
        _showScrollbarCheck.IsChecked = showBrowseScrollbar;
        _kioskRestartText.Text = kioskRestartAfterMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return Apply();
    }

    private bool Apply()
    {
        var applied = SlideShowSettingsPlanner.TryApply(
            _editor,
            _useTimingsCheck.IsChecked == true,
            _showAnimationCheck.IsChecked != true,
            _loopCheck.IsChecked == true,
            (PresentationShowType)Math.Clamp(_showTypeCombo.SelectedIndex, 0, 2),
            _showScrollbarCheck.IsChecked == true,
            ParseRestartMilliseconds(),
            _showNarrationCheck.IsChecked == true);
        if (applied && IsLoaded)
            DialogResult = true;
        return applied;
    }

    private uint? ParseRestartMilliseconds() =>
        uint.TryParse(_kioskRestartText.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? milliseconds
            : null;
}
