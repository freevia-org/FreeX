using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Free.Shared.Shell.Wpf;

public sealed record BackstageBackButtonSpec(
    string? AutomationId = null,
    string? AutomationName = null,
    string? AutomationHelpText = null,
    string? ToolTip = null,
    string? TooltipTitle = null,
    string? KeyTip = null);

public sealed record BackstageFrameComposerSpec(
    BackstageAccent Accent,
    IEnumerable<BackstageEntry> Entries)
{
    public Thickness? ContentPadding { get; init; }

    public BackstageBackButtonSpec? BackButton { get; init; }

    public BackstageFrameChrome? Chrome { get; init; }

    public Action<BackstageEntry?, Button>? DecorateNavButtons { get; init; }

    public Action? Closed { get; init; }
}

/// <summary>
/// Builds a configured WPF Backstage frame from app-supplied entries, metadata, and callbacks. Hosts keep
/// their panes and file commands; this composer owns the repeated frame setup shared across WPF apps.
/// </summary>
public static class BackstageFrameComposer
{
    public static BackstageFrame Build(BackstageFrameComposerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.Entries);

        var frame = new BackstageFrame(spec.Chrome);
        frame.SetAccent(spec.Accent);

        if (spec.ContentPadding is { } contentPadding)
            frame.SetContentPadding(contentPadding);

        if (spec.BackButton is { } backButton)
        {
            frame.ConfigureBackButton(
                backButton.AutomationId,
                backButton.AutomationName,
                backButton.AutomationHelpText,
                backButton.ToolTip,
                backButton.TooltipTitle,
                backButton.KeyTip);
        }

        frame.SetEntries(spec.Entries);

        if (spec.DecorateNavButtons is { } decorator)
            frame.DecorateNavButtons(decorator);

        if (spec.Closed is { } closed)
            frame.Closed += closed;

        return frame;
    }
}
