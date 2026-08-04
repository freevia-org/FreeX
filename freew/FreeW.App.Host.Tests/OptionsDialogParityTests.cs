using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Host;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Host.Tests;

public sealed class OptionsDialogParityTests
{
    [StaFact]
    public void Wpf_options_keeps_tab_content_before_the_shared_action_row()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new OptionsDialog(owner, new());
        try
        {
            dialog.Width.Should().Be(OptionsDialogPlanner.DialogWidth);
            var root = dialog.Content.Should().BeOfType<StackPanel>().Subject;
            root.Children.Count.Should().Be(2);

            var tabs = root.Children[0].Should().BeOfType<TabControl>().Subject;
            var actionRow = root.Children[1].Should().BeOfType<StackPanel>().Subject;
            tabs.Margin.Should().Be(new Thickness(OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, OptionsDialogPlanner.TabMargin, 0));
            actionRow.Margin.Should().Be(new Thickness(
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowTopMargin,
                OptionsDialogPlanner.ContentMargin,
                OptionsDialogPlanner.ActionRowBottomMargin));
            actionRow.Children.OfType<Button>().Select(button => button.Content?.ToString())
                .Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [StaFact]
    public void Wpf_autocorrect_table_realizes_declared_star_columns_across_available_width()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new OptionsDialog(owner, new FreeWOptions
        {
            AutoCorrect = new AutoCorrectOptions
            {
                ReplaceText = true,
                Replacements = [new AutoCorrectReplacement("(tm)", "™")],
            },
        });
        try
        {
            var declaredTable = (DataGrid)typeof(OptionsDialog)
                .GetField("_replacements", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            declaredTable.Columns[0].Width.UnitType.Should().Be(DataGridLengthUnitType.Star);
            declaredTable.Columns[0].Width.Value.Should().Be(1);
            declaredTable.Columns[1].Width.UnitType.Should().Be(DataGridLengthUnitType.Star);
            declaredTable.Columns[1].Width.Value.Should().Be(2);

            dialog.Show();
            dialog.UpdateLayout();
            var tabs = FindVisualChildren<TabControl>(dialog).Single();
            tabs.SelectedIndex = 1;
            dialog.UpdateLayout();
            dialog.UpdateLayout();

            var table = FindVisualChildren<DataGrid>(dialog).Single();
            table.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
            table.ActualWidth.Should().BeGreaterThan(300);
            table.Columns.Should().HaveCount(2);
            table.Columns[0].ActualWidth.Should().BeGreaterThan(80);
            table.Columns[1].ActualWidth.Should().BeGreaterThan(160);
            (table.Columns[1].ActualWidth / table.Columns[0].ActualWidth).Should().BeApproximately(2, 0.05);

            FindVisualChildren<TextBlock>(table).Select(text => text.Text)
                .Should().Contain("(tm)")
                .And.Contain("™");
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [StaFact]
    public void Wpf_autocorrect_pane_keeps_authority_width_and_action_semantics()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new OptionsDialog(owner, new FreeWOptions
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
            dialog.UpdateLayout();
            var tabs = FindVisualChildren<TabControl>(dialog).Single();
            tabs.SelectedIndex = 1;
            dialog.UpdateLayout();
            dialog.UpdateLayout();

            var pane = ((TabItem)tabs.SelectedItem).Content.Should().BeOfType<Grid>().Subject;
            pane.ActualWidth.Should().BeApproximately(378.6666666667, 0.1);

            var buttons = FindVisualChildren<Button>(dialog)
                .Where(button => button.IsVisible && button.Content is not null)
                .ToArray();
            buttons.Select(button => button.Content?.ToString())
                .Should().Equal(ShellStrings.Current.Ok, ShellStrings.Current.Cancel);
            buttons[0].IsDefault.Should().BeTrue();
            buttons[1].IsCancel.Should().BeTrue();
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [StaFact]
    public void Wpf_autoformat_uses_shared_row_spacing_and_enabled_state()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new OptionsDialog(owner, new FreeWOptions());
        try
        {
            var master = GetField<CheckBox>(dialog, "_autoCorrectEnabled");
            var rules = new[]
            {
                GetField<CheckBox>(dialog, "_smartQuotes"),
                GetField<CheckBox>(dialog, "_dashes"),
                GetField<CheckBox>(dialog, "_ellipsis"),
                GetField<CheckBox>(dialog, "_symbols"),
                GetField<CheckBox>(dialog, "_capitalization"),
                GetField<CheckBox>(dialog, "_bulletedLists"),
                GetField<CheckBox>(dialog, "_numberedLists"),
                GetField<CheckBox>(dialog, "_ordinals"),
                GetField<CheckBox>(dialog, "_fractions"),
                GetField<CheckBox>(dialog, "_hyperlinks"),
            };

            master.Margin.Should().Be(new Thickness(0, 0, 0, 8));
            rules.Should().OnlyContain(check => check.Margin == new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0));
            master.IsChecked.Should().BeTrue();
            rules.Should().OnlyContain(check => check.IsChecked == true && check.IsEnabled);

            dialog.Show();
            dialog.UpdateLayout();
            var tabs = FindVisualChildren<TabControl>(dialog).Single();
            tabs.SelectedIndex = 2;
            dialog.UpdateLayout();
            var section = FindVisualChildren<TextBlock>(dialog)
                .Single(text => text.Text == OptionsDialogPlanner.AutoFormatSectionLabel);
            section.Margin.Should().Be(new Thickness(0, OptionsDialogPlanner.ToggleTopMargin, 0, 0));
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    private static T GetField<T>(OptionsDialog dialog, string name) where T : class =>
        (T)(typeof(OptionsDialog)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing OptionsDialog field {name}."));

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value)
            yield return value;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }
}
