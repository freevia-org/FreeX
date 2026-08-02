using System.Windows;
using System.Windows.Controls;
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
}
