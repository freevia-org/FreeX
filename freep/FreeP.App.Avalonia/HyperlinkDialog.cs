using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class HyperlinkDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly RadioButton _urlRadio;
    private readonly RadioButton _slideRadio;
    private readonly TextBox _urlBox;
    private readonly ComboBox _slideCombo;
    private readonly TextBox _tooltipBox;
    private readonly TextBlock _validationText;

    internal Hyperlink? Result { get; private set; }

    internal bool ApplyForVisualEvidence(
        HyperlinkDialogTargetKind targetKind,
        string url,
        int selectedSlideIndex,
        string tooltip)
    {
        _urlRadio.IsChecked = targetKind == HyperlinkDialogTargetKind.Url;
        _slideRadio.IsChecked = targetKind == HyperlinkDialogTargetKind.Slide;
        _urlBox.Text = url;
        _slideCombo.SelectedIndex = selectedSlideIndex;
        _tooltipBox.Text = tooltip;
        UpdateEnabled();
        return Apply();
    }

    public HyperlinkDialog(IReadOnlyList<Slide> slides, Hyperlink? current = null)
        : this(HyperlinkDialogPlanner.BuildDialogRequest(slides, current))
    {
    }

    internal HyperlinkDialog(HyperlinkDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Title = HyperlinkDialogPlanner.Caption;
        Width = 405.3333333333333;
        Height = 216;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));

        _urlRadio = new RadioButton
        {
            Content = "Web address",
            GroupName = "HyperlinkTarget",
            Margin = new Thickness(0, 0, 0, 4),
        };
        _slideRadio = new RadioButton
        {
            Content = "Slide in this presentation",
            GroupName = "HyperlinkTarget",
            Margin = new Thickness(0, 0, 0, 8),
        };
        _urlBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 6),
            MinWidth = 260,
            PlaceholderText = "https://example.com",
        };
        _slideCombo = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 6),
            MinWidth = 260,
            ItemsSource = request.SlideOptions,
            SelectedIndex = request.SelectedSlideIndex,
        };
        _tooltipBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 6),
            MinWidth = 260,
        };
        _validationText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8),
        };
        AvaloniaCompactDialogChrome.ApplyRadioButton(_urlRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_slideRadio, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_urlBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_slideCombo, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_tooltipBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _validationText,
            DialogChromeStyle,
            new Thickness(0, 2, 0, 4));

        var initial = request.InitialState;
        _urlRadio.IsChecked = initial.TargetKind == HyperlinkDialogTargetKind.Url;
        _slideRadio.IsChecked = initial.TargetKind == HyperlinkDialogTargetKind.Slide;
        _urlBox.Text = initial.UrlText;
        _tooltipBox.Text = initial.TooltipText;

        _urlRadio.IsCheckedChanged += (_, _) => UpdateEnabled();
        _slideRadio.IsCheckedChanged += (_, _) => UpdateEnabled();

        Content = BuildContent();
        UpdateEnabled();
    }

    private Control BuildContent()
    {
        var grid = new Grid
        {
            Margin = new Thickness(14),
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
                new ColumnDefinition { Width = new GridLength(96) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        var radioPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 6),
            Children = { _urlRadio, _slideRadio },
        };
        Grid.SetRow(radioPanel, 0);
        Grid.SetColumnSpan(radioPanel, 2);
        grid.Children.Add(radioPanel);

        AddLabel(grid, "URL:", row: 1);
        Grid.SetRow(_urlBox, 1);
        Grid.SetColumn(_urlBox, 1);
        grid.Children.Add(_urlBox);

        AddLabel(grid, "Target slide:", row: 2);
        Grid.SetRow(_slideCombo, 2);
        Grid.SetColumn(_slideCombo, 1);
        grid.Children.Add(_slideCombo);

        AddLabel(grid, "Tooltip:", row: 3);
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
            Spacing = 8,
            Children =
            {
                MakeDialogButton("OK", isDefault: true, OnOk),
                MakeDialogButton("Cancel", isDefault: false, () => Close(null)),
            },
        };
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
            Margin = new Thickness(0, 0, 10, 6),
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
    }

    private static Button MakeDialogButton(string label, bool isDefault, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            IsDefault = isDefault,
            IsCancel = !isDefault,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 74, isDefault: isDefault);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void UpdateEnabled()
    {
        var isUrl = _urlRadio.IsChecked == true;
        _urlBox.IsEnabled = isUrl;
        _slideCombo.IsEnabled = !isUrl;
    }

    private void OnOk() => Apply();

    private bool Apply()
    {
        var selectedSlideId = (_slideCombo.SelectedItem as HyperlinkDialogSlideOption)?.Id;
        var plan = HyperlinkDialogPlanner.BuildResult(
            _urlRadio.IsChecked == true
                ? HyperlinkDialogTargetKind.Url
                : HyperlinkDialogTargetKind.Slide,
            _urlBox.Text,
            selectedSlideId,
            _tooltipBox.Text);

        if (!plan.ShouldApply)
        {
            var validation = plan.Validation!;
            _validationText.Text = validation.Message;
            FocusField(validation.FocusField);
            return false;
        }

        Result = plan.Result;
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
