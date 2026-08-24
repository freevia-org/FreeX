using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeP.App.Compositor;
using CompositorOptions = FreeP.App.Compositor.FreePOptions;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's modal settings editor, backed by <see cref="FreePOptions"/>. It edits the real persisted
/// settings the model exposes today — the recent-files cap, the default save format, and the UI language
/// override — and nothing it cannot persist. On OK it builds a normalized <see cref="Result"/> options
/// object; the host then applies it live and saves it through the shared <c>ApplicationOptionsStore</c>.
///
/// <para>
/// Code-only to match the rest of the FreeP window style (see <see cref="SlideSizeDialog"/>). Parsing and
/// normalization stay in the portable <see cref="OptionsDialogPlanner"/> so WPF and Avalonia share one
/// policy.
/// </para>
/// </summary>
internal sealed partial class OptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly OptionsDialogSession _session;
    private readonly OptionsDialogSurfaceSpec _surface;

    private readonly TextBox _recentFilesCap = new() { MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _defaultFormat = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _uiLanguage = new() { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _crashAnalytics = new()
    {
        Content = "Send privacy-filtered crash reports (takes effect next launch)",
    };
    private readonly TextBlock _status = new() { Foreground = System.Windows.Media.Brushes.Firebrick, Visibility = Visibility.Collapsed };

    /// <summary>The normalized options produced on OK; equals the input options on Cancel.</summary>
    public CompositorOptions Result { get; private set; }

    public OptionsDialog(Window owner, CompositorOptions options)
    {
        _session = new OptionsDialogSession(options, System.Globalization.CultureInfo.CurrentCulture);
        _surface = _session.Surface;
        Result = _session.InitialResult;

        Owner = owner;
        Title = _surface.Title;
        Width = OptionsDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _recentFilesCap.Text = _session.InitialState.RecentFilesCapText;
        _defaultFormat.ItemsSource = _surface.FormatChoices;
        _defaultFormat.SelectedIndex = 0;
        _uiLanguage.Text = _session.InitialState.UiLanguage;
        _crashAnalytics.IsChecked = CrashAnalyticsConsentStore.Load().Enabled;

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
        AddRow(grid, 3, "Crash analytics:", _crashAnalytics,
            "Off by default. Reports are sent only when a release endpoint is configured; document contents and paths are not intentionally collected.");

        _status.Margin = new Thickness(OptionsDialogPlanner.ContentMargin, 0, OptionsDialogPlanner.ContentMargin, 0);

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: OptionsDialogPlanner.ActionButtonWidth,
            rowMargin: new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowTopMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowBottomMargin),
            acceptContent: _surface.AcceptLabel,
            cancelContent: _surface.CancelLabel);
        var legalNotices = new Button
        {
            Content = "_Legal Notices…",
            MinWidth = OptionsDialogPlanner.ActionButtonWidth,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(legalNotices, "Legal Notices");
        AutomationProperties.SetAutomationId(legalNotices, "FreePOptionsLegalNoticesButton");
        legalNotices.Click += (_, _) => new LegalNoticesDialog { Owner = this }.ShowDialog();
        buttons.Children.Insert(0, legalNotices);

        Content = new StackPanel
        {
            Children = { grid, _status, buttons },
        };

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_recentFilesCap);
    }

    private void Accept()
    {
        _status.Visibility = Visibility.Collapsed;
        var format = (_defaultFormat.SelectedItem as ApplicationOptionsFormatChoice)?.Extension;
        var commit = _session.PlanAcceptance(new BasicApplicationOptionsDialogInput(
            _recentFilesCap.Text,
            format,
            _uiLanguage.Text));
        if (!commit.ShouldApply)
        {
            _status.Text = commit.Validation?.Message ?? OptionsDialogSession.RecentFilesCapValidationMessage;
            _status.Visibility = Visibility.Visible;
            DialogFocus.FocusAndSelect(_recentFilesCap);
            return;
        }

        Result = commit.Result!;
        _ = CrashAnalyticsConsentStore.Save(_crashAnalytics.IsChecked == true);
        if (IsLoaded)
        {
            DialogResult = true;
            Close();
        }
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement field, string? hint = null)
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

        FrameworkElement value = field;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            value = new StackPanel
            {
                Children =
                {
                    field,
                    new TextBlock
                    {
                        Text = hint,
                        FontSize = 11,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                },
            };
        }

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);

        grid.Children.Add(text);
        grid.Children.Add(value);
    }
}
