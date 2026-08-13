using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed partial class HeaderFooterDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly HeaderFooterDialogSession _session;
    private readonly HeaderFooterDialogFormSession<Control> _formSession;
    private readonly CheckBox _dateTimeCheck;
    private readonly ComboBox _dateFormatCombo;
    private readonly CheckBox _fixedDateCheck;
    private readonly TextBox _fixedDateBox;
    private readonly CheckBox _footerCheck;
    private readonly TextBox _footerBox;
    private readonly CheckBox _slideNumberCheck;
    private readonly CheckBox _dontShowOnTitleSlideCheck;

    internal HeaderFooterState InitialState => _session.InitialState;
    internal HeaderFooterApplyPlan? LastApplyPlan => _session.LastApplyPlan;
    internal HeaderFooterCommandFocus RequestedFocus => _session.RequestedFocus;

    public HeaderFooterDialog(EditingSession editor, HeaderFooterCommandFocus focus)
    {
        _session = new HeaderFooterDialogSession(editor, focus);
        var initial = _session.State;
        var defaults = initial.Input;
        var surface = _session.Surface;

        Title = surface.Title;
        AutomationProperties.SetName(this, surface.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.AutomationId);
        Width = 345.3333333333333;
        Height = 260.6666666666667;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        _dateTimeCheck = new CheckBox
        {
            Content = surface.Field(HeaderFooterDialogField.DateTime).Label,
            IsChecked = defaults.ShowDateTime,
        };
        _dateFormatCombo = new ComboBox
        {
            ItemsSource = initial.DateFormatOptions,
            SelectedIndex = defaults.DateFormatIndex,
            MinWidth = 260,
            Margin = new Thickness(20, 0, 0, 4),
        };
        _fixedDateCheck = new CheckBox
        {
            Content = surface.Field(HeaderFooterDialogField.FixedDateTime).Label,
            IsChecked = defaults.UseFixedDateTime,
            Margin = new Thickness(20, 0, 0, 4),
        };
        _fixedDateBox = new TextBox
        {
            Text = defaults.FixedDateTimeText,
            MinWidth = 240,
            Margin = new Thickness(40, 0, 0, 8),
        };
        _footerCheck = new CheckBox
        {
            Content = surface.Field(HeaderFooterDialogField.Footer).Label,
            IsChecked = defaults.ShowFooter,
        };
        _footerBox = new TextBox
        {
            Text = defaults.FooterText,
            MinWidth = 260,
            Margin = new Thickness(20, 0, 0, 8),
        };
        _slideNumberCheck = new CheckBox
        {
            Content = surface.Field(HeaderFooterDialogField.SlideNumber).Label,
            IsChecked = defaults.ShowSlideNumber,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _dontShowOnTitleSlideCheck = new CheckBox
        {
            Content = surface.Field(HeaderFooterDialogField.SuppressOnTitleSlide).Label,
            IsChecked = defaults.SuppressOnTitleSlide,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _formSession = new(
            PresentationDialogControlAdapter.CaptureValue,
            PresentationDialogControlAdapter.ApplyValue,
            static (control, enabled) => control.IsEnabled = enabled,
            static control => control.Focus(),
            SelectAllText);
        RegisterControl(_dateTimeCheck, surface.Field(HeaderFooterDialogField.DateTime));
        RegisterControl(_dateFormatCombo, surface.Field(HeaderFooterDialogField.DateFormat));
        RegisterControl(_fixedDateCheck, surface.Field(HeaderFooterDialogField.FixedDateTime));
        RegisterControl(_fixedDateBox, surface.Field(HeaderFooterDialogField.FixedDateTimeText));
        RegisterControl(_footerCheck, surface.Field(HeaderFooterDialogField.Footer));
        RegisterControl(_footerBox, surface.Field(HeaderFooterDialogField.FooterText));
        RegisterControl(_slideNumberCheck, surface.Field(HeaderFooterDialogField.SlideNumber));
        RegisterControl(
            _dontShowOnTitleSlideCheck,
            surface.Field(HeaderFooterDialogField.SuppressOnTitleSlide));

        ApplyChrome();
        ApplyDisabledChrome();
        _footerCheck.IsCheckedChanged += (_, _) => UpdateEnabledState();
        _dateTimeCheck.IsCheckedChanged += (_, _) => UpdateEnabledState();
        _fixedDateCheck.IsCheckedChanged += (_, _) => UpdateEnabledState();

        Content = BuildContent();
        UpdateEnabledState();
        Opened += (_, _) => FocusRequestedControl();
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
        var panel = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                _dateTimeCheck,
                _dateFormatCombo,
                _fixedDateCheck,
                _fixedDateBox,
                _footerCheck,
                _footerBox,
                _slideNumberCheck,
                _dontShowOnTitleSlideCheck,
            },
        };

        var apply = BuildButton(
            _session.Surface.Action(HeaderFooterDialogAction.Apply),
            () => Apply(HeaderFooterApplyScope.CurrentSlide));
        var applyAll = BuildButton(
            _session.Surface.Action(HeaderFooterDialogAction.ApplyToAll),
            () => Apply(HeaderFooterApplyScope.AllSlides));
        var cancel = BuildButton(
            _session.Surface.Action(HeaderFooterDialogAction.Cancel),
            () => Close(false));
        var actions = AvaloniaCompactDialogChrome.CreateActionRow(
            [apply, applyAll, cancel],
            new Thickness(0));
        actions.Spacing = 6;
        panel.Children.Add(actions);
        return panel;
    }

    private void ApplyChrome()
    {
        AvaloniaCompactDialogChrome.ApplyCheckBox(_dateTimeCheck, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_dateFormatCombo, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_fixedDateCheck, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_fixedDateBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_footerCheck, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_footerBox, DialogChromeStyle);
        _fixedDateBox.Background = Brushes.White;
        _footerBox.Background = Brushes.White;
        AvaloniaCompactDialogChrome.ApplyCheckBox(_slideNumberCheck, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_dontShowOnTitleSlideCheck, DialogChromeStyle);
        foreach (var checkBox in new[]
                 {
                     _dateTimeCheck,
                     _fixedDateCheck,
                     _footerCheck,
                     _slideNumberCheck,
                     _dontShowOnTitleSlideCheck,
                 })
        {
            checkBox.Height = 20;
            checkBox.MinHeight = 20;
            checkBox.MaxHeight = 20;
            checkBox.Padding = new Thickness(0);
        }
    }

    private void ApplyDisabledChrome()
    {
        var disabledTextBox = new Style(x => x.OfType<TextBox>().Class(":disabled").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.White),
                new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8))),
            },
        };
        Styles.Add(disabledTextBox);
    }

    private static Button BuildButton(
        PresentationDialogActionPlan<HeaderFooterDialogAction> plan,
        Action action)
    {
        var button = new Button { Content = plan.Label, IsCancel = plan.IsCancel };
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

    private void UpdateEnabledState()
    {
        if (_formSession.IsApplyingState)
            return;

        var enabled = _session.SetInput(_formSession.CaptureInput()).Enabled;
        _formSession.ApplyEnabledState(enabled);
    }

    private bool Apply(HeaderFooterApplyScope scope)
    {
        _session.SetInput(_formSession.CaptureInput());
        if (!_session.TryCommit(scope))
            return false;

        if (IsVisible)
            Close(true);
        return true;
    }

    private void RegisterControl(
        Control control,
        PresentationDialogFieldPlan<HeaderFooterDialogField> field)
    {
        PresentationDialogControlAdapter.ApplySemantic(control, field);
        _formSession.Register(field.Id, control);
    }

    private static void SelectAllText(Control control)
    {
        if (control is TextBox textBox)
            textBox.SelectAll();
    }

    private void FocusRequestedControl() => _formSession.Focus(_session.RequestedFocusPlan);

}
