using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class HeaderFooterDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly HeaderFooterDialogSession _session;
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
        var defaults = _session.InitialInput;

        Title = "Header and Footer";
        Width = 345.3333333333333;
        Height = 260.6666666666667;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        _dateTimeCheck = new CheckBox { Content = "Date and time", IsChecked = defaults.ShowDateTime };
        _dateFormatCombo = new ComboBox
        {
            ItemsSource = HeaderFooterDialogSession.DateFormatOptions,
            SelectedIndex = defaults.DateFormatIndex,
            MinWidth = 260,
            Margin = new Thickness(20, 0, 0, 4),
        };
        _fixedDateCheck = new CheckBox
        {
            Content = "Fixed",
            IsChecked = defaults.UseFixedDateTime,
            Margin = new Thickness(20, 0, 0, 4),
        };
        _fixedDateBox = new TextBox
        {
            Text = defaults.FixedDateTimeText,
            MinWidth = 240,
            Margin = new Thickness(40, 0, 0, 8),
        };
        _footerCheck = new CheckBox { Content = "Footer", IsChecked = defaults.ShowFooter };
        _footerBox = new TextBox
        {
            Text = defaults.FooterText,
            MinWidth = 260,
            Margin = new Thickness(20, 0, 0, 8),
        };
        _slideNumberCheck = new CheckBox
        {
            Content = "Slide number",
            IsChecked = defaults.ShowSlideNumber,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _dontShowOnTitleSlideCheck = new CheckBox
        {
            Content = "Don't show on title slide",
            IsChecked = defaults.SuppressOnTitleSlide,
            Margin = new Thickness(0, 0, 0, 8),
        };

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

    internal bool ApplyForTests(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string footerText,
        HeaderFooterApplyScope scope,
        bool suppressOnTitleSlide = false,
        HeaderFooterDateTimeMode dateTimeMode = HeaderFooterDateTimeMode.AutoUpdate,
        string dateTimeFieldType = "datetime1",
        string fixedDateTimeText = "")
    {
        PrepareForVisualEvidence(
            showDateTime,
            showFooter,
            showSlideNumber,
            footerText,
            suppressOnTitleSlide,
            dateTimeMode,
            dateTimeFieldType,
            fixedDateTimeText);
        return Apply(scope);
    }

    internal void PrepareForVisualEvidence(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string footerText,
        bool suppressOnTitleSlide = false,
        HeaderFooterDateTimeMode dateTimeMode = HeaderFooterDateTimeMode.AutoUpdate,
        string dateTimeFieldType = "datetime1",
        string fixedDateTimeText = "")
    {
        ApplyInput(HeaderFooterDialogSession.CreateInput(
            showDateTime,
            showFooter,
            showSlideNumber,
            footerText,
            suppressOnTitleSlide,
            dateTimeMode,
            dateTimeFieldType,
            fixedDateTimeText));
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

        var apply = BuildButton("Apply", () => Apply(HeaderFooterApplyScope.CurrentSlide), isDefault: true);
        var applyAll = BuildButton("Apply to All", () => Apply(HeaderFooterApplyScope.AllSlides));
        var cancel = BuildButton("Cancel", () => Close(false), isCancel: true);
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
        string text,
        Action action,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button { Content = text, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 76, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateEnabledState()
    {
        var enabled = HeaderFooterDialogSession.BuildEnabledState(ReadInput());
        _dateFormatCombo.IsEnabled = enabled.IsDateFormatEnabled;
        _fixedDateCheck.IsEnabled = enabled.IsDateTimeModeEnabled;
        _fixedDateBox.IsEnabled = enabled.IsFixedDateTimeTextEnabled;
        _footerBox.IsEnabled = enabled.IsFooterTextEnabled;
    }

    private bool Apply(HeaderFooterApplyScope scope)
    {
        if (!_session.TryApply(ReadInput(), scope))
            return false;

        if (IsVisible)
            Close(true);
        return true;
    }

    private HeaderFooterDialogInputState ReadInput() =>
        HeaderFooterDialogSession.CreateInput(
            _dateTimeCheck.IsChecked == true,
            _footerCheck.IsChecked == true,
            _slideNumberCheck.IsChecked == true,
            _footerBox.Text,
            _dontShowOnTitleSlideCheck.IsChecked == true,
            _fixedDateCheck.IsChecked == true,
            _dateFormatCombo.SelectedIndex,
            _fixedDateBox.Text);

    private void ApplyInput(HeaderFooterDialogInputState input)
    {
        _dateTimeCheck.IsChecked = input.ShowDateTime;
        _dateFormatCombo.SelectedIndex = input.DateFormatIndex;
        _fixedDateCheck.IsChecked = input.UseFixedDateTime;
        _fixedDateBox.Text = input.FixedDateTimeText;
        _footerCheck.IsChecked = input.ShowFooter;
        _footerBox.Text = input.FooterText;
        _slideNumberCheck.IsChecked = input.ShowSlideNumber;
        _dontShowOnTitleSlideCheck.IsChecked = input.SuppressOnTitleSlide;
        UpdateEnabledState();
    }

    private void FocusRequestedControl()
    {
        switch (RequestedFocus)
        {
            case HeaderFooterCommandFocus.DateTime:
                _dateTimeCheck.Focus();
                break;
            case HeaderFooterCommandFocus.Footer:
                _footerBox.Focus();
                _footerBox.SelectAll();
                break;
            case HeaderFooterCommandFocus.SlideNumber:
                _slideNumberCheck.Focus();
                break;
        }
    }
}
