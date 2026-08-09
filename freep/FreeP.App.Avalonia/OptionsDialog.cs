using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>
/// Compact Avalonia editor for the FreeP options the cross-platform shell consumes today. Parsing and
/// normalization stay in the portable <see cref="OptionsDialogPlanner"/> so WPF and Avalonia share one
/// policy, the same way <see cref="SlideSizeDialog"/> shares <c>SlideSizeDialogPlanner</c>.
/// </summary>
internal sealed class OptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly OptionsDialogSession _session;
    private readonly OptionsDialogSurfaceSpec _surface;
    private readonly TextBox _recentFilesCap = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = new();

    public FreePOptions? Result { get; private set; }

    internal TextBox RecentFilesCapForTest => _recentFilesCap;
    internal ComboBox DefaultFormatForTest => _defaultFormat;
    internal TextBox UiLanguageForTest => _uiLanguage;
    internal TextBlock StatusForTest => _status;

    public OptionsDialog(FreePOptions options)
    {
        _session = new OptionsDialogSession(options, System.Globalization.CultureInfo.CurrentCulture);
        _surface = _session.Surface;

        Title = _surface.Title;
        Width = OptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _recentFilesCap.Text = _session.InitialState.RecentFilesCapText;
        _defaultFormat.ItemsSource = _surface.FormatChoices;
        _defaultFormat.SelectedIndex = 0;
        _uiLanguage.Text = _session.InitialState.UiLanguage;

        AvaloniaCompactDialogChrome.ApplyTextBox(_recentFilesCap, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_defaultFormat, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_uiLanguage, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(16, 8, 16, 0));

        Content = BuildContent();

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close();
            e.Handled = true;
        };

        Opened += (_, _) => AvaloniaCompactDialogChrome.FocusAndSelect(_recentFilesCap);
    }

    internal void AcceptForTest() => Accept();

    private Control BuildContent()
    {
        var grid = new Grid
        {
            Margin = new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ContentBottomMargin),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        AddRow(grid, 0, _surface.RecentFilesLabel, _recentFilesCap);
        AddRow(grid, 1, _surface.DefaultSaveFormatLabel, _defaultFormat);
        AddRow(grid, 2, _surface.UiLanguageLabel, _uiLanguage, _surface.UiLanguageHint);

        var ok = new Button { Content = "OK" };
        ok.Click += (_, _) => Accept();
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: OptionsDialogPlanner.ActionButtonWidth, isDefault: true);

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: OptionsDialogPlanner.ActionButtonWidth);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowTopMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowBottomMargin));

        var root = new StackPanel();
        root.Children.Add(grid);
        root.Children.Add(_status);
        root.Children.Add(buttons);
        return root;
    }

    private void Accept()
    {
        _status.IsVisible = false;
        var format = (_defaultFormat.SelectedItem as OptionsDialogFormatChoice)?.Extension;
        var commit = _session.PlanAcceptance(new OptionsDialogInput(
            _recentFilesCap.Text,
            format,
            _uiLanguage.Text));
        if (!commit.ShouldApply)
        {
            _status.Text = commit.Validation?.Message ?? OptionsDialogSession.RecentFilesCapValidationMessage;
            _status.IsVisible = true;
            _recentFilesCap.Focus();
            _recentFilesCap.SelectAll();
            return;
        }

        Result = commit.Result;
        if (IsVisible)
            Close();
    }

    private static void AddRow(Grid grid, int row, string label, Control field, string? hint = null)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);

        field.Margin = new Thickness(0, 4, 0, 4);

        Control value = field;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var stack = new StackPanel();
            stack.Children.Add(field);
            stack.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });
            value = stack;
        }

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);

        grid.Children.Add(text);
        grid.Children.Add(value);
    }
}
