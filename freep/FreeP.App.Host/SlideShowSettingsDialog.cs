using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

internal sealed class SlideShowSettingsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
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

        _formSession = new(CaptureValue, ApplyValue);
        _formSession.Register(SlideShowSettingsDialogField.UseTimings, _useTimingsCheck);
        _formSession.Register(SlideShowSettingsDialogField.ShowWithoutAnimation, _showAnimationCheck);
        _formSession.Register(SlideShowSettingsDialogField.PlayNarration, _showNarrationCheck);
        _formSession.Register(SlideShowSettingsDialogField.ShowMediaControls, _showMediaControlsCheck);
        _formSession.Register(SlideShowSettingsDialogField.ShowMasterGraphics, _showMasterShapesCheck);
        _formSession.Register(SlideShowSettingsDialogField.LoopUntilStopped, _loopCheck);
        _formSession.Register(SlideShowSettingsDialogField.ShowType, _showTypeCombo);
        _formSession.Register(SlideShowSettingsDialogField.ShowBrowseScrollbar, _showScrollbarCheck);
        _formSession.Register(SlideShowSettingsDialogField.KioskRestartMilliseconds, _kioskRestartText);

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
        _formSession.ApplyInput(SlideShowSettingsDialogSession.CreateInput(
            useSlideTimings,
            !showWithAnimation,
            loopUntilStopped,
            SlideShowSettingsDialogSession.ShowTypeIndex(showType),
            showBrowseScrollbar,
            SlideShowSettingsDialogSession.FormatRestartMilliseconds(kioskRestartAfterMilliseconds),
            showWithNarration,
            showMediaControls,
            showMasterShapes));
        return Apply();
    }

    private bool Apply()
    {
        var applied = _session.TryApply(_formSession.CaptureInput());
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

    private static SlideShowSettingsDialogFieldValue CaptureValue(Control control) => control switch
    {
        CheckBox checkBox => new(IsChecked: checkBox.IsChecked),
        ComboBox comboBox => new(SelectedIndex: comboBox.SelectedIndex),
        TextBox textBox => new(Text: textBox.Text ?? string.Empty),
        _ => throw new InvalidOperationException($"Unsupported slide show settings control: {control.GetType().Name}.")
    };

    private static void ApplyValue(Control control, SlideShowSettingsDialogFieldValue value)
    {
        switch (control)
        {
            case CheckBox checkBox:
                checkBox.IsChecked = value.IsChecked;
                break;
            case ComboBox comboBox:
                comboBox.SelectedIndex = value.SelectedIndex;
                break;
            case TextBox textBox:
                textBox.Text = value.Text;
                break;
        }
    }

}
