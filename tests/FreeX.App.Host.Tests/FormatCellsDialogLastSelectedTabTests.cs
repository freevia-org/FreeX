using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.App.Host;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormatCellsDialogLastSelectedTabTests
{
    [Fact]
    public void FormatCellsDialog_DefaultReopenRestoresLastTabAfterCancel_AndExplicitTabOverridesIt()
    {
        StaTestRunner.Run(() =>
        {
            var first = new FormatCellsDialog(CellStyle.Default, WorkbookTheme.Office);
            first.Dispatcher.BeginInvoke(() =>
            {
                var tabs = (TabControl)first.FindName("Tabs");
                tabs.SelectedIndex = (int)FormatCellsDialogTab.Font;
                CancelDialog(first);
            });

            first.ShowDialog();
            first.DialogResult.Should().BeFalse();

            var reopened = new FormatCellsDialog(CellStyle.Default, WorkbookTheme.Office);
            reopened.Dispatcher.BeginInvoke(() =>
            {
                ((TabControl)reopened.FindName("Tabs")).SelectedIndex
                    .Should().Be((int)FormatCellsDialogTab.Font);
                CancelDialog(reopened);
            });

            reopened.ShowDialog();
            reopened.DialogResult.Should().BeFalse();

            var overridden = new FormatCellsDialog(
                CellStyle.Default,
                WorkbookTheme.Office,
                FormatCellsDialogTab.Alignment);
            overridden.Dispatcher.BeginInvoke(() =>
            {
                ((TabControl)overridden.FindName("Tabs")).SelectedIndex
                    .Should().Be((int)FormatCellsDialogTab.Alignment);
                CancelDialog(overridden);
            });

            overridden.ShowDialog();
            overridden.DialogResult.Should().BeFalse();
        });
    }

    private static void CancelDialog(FormatCellsDialog dialog)
    {
        var cancel = FindVisualChildren<Button>(dialog)
            .Single(button => AutomationProperties.GetAutomationId(button) == "FormatCellsCancelButton");
        ((IInvokeProvider)new ButtonAutomationPeer(cancel)).Invoke();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
