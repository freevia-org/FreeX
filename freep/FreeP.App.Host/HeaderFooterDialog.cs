using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed class HeaderFooterDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly CheckBox _dateTimeCheck;
    private readonly ComboBox _dateFormatCombo;
    private readonly CheckBox _fixedDateCheck;
    private readonly TextBox _fixedDateBox;
    private readonly CheckBox _footerCheck;
    private readonly CheckBox _slideNumberCheck;
    private readonly CheckBox _dontShowOnTitleSlideCheck;
    private readonly TextBox _footerBox;

    internal HeaderFooterState InitialState { get; }
    internal HeaderFooterCommandFocus RequestedFocus { get; }
    public HeaderFooterApplyPlan? LastApplyPlan { get; private set; }

    public HeaderFooterDialog(EditingSession editor, HeaderFooterCommandFocus focus)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        RequestedFocus = focus;
        InitialState = HeaderFooterCommandPlanner.BuildState(editor);
        var defaults = HeaderFooterCommandPlanner.BuildDefaultOptions(InitialState, focus);

        Title = "Header and Footer";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel
        {
            Margin = new Thickness(14),
        };

        _dateTimeCheck = new CheckBox
        {
            Content = "Date and time",
            IsChecked = defaults.ShowDateTime,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _dateFormatCombo = new ComboBox
        {
            ItemsSource = HeaderFooterCommandPlanner.DateFormatOptions,
            DisplayMemberPath = nameof(HeaderFooterDateFormatOption.DisplayName),
            SelectedItem = HeaderFooterCommandPlanner.DateFormatOptions.FirstOrDefault(option =>
                StringComparer.Ordinal.Equals(option.FieldType, defaults.DateTimeFieldType)) ??
                HeaderFooterCommandPlanner.DateFormatOptions[0],
            Margin = new Thickness(20, 0, 0, 4),
            MinWidth = 260,
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
            Margin = new Thickness(40, 0, 0, 8),
            MinWidth = 240,
        };
        _footerCheck = new CheckBox
        {
            Content = "Footer",
            IsChecked = defaults.ShowFooter,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _footerBox = new TextBox
        {
            Text = defaults.FooterText,
            Margin = new Thickness(20, 0, 0, 8),
            MinWidth = 260,
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

        _footerCheck.Checked += (_, _) => UpdateFooterEnabled();
        _footerCheck.Unchecked += (_, _) => UpdateFooterEnabled();
        _dateTimeCheck.Checked += (_, _) => UpdateDateTimeEnabled();
        _dateTimeCheck.Unchecked += (_, _) => UpdateDateTimeEnabled();
        _fixedDateCheck.Checked += (_, _) => UpdateDateTimeEnabled();
        _fixedDateCheck.Unchecked += (_, _) => UpdateDateTimeEnabled();

        panel.Children.Add(_dateTimeCheck);
        panel.Children.Add(_dateFormatCombo);
        panel.Children.Add(_fixedDateCheck);
        panel.Children.Add(_fixedDateBox);
        panel.Children.Add(_footerCheck);
        panel.Children.Add(_footerBox);
        panel.Children.Add(_slideNumberCheck);
        panel.Children.Add(_dontShowOnTitleSlideCheck);
        panel.Children.Add(BuildButtonRow());

        Content = panel;
        UpdateDateTimeEnabled();
        UpdateFooterEnabled();
        Loaded += (_, _) => FocusRequestedControl();
    }

    private UIElement BuildButtonRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        row.Children.Add(MakeButton("Apply", isDefault: true, () => Apply(HeaderFooterApplyScope.CurrentSlide)));
        row.Children.Add(MakeButton("Apply to All", isDefault: false, () => Apply(HeaderFooterApplyScope.AllSlides)));
        row.Children.Add(MakeButton("Cancel", isDefault: false, () => DialogResult = false, isCancel: true));
        return row;
    }

    private static Button MakeButton(
        string label,
        bool isDefault,
        Action action,
        bool isCancel = false)
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

    private void UpdateFooterEnabled()
    {
        _footerBox.IsEnabled = _footerCheck.IsChecked == true;
    }

    private void UpdateDateTimeEnabled()
    {
        var showDateTime = _dateTimeCheck.IsChecked == true;
        var fixedDate = _fixedDateCheck.IsChecked == true;
        _dateFormatCombo.IsEnabled = showDateTime && !fixedDate;
        _fixedDateCheck.IsEnabled = showDateTime;
        _fixedDateBox.IsEnabled = showDateTime && fixedDate;
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

    private void Apply(HeaderFooterApplyScope scope)
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

        if (HeaderFooterCommandPlanner.TryApply(_editor, options, out var plan))
        {
            LastApplyPlan = plan;
            if (IsLoaded)
            {
                DialogResult = true;
            }
        }
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
        _fixedDateCheck.IsChecked = dateTimeMode == HeaderFooterDateTimeMode.Fixed;
        _fixedDateBox.Text = fixedDateTimeText;
        _dateFormatCombo.SelectedItem = HeaderFooterCommandPlanner.DateFormatOptions.FirstOrDefault(option =>
            StringComparer.Ordinal.Equals(option.FieldType, dateTimeFieldType)) ??
            HeaderFooterCommandPlanner.DateFormatOptions[0];
        _footerCheck.IsChecked = showFooter;
        _footerBox.Text = footerText;
        _slideNumberCheck.IsChecked = showSlideNumber;
        _dontShowOnTitleSlideCheck.IsChecked = suppressOnTitleSlide;
        UpdateDateTimeEnabled();
        Apply(scope);
        return LastApplyPlan?.ShouldApply == true;
    }
}
