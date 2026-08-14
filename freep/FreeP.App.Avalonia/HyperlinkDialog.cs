using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class HyperlinkDialog : FreePDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with { ControlHeight = 26 };
    private static IBrush WpfDefaultButtonBorderBrush => FreePBrushes.Accent;
    private static readonly IBrush WpfCancelButtonBackgroundBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF1));

    private readonly HyperlinkDialogSession _session;
    private readonly HyperlinkDialogSurfacePlan _surface;
    private readonly RadioButton _urlRadio;
    private readonly RadioButton _slideRadio;
    private readonly TextBox _urlBox;
    private readonly ComboBox _slideCombo;
    private readonly TextBox _tooltipBox;
    private readonly TextBlock _validationText;

    internal Hyperlink? Result => _session.Result;

    public HyperlinkDialog(IReadOnlyList<Slide> slides, Hyperlink? current = null)
        : this(HyperlinkDialogPlanner.BuildDialogRequest(slides, current))
    {
    }

    internal HyperlinkDialog(HyperlinkDialogRequest request)
        : base(DialogChromeStyle)
    {
        ArgumentNullException.ThrowIfNull(request);
        _session = new HyperlinkDialogSession(request);
        _surface = _session.Surface;

        Title = _surface.Title;
        Width = 405.3333333333333;
        Height = 216;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, _surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, _surface.Schema.AutomationId);

        _urlRadio = new RadioButton
        {
            Content = _surface.TargetLabel(HyperlinkDialogTargetKind.Url),
            GroupName = "HyperlinkTarget",
            Margin = new Thickness(0, 0, 0, 4),
        };
        _slideRadio = new RadioButton
        {
            Content = _surface.TargetLabel(HyperlinkDialogTargetKind.Slide),
            GroupName = "HyperlinkTarget",
            Margin = new Thickness(0, 0, 0, 8),
        };
        PresentationDialogControlAdapter.ApplySemantic(_urlRadio, _surface.TargetField(HyperlinkDialogTargetKind.Url));
        PresentationDialogControlAdapter.ApplySemantic(_slideRadio, _surface.TargetField(HyperlinkDialogTargetKind.Slide));
        _urlBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 4),
            MinWidth = 260,
        };
        PresentationDialogControlAdapter.ApplySemantic(_urlBox, _surface.Field(HyperlinkDialogField.Url));
        _slideCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 2),
            MinWidth = 260,
            ItemsSource = _session.SlideOptions,
            SelectedIndex = _session.State.SelectedSlideIndex,
        };
        PresentationDialogControlAdapter.ApplySemantic(_slideCombo, _surface.Field(HyperlinkDialogField.Slide));
        _tooltipBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            MinWidth = 260,
        };
        PresentationDialogControlAdapter.ApplySemantic(_tooltipBox, _surface.Field(HyperlinkDialogField.Tooltip));
        _validationText = new TextBlock
        {
            Foreground = FreePBrushes.Accent,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8),
        };
        PresentationDialogControlAdapter.ApplySemantic(_validationText, _surface.Field(HyperlinkDialogField.Validation));
        AvaloniaCompactDialogChrome.ApplyCompactRadioButton(_urlRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactRadioButton(_slideRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_urlBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_slideCombo, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_tooltipBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _validationText,
            DialogChromeStyle,
            new Thickness(0, 2, 0, 8));
        _validationText.IsVisible = true;
        _slideCombo.Opacity = 1;
        _slideCombo.Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));
        _slideCombo.Styles.Add(new Style(selector => selector.OfType<ComboBox>().Class(":disabled"))
        {
            Setters =
            {
                new Setter(ComboBox.OpacityProperty, 1d),
                new Setter(ComboBox.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))),
                new Setter(ComboBox.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))),
            },
        });

        var state = _session.State;
        RenderInputState(state);
        _urlRadio.TabIndex = 0;
        _slideRadio.TabIndex = 1;
        _urlBox.TabIndex = 2;
        _slideCombo.TabIndex = 3;
        _tooltipBox.TabIndex = 4;

        _urlRadio.IsCheckedChanged += (_, _) =>
        {
            if (_urlRadio.IsChecked == true)
                RenderTargetState(_session.SelectTarget(HyperlinkDialogTargetKind.Url));
        };
        _slideRadio.IsCheckedChanged += (_, _) =>
        {
            if (_slideRadio.IsChecked == true)
                RenderTargetState(_session.SelectTarget(HyperlinkDialogTargetKind.Slide));
        };
        _urlBox.TextChanged += (_, _) => _session.SetUrlText(_urlBox.Text);
        _slideCombo.SelectionChanged += (_, _) => _session.SelectSlide(_slideCombo.SelectedIndex);
        _tooltipBox.TextChanged += (_, _) => _session.SetTooltipText(_tooltipBox.Text);

        Content = BuildContent();
        Opened += (_, _) => ApplyWpfButtonChrome();
    }

    private Control BuildContent()
    {
        var grid = new Grid
        {
            Margin = new Thickness(12),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(90) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        var radioPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, -4),
            Children = { _urlRadio, _slideRadio },
        };
        Grid.SetRow(radioPanel, 0);
        Grid.SetColumnSpan(radioPanel, 2);
        grid.Children.Add(radioPanel);

        AddLabel(grid, _surface.UrlLabel, row: 1);
        Grid.SetRow(_urlBox, 1);
        Grid.SetColumn(_urlBox, 1);
        grid.Children.Add(_urlBox);

        AddLabel(grid, _surface.SlideLabel, row: 2);
        Grid.SetRow(_slideCombo, 2);
        Grid.SetColumn(_slideCombo, 1);
        grid.Children.Add(_slideCombo);

        AddLabel(grid, _surface.TooltipLabel, row: 3);
        Grid.SetRow(_tooltipBox, 3);
        Grid.SetColumn(_tooltipBox, 1);
        grid.Children.Add(_tooltipBox);

        Grid.SetRow(_validationText, 4);
        Grid.SetColumnSpan(_validationText, 2);
        grid.Children.Add(_validationText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 13,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var ok = MakeDialogButton(
            _surface.Action(HyperlinkDialogAction.Accept),
            OnOk);
        var cancel = MakeDialogButton(
            _surface.Action(HyperlinkDialogAction.Cancel),
            () => Close(null));
        ok.TabIndex = 5;
        cancel.TabIndex = 6;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 5);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        return grid;
    }

    private static void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 4),
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
    }

    private static Button MakeDialogButton(
        PresentationDialogActionPlan<HyperlinkDialogAction> action,
        Action onClick)
    {
        var button = new Button
        {
            Content = action.Label,
            IsDefault = action.IsDefault,
            IsCancel = action.IsCancel,
        };
        AutomationProperties.SetName(button, action.AccessibleName);
        AutomationProperties.SetAutomationId(button, action.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: 75,
            isDefault: action.IsDefault);
        button.Background = action.IsDefault ? FreePBrushes.White : WpfCancelButtonBackgroundBrush;
        button.BorderBrush = action.IsDefault
            ? WpfDefaultButtonBorderBrush
            : FreePBrushes.DisabledBorder;
        button.Click += (_, _) => onClick();
        return button;
    }

    private void RenderTargetState(HyperlinkDialogViewState state)
    {
        _urlBox.IsEnabled = state.IsUrlInputEnabled;
        _slideCombo.IsEnabled = state.IsSlideInputEnabled;
        _slideCombo.Opacity = 1;
        _slideCombo.Foreground = state.IsUrlInputEnabled
            ? new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))
            : Brushes.Black;
    }

    private void RenderInputState(HyperlinkDialogViewState state)
    {
        _urlRadio.IsChecked = state.TargetKind == HyperlinkDialogTargetKind.Url;
        _slideRadio.IsChecked = state.TargetKind == HyperlinkDialogTargetKind.Slide;
        _urlBox.Text = state.UrlText;
        _slideCombo.SelectedIndex = state.SelectedSlideIndex;
        _tooltipBox.Text = state.TooltipText;
        RenderTargetState(state);
    }

    private void ApplyWpfButtonChrome()
    {
        if (Content is not Grid grid)
            return;

        var row = grid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetRow(panel) == 5);
        if (row is null)
            return;

        var buttons = row.Children.OfType<Button>().ToArray();
        if (buttons.Length > 0)
        {
            buttons[0].Background = FreePBrushes.White;
            buttons[0].BorderBrush = WpfDefaultButtonBorderBrush;
        }
        if (buttons.Length > 1)
        {
            buttons[1].Background = WpfCancelButtonBackgroundBrush;
            buttons[1].BorderBrush = FreePBrushes.DisabledBorder;
        }

        AvaloniaCompactDialogChrome.ApplyWpfDisabledComboSurface(_slideCombo);
    }

    private void OnOk() => Apply();

    private bool Apply()
    {
        var plan = _session.TryAccept();

        if (!plan.ShouldApply)
        {
            var validation = plan.Validation!;
            _validationText.Text = _session.State.ValidationText;
            _validationText.IsVisible = true;
            FocusField(validation.FocusField);
            return false;
        }

        _validationText.Text = string.Empty;
        _validationText.IsVisible = true;
        if (IsVisible)
            Close(Result);
        return true;
    }

    private void FocusField(HyperlinkDialogField field)
    {
        if (field == HyperlinkDialogField.Url)
        {
            _urlBox.Focus();
            _urlBox.SelectAll();
        }
        else if (field == HyperlinkDialogField.Slide)
        {
            _slideCombo.Focus();
        }
    }
}
