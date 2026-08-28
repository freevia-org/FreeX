using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Free.Shared.Shell.Wpf;

/// <summary>Accent brushes used by a WPF Backstage host wrapper.</summary>
public readonly record struct BackstageAccent(
    Color Sidebar,
    Color Hover,
    Color Selected,
    Color Separator);

/// <summary>
/// Shared wrapper for app-specific WPF Backstage views. The app still supplies entries and panes; this
/// shell owns the repeated host control setup, frame tinting, visibility, and closed callback plumbing.
/// </summary>
public sealed class BackstageViewShell
{
    private readonly UserControl _host;
    private readonly Action _onClosed;

    public BackstageViewShell(
        UserControl host,
        BackstageAccent accent,
        IEnumerable<BackstageEntry> entries,
        Action onClosed,
        BackstageFrameChrome? chrome = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(onClosed);

        _host = host;
        _onClosed = onClosed;
        Frame = BackstageFrameComposer.Build(new BackstageFrameComposerSpec(accent, entries)
        {
            Chrome = chrome,
            Closed = OnFrameClosed
        });

        _host.Padding = new Thickness(0);
        _host.Background = Brushes.White;
        _host.Content = Frame;
        _host.Visibility = Visibility.Collapsed;
    }

    public BackstageFrame Frame { get; }

    public void Show(string? paneLabelOrAutomationId = null)
    {
        _host.Visibility = Visibility.Visible;
        Frame.Show(paneLabelOrAutomationId);
    }

    public void Hide() => Frame.Hide();

    private void OnFrameClosed()
    {
        _host.Visibility = Visibility.Collapsed;
        _onClosed();
    }
}
