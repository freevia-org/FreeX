using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed class HeaderFooterDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly HeaderFooterDialogSession _session;
    private readonly CheckBox _dateTimeCheck;
    private readonly ComboBox _dateFormatCombo;
    private readonly CheckBox _fixedDateCheck;
    private readonly TextBox _fixedDateBox;
    private readonly CheckBox _footerCheck;
    private readonly CheckBox _slideNumberCheck;
    private readonly CheckBox _dontShowOnTitleSlideCheck;
    private readonly TextBox _footerBox;

    internal HeaderFooterState InitialState => _session.InitialState;
    internal HeaderFooterCommandFocus RequestedFocus => _session.RequestedFocus;
    public HeaderFooterApplyPlan? LastApplyPlan => _session.LastApplyPlan;

    public HeaderFooterDialog(EditingSession editor, HeaderFooterCommandFocus focus)
    {
        _session = new HeaderFooterDialogSession(editor, focus);
        var defaults = _session.InitialInput;

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
            ItemsSource = HeaderFooterDialogSession.DateFormatOptions,
            DisplayMemberPath = nameof(HeaderFooterDateFormatOption.DisplayName),
            SelectedIndex = defaults.DateFormatIndex,
            Margin = new Thickness(20, 0, 0, 4),
            MinWidth = 260,
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

        _footerCheck.Checked += (_, _) => UpdateEnabledState();
        _footerCheck.Unchecked += (_, _) => UpdateEnabledState();
        _dateTimeCheck.Checked += (_, _) => UpdateEnabledState();
        _dateTimeCheck.Unchecked += (_, _) => UpdateEnabledState();
        _fixedDateCheck.Checked += (_, _) => UpdateEnabledState();
        _fixedDateCheck.Unchecked += (_, _) => UpdateEnabledState();

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
        UpdateEnabledState();
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

    private void UpdateEnabledState()
    {
        var enabled = HeaderFooterDialogSession.BuildEnabledState(ReadInput());
        _dateFormatCombo.IsEnabled = enabled.IsDateFormatEnabled;
        _fixedDateCheck.IsEnabled = enabled.IsDateTimeModeEnabled;
        _fixedDateBox.IsEnabled = enabled.IsFixedDateTimeTextEnabled;
        _footerBox.IsEnabled = enabled.IsFooterTextEnabled;
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
        if (_session.TryApply(ReadInput(), scope))
        {
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
        PrepareForVisualEvidence(
            showDateTime,
            showFooter,
            showSlideNumber,
            footerText,
            suppressOnTitleSlide,
            dateTimeMode,
            dateTimeFieldType,
            fixedDateTimeText);
        Apply(scope);
        return LastApplyPlan?.ShouldApply == true;
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
}
