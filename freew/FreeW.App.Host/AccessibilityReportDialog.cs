using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        Owner = owner;
        Title = "Accessibility Checker";
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
            Text = report.IsClean
                ? "No accessibility issues found."
                : $"{report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.TipCount} tip(s).",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        if (!report.IsClean)
        {
            var list = new StackPanel();
            AddGroup(list, "Errors", AccessibilitySeverity.Error, report, Color.FromRgb(0xC0, 0x00, 0x00));
            AddGroup(list, "Warnings", AccessibilitySeverity.Warning, report, Color.FromRgb(0xB8, 0x6A, 0x00));
            AddGroup(list, "Tips", AccessibilitySeverity.Tip, report, Color.FromRgb(0x40, 0x40, 0x40));

            outer.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 420,
                Content = list
            });
        }

        var close = new Button
        {
            Content = "Close",
            MinWidth = 84,
            IsDefault = true,
            IsCancel = true,
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 4)
        };
        close.Click += (_, _) => Close();
        outer.Children.Add(close);

        Content = outer;
    }

    // Add a severity group header plus one bullet line per issue in that group; emits nothing when empty.
    private static void AddGroup(
        StackPanel parent, string heading, AccessibilitySeverity severity, AccessibilityReport report, Color accent)
    {
        var issues = report.Issues.Where(i => i.Severity == severity).ToList();
        if (issues.Count == 0)
            return;

        parent.Children.Add(new TextBlock
        {
            Text = $"{heading} ({issues.Count})",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 8, 0, 2)
        });

        foreach (var issue in issues)
        {
            parent.Children.Add(new TextBlock
            {
                Text = $"•  {issue.Message}",
                Margin = new Thickness(8, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }
}
