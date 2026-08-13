using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small read-only modal showing the result of "Check Accessibility": the
/// <see cref="AccessibilityReport"/> produced by the pure <see cref="AccessibilityChecker"/>, with issues
/// grouped by <see cref="AccessibilitySeverity"/> (Errors, then Warnings, then Tips) and each issue's
/// human-readable message listed under its group. Code-only to match the rest of the FreeW window style;
/// purely informational, so it has a single Close button. Mirrors <see cref="StatisticsDialog"/>.
/// </summary>
internal sealed class AccessibilityReportDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    public AccessibilityReportDialog(Window owner, AccessibilityReport report)
    {
        var plan = AccessibilityReportDialogPlanner.Build(report);
        Owner = owner;
        Title = plan.Title;
        Width = 460;
        MaxHeight = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var outer = new StackPanel { Margin = new Thickness(16, 14, 16, 8) };

        // Summary line: counts by severity, or a clean-bill-of-health message.
        outer.Children.Add(new TextBlock
        {
            Text = plan.Summary,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        if (!plan.IsClean)
        {
            var list = new StackPanel();
            foreach (var group in plan.Groups)
                AddGroup(list, group);

            outer.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 420,
                Content = list
            });
        }

        // Reuse the shared OK-only button row (accelerator, automation name, shell strings; the single OK
        // button is IsDefault + IsCancel so Enter/Esc both close). Matches FreeX's informational dialogs.
        outer.Children.Add(DialogButtonRowFactory.CreateOkOnly(Close, buttonWidth: 84, rowMargin: new Thickness(0, 12, 0, 4)));

        Content = outer;
    }

    private static void AddGroup(StackPanel parent, AccessibilityDialogGroupPlan group)
    {
        var accent = (Color)ColorConverter.ConvertFromString(group.AccentHex);

        parent.Children.Add(new TextBlock
        {
            Text = group.Heading,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 8, 0, 2)
        });

        foreach (var issueLine in group.IssueLines)
        {
            parent.Children.Add(new TextBlock
            {
                Text = issueLine,
                Margin = new Thickness(8, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }
}
