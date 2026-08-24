using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>
/// Compact Avalonia editor for the FreeP options the cross-platform shell consumes today. Parsing and
/// normalization stay in the portable <see cref="OptionsDialogPlanner"/> so WPF and Avalonia share one
/// policy, the same way <see cref="SlideSizeDialog"/> shares <c>SlideSizeDialogPlanner</c>.
/// </summary>
internal sealed partial class OptionsDialog : FreePDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly OptionsDialogSession _session;
    private readonly OptionsDialogSurfaceSpec _surface;
    private readonly TextBox _recentFilesCap = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _crashAnalytics = new()
    {
        Content = "Send privacy-filtered crash reports (takes effect next launch)",
    };
    private readonly TextBlock _status = new();

    public FreePOptions? Result { get; private set; }

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

        _recentFilesCap.Text = _session.InitialState.RecentFilesCapText;
        _defaultFormat.ItemsSource = _surface.FormatChoices;
        _defaultFormat.SelectedIndex = 0;
        _uiLanguage.Text = _session.InitialState.UiLanguage;
        _crashAnalytics.IsChecked = CrashAnalyticsConsentStore.Load().Enabled;

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
        AvaloniaLabeledFormRow.Add(grid, 0, _surface.RecentFilesLabel, _recentFilesCap);
        AvaloniaLabeledFormRow.Add(grid, 1, _surface.DefaultSaveFormatLabel, _defaultFormat);
        AvaloniaLabeledFormRow.Add(grid, 2, _surface.UiLanguageLabel, _uiLanguage, _surface.UiLanguageHint);
        AvaloniaLabeledFormRow.Add(
            grid,
            3,
            "Crash analytics:",
            _crashAnalytics,
            "Off by default. Reports are sent only when a release endpoint is configured; document contents and paths are not intentionally collected.");

        var ok = new Button { Content = _surface.AcceptLabel };
        ok.Click += (_, _) => Accept();
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: OptionsDialogPlanner.ActionButtonWidth, isDefault: true);

        var cancel = new Button { Content = _surface.CancelLabel, IsCancel = true };
        cancel.Click += (_, _) => Close();
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: OptionsDialogPlanner.ActionButtonWidth);

        var legalNotices = new Button { Content = "Legal Notices…" };
        AutomationProperties.SetName(legalNotices, "Legal Notices");
        AutomationProperties.SetAutomationId(legalNotices, "FreePOptionsLegalNoticesButton");
        AvaloniaCompactDialogChrome.ApplyButton(
            legalNotices,
            DialogChromeStyle,
            minWidth: OptionsDialogPlanner.ActionButtonWidth);
        legalNotices.Click += async (_, _) => await new LegalNoticesDialog().ShowDialog(this);

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [legalNotices, ok, cancel],
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
        var format = (_defaultFormat.SelectedItem as ApplicationOptionsFormatChoice)?.Extension;
        var commit = _session.PlanAcceptance(new BasicApplicationOptionsDialogInput(
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
        _ = CrashAnalyticsConsentStore.Save(_crashAnalytics.IsChecked == true);
        if (IsVisible)
            Close();
    }

}
