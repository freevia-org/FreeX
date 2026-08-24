using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class OptionsDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Options_uses_Wpf_table_geometry_and_shared_action_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions
            {
                AutoCorrect = new AutoCorrectOptions
                {
                    Replacements = [new AutoCorrectReplacement("teh", "the")],
                },
            });
            try
            {
                dialog.Width.Should().Be(460);
                dialog.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);

                var table = GetField<Border>(dialog, "_replacements");
                table.Height.Should().Be(180);
                var scroller = table.Child.Should().BeOfType<ScrollViewer>().Subject;
                scroller.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
                scroller.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
                var grid = GetField<Grid>(dialog, "_replacementGrid");
                scroller.Content.Should().BeSameAs(grid);
                grid.ColumnDefinitions.Count.Should().Be(2, "the WPF DataGrid declares two replacement columns");
                grid.ColumnDefinitions[0].Width.IsStar.Should().BeTrue();
                grid.ColumnDefinitions[0].Width.Value.Should().Be(1);
                grid.ColumnDefinitions[1].Width.IsStar.Should().BeTrue();
                grid.ColumnDefinitions[1].Width.Value.Should().Be(2);
                grid.RowDefinitions.Count.Should().Be(3, "the WPF DataGrid has one populated row plus its blank add row");
                grid.RowDefinitions[0].Height.Should().Be(new GridLength(26), "the WPF DataGrid header is 26px at 96 DPI");
                dialog.ReplacementEditorsForTest.Should().HaveCount(2);
                dialog.ReplacementEditorsForTest[0].Replace.BorderBrush.Should().Be(Brushes.Black);

                var buttons = dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Where(button => button is not ToggleButton)
                    .ToArray();
                buttons.Select(UserFacingButtonText)
                    .Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
                buttons[0].MinWidth.Should().Be(84);
                buttons[0].IsDefault.Should().BeTrue();
                buttons[0].IsCancel.Should().BeFalse();
                buttons[1].IsCancel.Should().BeTrue();
                buttons[1].IsDefault.Should().BeFalse();
                buttons.Select(AutomationProperties.GetName)
                    .Should().OnlyContain(name => !string.IsNullOrWhiteSpace(name));
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Options_realizes_replacement_editors_in_one_to_two_viewport_geometry()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions
            {
                AutoCorrect = new AutoCorrectOptions
                {
                    ReplaceText = true,
                    Replacements = [new AutoCorrectReplacement("(tm)", "™")],
                },
            });
            try
            {
                dialog.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var grid = GetField<Grid>(dialog, "_replacementGrid");
                var editors = dialog.ReplacementEditorsForTest;
                grid.Bounds.Width.Should().BeGreaterThan(300);
                editors.Should().HaveCount(2);
                editors[0].Replace.Bounds.Width.Should().BeGreaterThan(80);
                editors[0].With.Bounds.Width.Should().BeGreaterThan(160);
                (editors[0].With.Bounds.Width / editors[0].Replace.Bounds.Width).Should().BeApproximately(2, 0.05);
                editors[0].Replace.Text.Should().Be("(tm)");
                editors[0].With.Text.Should().Be("™");
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Options_selects_the_recent_files_field_on_open_and_commits_grid_rows()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions
            {
                RecentFilesCap = 12,
                AutoCorrect = new AutoCorrectOptions
                {
                    ReplaceText = true,
                    Replacements = [new AutoCorrectReplacement("teh", "the")],
                },
            });
            try
            {
                dialog.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                dialog.RecentFilesCapForTest.IsFocused.Should().BeTrue();
                dialog.RecentFilesCapForTest.SelectionStart.Should().Be(0);
                dialog.RecentFilesCapForTest.SelectionEnd.Should().Be(2);

                var row = dialog.ReplacementEditorsForTest[0];
                row.Replace.Text = " adn ";
                row.With.Text = "and";
                dialog.AcceptForTest();

                dialog.Result.Should().NotBeNull();
                dialog.Result!.RecentFilesCap.Should().Be(12);
                dialog.Result.AutoCorrect.ReplaceText.Should().BeTrue();
                dialog.Result.AutoCorrect.Replacements
                    .Should().ContainSingle(replacement => replacement.Replace == "adn" && replacement.With == "and");
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Options_matches_wpf_action_order_and_autocorrect_state_policy()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions
            {
                AutoCorrectEnabled = false,
                AutoFormat = AutoFormatOptions.Default,
                AutoCorrect = new AutoCorrectOptions
                {
                    CorrectTwoInitialCapitals = true,
                    ReplaceText = false,
                    Replacements = [new AutoCorrectReplacement("teh", "the")],
                },
            });
            try
            {
                var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
                var root = dialog.Content.Should().BeOfType<StackPanel>().Subject;
                root.Children.Should().HaveCount(3);
                root.Children[0].Should().BeSameAs(tabs);
                root.Children[1].Should().BeSameAs(GetField<TextBlock>(dialog, "_status"));
                root.Children[2].Should().BeOfType<StackPanel>();

                tabs.Items.Cast<TabItem>().Select(item => item.Header?.ToString())
                    .Should().Equal("General", "AutoCorrect", "AutoFormat As You Type");

                var checks = dialog.GetLogicalDescendants().OfType<CheckBox>().ToArray();
                var master = checks.Single(check => check.Content?.ToString() == "Enable AutoCorrect (smart typing) as you type");
                var smartQuotes = checks.Single(check => check.Content?.ToString()!.StartsWith("Straight quotes", StringComparison.Ordinal) == true);
                var replaceText = checks.Single(check => check.Content?.ToString() == "Replace text as you type");
                var table = GetField<Border>(dialog, "_replacements");

                master.IsChecked.Should().BeFalse();
                smartQuotes.IsEnabled.Should().BeFalse();
                replaceText.IsChecked.Should().BeFalse();
                table.IsEnabled.Should().BeFalse();

                master.IsChecked = true;
                smartQuotes.IsEnabled.Should().BeTrue();
                replaceText.IsChecked = true;
                table.IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Options_autoformat_matches_wpf_row_spacing_and_control_geometry()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions());
            try
            {
                var checks = dialog.GetLogicalDescendants().OfType<CheckBox>().ToArray();
                var master = checks.Single(check => check.Content?.ToString() == "Enable AutoCorrect (smart typing) as you type");
                var rules = checks.Where(check => check.Content?.ToString() is not null &&
                    check.Content.ToString() != "Enable AutoCorrect (smart typing) as you type" &&
                    check.Content.ToString() != "Correct TWo INitial CApitals" &&
                    check.Content.ToString() != "Capitalize names of days" &&
                    check.Content.ToString() != "Replace text as you type" &&
                    check.Content.ToString() != "Send privacy-filtered crash reports (takes effect next launch)").ToArray();
                var autoFormatPanel = master.Parent as StackPanel;
                var section = dialog.GetLogicalDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == OptionsDialogPlanner.AutoFormatSectionLabel);

                master.Margin.Should().Be(new Thickness(0, 0, 0, 8));
                rules.Should().HaveCount(10);
                rules.Should().OnlyContain(check => check.Margin == new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0));
                rules.Should().OnlyContain(check => check.Height == 16 && check.MinHeight == 16 && check.MaxHeight == 16);
                section.Margin.Should().Be(new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0));
                autoFormatPanel.Should().NotBeNull();
                autoFormatPanel!.Margin.Left.Should().Be(OptionsDialogPlanner.ContentMargin + OptionsDialogPlanner.ToggleTopMargin);
                master.IsChecked.Should().BeTrue();
                rules.Should().OnlyContain(check => check.IsChecked == true && check.IsEnabled);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Options_autocorrect_pane_uses_retained_wpf_width_and_action_semantics()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions
            {
                AutoCorrect = new AutoCorrectOptions
                {
                    ReplaceText = true,
                    Replacements = [new AutoCorrectReplacement("teh", "the")],
                },
            });
            try
            {
                dialog.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                tabs.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(presenter => presenter.Name == "PART_SelectedContentHost")
                    .Bounds.Width.Should().Be(OptionsDialogPlanner.DialogWidth - (2 * OptionsDialogPlanner.TabMargin));
                tabs.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var pane = tabs.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(presenter => presenter.Name == "PART_SelectedContentHost");
                pane.Bounds.Width.Should().Be(
                    OptionsDialogPlanner.DialogWidth - (2 * OptionsDialogPlanner.TabMargin) - OptionsDialogPlanner.AutoCorrectTabPaneRightInset);

                var buttons = dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Where(button => button is not ToggleButton)
                    .ToArray();
                buttons.Select(UserFacingButtonText).Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
                buttons[0].IsDefault.Should().BeTrue();
                buttons[1].IsCancel.Should().BeTrue();
                buttons[0].BorderBrush.Should().BeAssignableTo<ISolidColorBrush>().Which.Color.Should().Be(Color.FromRgb(200, 200, 200));
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Options_general_fields_share_the_Wpf_left_aligned_value_column()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new OptionsDialog(new FreeWOptions());
            try
            {
                dialog.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                Control[] fields =
                {
                    GetField<TextBox>(dialog, "_recentFilesCap"),
                    GetField<ComboBox>(dialog, "_defaultFormat"),
                    GetField<TextBox>(dialog, "_uiLanguage"),
                };
                var general = dialog.GetLogicalDescendants().OfType<TabItem>()
                    .Single(item => item.Header?.ToString() == "General")
                    .Content.Should().BeOfType<Grid>().Subject;
                fields.Select(field => field.HorizontalAlignment)
                    .Should().OnlyContain(alignment => alignment == HorizontalAlignment.Left);
                fields.Select(field => field.TranslatePoint(new Point(0, 0), general)!.Value.X)
                    .Distinct()
                    .Should().ContainSingle("WPF places all General value controls in one left-aligned column");
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    private static T GetField<T>(OptionsDialog dialog, string name) where T : class =>
        (T)(typeof(OptionsDialog)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing OptionsDialog field {name}."));

    private static string? UserFacingButtonText(Button button) => button.Content switch
    {
        string text => text,
        AccessText accessText => accessText.Text,
        TextBlock textBlock => textBlock.Text,
        _ => button.Content?.ToString(),
    };
}
