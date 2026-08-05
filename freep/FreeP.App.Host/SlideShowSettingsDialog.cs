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
    private readonly CheckBox _loopCheck;
    private readonly ComboBox _showTypeCombo;

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

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(_useTimingsCheck);
        panel.Children.Add(_showAnimationCheck);
        panel.Children.Add(_loopCheck);
        panel.Children.Add(_showTypeCombo);
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
        PresentationShowType showType = PresentationShowType.PresentedBySpeaker)
    {
        _useTimingsCheck.IsChecked = useSlideTimings;
        _showAnimationCheck.IsChecked = !showWithAnimation;
        _loopCheck.IsChecked = loopUntilStopped;
        _showTypeCombo.SelectedIndex = (int)showType;
        return Apply();
    }

    private bool Apply()
    {
        var applied = SlideShowSettingsPlanner.TryApply(
            _editor,
            _useTimingsCheck.IsChecked == true,
            _showAnimationCheck.IsChecked != true,
            _loopCheck.IsChecked == true,
            (PresentationShowType)Math.Clamp(_showTypeCombo.SelectedIndex, 0, 2));
        if (applied && IsLoaded)
            DialogResult = true;
        return applied;
    }
}
