using System.Windows;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

public sealed record SisterBackstageEntrySpec(
    Func<UIElement> BuildInfoPane,
    Action New,
    Action Open,
    Action Save,
    Action SaveAs,
    Func<UIElement> BuildRecentPane,
    Func<UIElement> BuildNewPane,
    Func<UIElement> BuildOptionsPane)
{
    public Action? Print { get; init; }

    public Action? SaveCopy { get; init; }

    public Action? Close { get; init; }

    public Func<UIElement>? BuildHomePane { get; init; }

    public bool UseNewPane { get; init; }

    public Func<UIElement>? BuildOpenPane { get; init; }

    public Action? ImportPdfText { get; init; }

    public Func<UIElement>? BuildSharePane { get; init; }

    public Func<UIElement>? BuildSaveAsPane { get; init; }

    public Func<UIElement>? BuildPrintPane { get; init; }

    public Func<UIElement>? BuildExportPane { get; init; }

    public Func<UIElement>? BuildAccountPane { get; init; }

    public bool HideRecentPane { get; init; }
}

/// <summary>
/// Builds the common File/Backstage rail used by the WPF sister apps. Hosts still own panes and actions;
/// this helper owns the shared Office-style entry order, labels, icons, docking, and optional Print/Export slots.
/// </summary>
public static class SisterBackstageEntryBuilder
{
    public static IReadOnlyList<BackstageEntry> Build(SisterBackstageEntrySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(spec.BuildInfoPane);
        ArgumentNullException.ThrowIfNull(spec.New);
        ArgumentNullException.ThrowIfNull(spec.Open);
        ArgumentNullException.ThrowIfNull(spec.Save);
        ArgumentNullException.ThrowIfNull(spec.SaveAs);
        ArgumentNullException.ThrowIfNull(spec.BuildRecentPane);
        ArgumentNullException.ThrowIfNull(spec.BuildNewPane);
        ArgumentNullException.ThrowIfNull(spec.BuildOptionsPane);

        var entries = new List<BackstageEntry>();

        var hasHomePane = spec.BuildHomePane is not null;

        if (spec.BuildHomePane is not null)
            entries.Add(BackstageEntry.Pane("Home", BackstageIconKind.Grid, spec.BuildHomePane, iconName: "home"));

        if (!hasHomePane)
            entries.Add(BackstageEntry.Pane("Info", BackstageIconKind.Info, spec.BuildInfoPane, iconName: "info"));

        entries.Add(spec.UseNewPane
            ? BackstageEntry.Pane("New", BackstageIconKind.Insert, spec.BuildNewPane, iconName: "new")
            : BackstageEntry.Command("New", BackstageIconKind.Insert, spec.New, iconName: "new"));

        entries.Add(spec.BuildOpenPane is null
            ? BackstageEntry.Command("Open", BackstageIconKind.GetData, spec.Open, iconName: "open")
            : BackstageEntry.Pane("Open", BackstageIconKind.GetData, spec.BuildOpenPane, iconName: "open"));

        if (spec.ImportPdfText is not null)
            entries.Add(BackstageEntry.Command("Import PDF (text only)", BackstageIconKind.GetData, spec.ImportPdfText, iconName: "open"));

        if (spec.BuildSharePane is not null)
            entries.Add(BackstageEntry.Pane("Share", BackstageIconKind.Share, spec.BuildSharePane, iconName: "share"));

        if (hasHomePane)
            entries.Add(BackstageEntry.Pane("Info", BackstageIconKind.Info, spec.BuildInfoPane, iconName: "info"));

        entries.Add(BackstageEntry.Divider());
        entries.Add(BackstageEntry.Command("Save", BackstageIconKind.Save, spec.Save, iconName: "save"));
        entries.Add(spec.BuildSaveAsPane is null
            ? BackstageEntry.Command("Save As", BackstageIconKind.Save, spec.SaveAs, iconName: "save-as")
            : BackstageEntry.Pane("Save As", BackstageIconKind.Save, spec.BuildSaveAsPane, iconName: "save-as"));

        if (spec.SaveCopy is not null)
            entries.Add(BackstageEntry.Command("Save a Copy", BackstageIconKind.Save, spec.SaveCopy, iconName: "save-copy"));

        if (spec.BuildPrintPane is not null)
            entries.Add(BackstageEntry.Pane("Print", BackstageIconKind.Print, spec.BuildPrintPane, iconName: "print"));
        else if (spec.Print is not null)
            entries.Add(BackstageEntry.Command("Print", BackstageIconKind.Print, spec.Print, iconName: "print"));

        if (spec.BuildExportPane is not null)
            entries.Add(BackstageEntry.Pane("Export", BackstageIconKind.Share, spec.BuildExportPane, iconName: "export"));

        if (!spec.HideRecentPane)
            entries.Add(BackstageEntry.Pane("Recent", BackstageIconKind.GetData, spec.BuildRecentPane, iconName: "recent"));
        if (!spec.UseNewPane)
            entries.Add(BackstageEntry.Pane("New from template", BackstageIconKind.Grid, spec.BuildNewPane, iconName: "new"));
        if (spec.Close is not null)
            entries.Add(BackstageEntry.Command("Close", BackstageIconKind.Previous, spec.Close, iconName: "close"));
        if (spec.BuildAccountPane is not null)
            entries.Add(BackstageEntry.Pane("Account", BackstageIconKind.Info, spec.BuildAccountPane, dockBottom: true, iconName: "account"));
        entries.Add(BackstageEntry.Pane("Options", BackstageIconKind.View, spec.BuildOptionsPane, dockBottom: true, iconName: "options"));
        if (spec.Close is null)
            entries.Add(BackstageEntry.Command("Close", BackstageIconKind.Previous, static () => { }, dockBottom: true, iconName: "close"));

        return entries;
    }
}
