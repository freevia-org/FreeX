using System.Windows;
using System.Windows.Automation;
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
        var initial = _session.State;
        var defaults = initial.Input;
        var surface = _session.Surface;

        Title = surface.Title;
        AutomationProperties.SetName(this, surface.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.AutomationId);
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
            Content = surface.Field(HeaderFooterDialogField.DateTime).Label,
            IsChecked = defaults.ShowDateTime,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _dateFormatCombo = new ComboBox
        {
            ItemsSource = initial.DateFormatOptions,
            DisplayMemberPath = nameof(HeaderFooterDateFormatOption.DisplayName),
            SelectedIndex = defaults.DateFormatIndex,
            Margin = new Thickness(20, 0, 0, 4),
            MinWidth = 260,
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
            Margin = new Thickness(40, 0, 0, 8),
            MinWidth = 240,
        };
        _footerCheck = new CheckBox
        {
            Content = surface.Field(HeaderFooterDialogField.Footer).Label,
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
            Content = surface.Field(HeaderFooterDialogField.SlideNumber).Label,
            IsChecked = defaults.ShowSlideNumber,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _dontShowOnTitleSlideCheck = new CheckBox
        {
            Content = surface.Field(HeaderFooterDialogField.SuppressOnTitleSlide).Label,
            IsChecked = defaults.SuppressOnTitleSlide,
            Margin = new Thickness(0, 0, 0, 12),
        };

        ApplySemantic(_dateTimeCheck, surface.Field(HeaderFooterDialogField.DateTime));
        ApplySemantic(_dateFormatCombo, surface.Field(HeaderFooterDialogField.DateFormat));
        ApplySemantic(_fixedDateCheck, surface.Field(HeaderFooterDialogField.FixedDateTime));
        ApplySemantic(_fixedDateBox, surface.Field(HeaderFooterDialogField.FixedDateTimeText));
        ApplySemantic(_footerCheck, surface.Field(HeaderFooterDialogField.Footer));
        ApplySemantic(_footerBox, surface.Field(HeaderFooterDialogField.FooterText));
        ApplySemantic(_slideNumberCheck, surface.Field(HeaderFooterDialogField.SlideNumber));
        ApplySemantic(
            _dontShowOnTitleSlideCheck,
            surface.Field(HeaderFooterDialogField.SuppressOnTitleSlide));

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

        row.Children.Add(MakeButton(
            _session.Surface.Action(HeaderFooterDialogAction.Apply),
            () => Apply(HeaderFooterApplyScope.CurrentSlide)));
        row.Children.Add(MakeButton(
            _session.Surface.Action(HeaderFooterDialogAction.ApplyToAll),
            () => Apply(HeaderFooterApplyScope.AllSlides)));
        row.Children.Add(MakeButton(
            _session.Surface.Action(HeaderFooterDialogAction.Cancel),
            () => DialogResult = false));
        return row;
    }

    private static Button MakeButton(
        PresentationDialogActionPlan<HeaderFooterDialogAction> plan,
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

    private void UpdateEnabledState()
    {
        var enabled = _session.SetInput(ReadInput()).Enabled;
        ApplyEnabledState(enabled);
    }

    private void ApplyEnabledState(HeaderFooterDialogEnabledState enabled)
    {
        _dateFormatCombo.IsEnabled = enabled.IsDateFormatEnabled;
        _fixedDateCheck.IsEnabled = enabled.IsDateTimeModeEnabled;
        _fixedDateBox.IsEnabled = enabled.IsFixedDateTimeTextEnabled;
        _footerBox.IsEnabled = enabled.IsFooterTextEnabled;
    }

    private void FocusRequestedControl()
    {
        switch (_session.RequestedFocusField)
        {
            case HeaderFooterDialogField.DateTime:
                _dateTimeCheck.Focus();
                break;
            case HeaderFooterDialogField.FooterText:
                _footerBox.Focus();
                _footerBox.SelectAll();
                break;
            case HeaderFooterDialogField.SlideNumber:
                _slideNumberCheck.Focus();
                break;
        }
    }

    private void Apply(HeaderFooterApplyScope scope)
    {
        _session.SetInput(ReadInput());
        if (_session.TryCommit(scope))
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
        var state = _session.SetInput(
            showDateTime,
            showFooter,
            showSlideNumber,
            footerText,
            suppressOnTitleSlide,
            dateTimeMode,
            dateTimeFieldType,
            fixedDateTimeText);
        ApplyInput(state.Input);
    }

    private HeaderFooterDialogInputState ReadInput() =>
        new(
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

    private static void ApplySemantic(
        DependencyObject control,
        PresentationDialogFieldPlan<HeaderFooterDialogField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
    }
}
