using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class HeaderFooterDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly EditingSession _editor;
    private readonly CheckBox _dateTimeCheck;
    private readonly ComboBox _dateFormatCombo;
    private readonly CheckBox _fixedDateCheck;
    private readonly TextBox _fixedDateBox;
    private readonly CheckBox _footerCheck;
    private readonly TextBox _footerBox;
    private readonly CheckBox _slideNumberCheck;
    private readonly CheckBox _dontShowOnTitleSlideCheck;

    internal HeaderFooterState InitialState { get; }
    internal HeaderFooterApplyPlan? LastApplyPlan { get; private set; }
    internal HeaderFooterCommandFocus RequestedFocus { get; }

    public HeaderFooterDialog(EditingSession editor, HeaderFooterCommandFocus focus)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        RequestedFocus = focus;
        InitialState = HeaderFooterCommandPlanner.BuildState(editor);
        var defaults = HeaderFooterCommandPlanner.BuildDefaultOptions(InitialState, focus);

        Title = "Header and Footer";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _dateTimeCheck = new CheckBox { Content = "Date and time", IsChecked = defaults.ShowDateTime };
        _dateFormatCombo = new ComboBox
        {
            ItemsSource = HeaderFooterCommandPlanner.DateFormatOptions,
            SelectedItem = HeaderFooterCommandPlanner.DateFormatOptions.FirstOrDefault(option =>
                StringComparer.Ordinal.Equals(option.FieldType, defaults.DateTimeFieldType)) ??
                HeaderFooterCommandPlanner.DateFormatOptions[0],
            MinWidth = 260,
            Margin = new Thickness(20, 4, 0, 4),
        };
        _fixedDateCheck = new CheckBox
        {
            Content = "Fixed",
            IsChecked = defaults.DateTimeMode == HeaderFooterDateTimeMode.Fixed,
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
            Margin = new Thickness(20, 4, 0, 8),
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
            Margin = new Thickness(0, 0, 0, 12),
        };

        ApplyChrome();
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
        _dateTimeCheck.IsChecked = showDateTime;
        _dateFormatCombo.SelectedItem = HeaderFooterCommandPlanner.DateFormatOptions.FirstOrDefault(option =>
            StringComparer.Ordinal.Equals(option.FieldType, dateTimeFieldType)) ??
            HeaderFooterCommandPlanner.DateFormatOptions[0];
        _fixedDateCheck.IsChecked = dateTimeMode == HeaderFooterDateTimeMode.Fixed;
        _fixedDateBox.Text = fixedDateTimeText;
        _footerCheck.IsChecked = showFooter;
        _footerBox.Text = footerText;
        _slideNumberCheck.IsChecked = showSlideNumber;
        _dontShowOnTitleSlideCheck.IsChecked = suppressOnTitleSlide;
        UpdateEnabledState();
        return Apply(scope);
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
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [apply, applyAll, cancel],
            new Thickness(0, 2, 0, 0)));
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
        AvaloniaCompactDialogChrome.ApplyCheckBox(_slideNumberCheck, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_dontShowOnTitleSlideCheck, DialogChromeStyle);
    }

    private static Button BuildButton(
        string text,
        Action action,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button { Content = text, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 82, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateEnabledState()
    {
        var showDateTime = _dateTimeCheck.IsChecked == true;
        var fixedDate = _fixedDateCheck.IsChecked == true;
        _dateFormatCombo.IsEnabled = showDateTime && !fixedDate;
        _fixedDateCheck.IsEnabled = showDateTime;
        _fixedDateBox.IsEnabled = showDateTime && fixedDate;
        _footerBox.IsEnabled = _footerCheck.IsChecked == true;
    }

    private bool Apply(HeaderFooterApplyScope scope)
    {
        var dateFormat = _dateFormatCombo.SelectedItem as HeaderFooterDateFormatOption ??
            HeaderFooterCommandPlanner.DateFormatOptions[0];
        var options = new HeaderFooterApplyOptions(
            _dateTimeCheck.IsChecked == true,
            _footerCheck.IsChecked == true,
            _slideNumberCheck.IsChecked == true,
            _footerBox.Text ?? string.Empty,
            scope,
            _dontShowOnTitleSlideCheck.IsChecked == true,
            _fixedDateCheck.IsChecked == true
                ? HeaderFooterDateTimeMode.Fixed
                : HeaderFooterDateTimeMode.AutoUpdate,
            dateFormat.FieldType,
            _fixedDateBox.Text ?? string.Empty);

        if (!HeaderFooterCommandPlanner.TryApply(_editor, options, out var plan))
            return false;

        LastApplyPlan = plan;
        if (IsVisible)
            Close(true);
        return true;
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
