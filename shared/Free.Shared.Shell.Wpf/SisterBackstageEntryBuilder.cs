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

        var plans = SisterBackstageEntryPlanner.Build(new SisterBackstageEntryPlanSpec<UIElement>(
            spec.BuildInfoPane,
            spec.New,
            spec.Open,
            spec.Save,
            spec.SaveAs,
            spec.BuildRecentPane,
            spec.BuildNewPane,
            spec.BuildOptionsPane)
        {
            Print = spec.Print,
            SaveCopy = spec.SaveCopy,
            Close = spec.Close,
            BuildHomePane = spec.BuildHomePane,
            UseNewPane = spec.UseNewPane,
            BuildOpenPane = spec.BuildOpenPane,
            ImportPdfText = spec.ImportPdfText,
            BuildSharePane = spec.BuildSharePane,
            BuildSaveAsPane = spec.BuildSaveAsPane,
            BuildPrintPane = spec.BuildPrintPane,
            BuildExportPane = spec.BuildExportPane,
            BuildAccountPane = spec.BuildAccountPane,
            HideRecentPane = spec.HideRecentPane,
        });

        return plans.Select(ToWpfEntry).ToArray();
    }

    private static BackstageEntry ToWpfEntry(SisterBackstageEntryPlan<UIElement> plan) =>
        plan.Kind switch
        {
            SisterBackstageEntryKind.Pane => BackstageEntry.Pane(
                plan.Label,
                plan.Icon,
                plan.ContentFactory ?? throw new InvalidOperationException($"Pane '{plan.Label}' has no content factory."),
                plan.DockBottom,
                iconName: plan.IconCommandName),
            SisterBackstageEntryKind.Command => BackstageEntry.Command(
                plan.Label,
                plan.Icon,
                plan.Action ?? throw new InvalidOperationException($"Command '{plan.Label}' has no action."),
                plan.DockBottom,
                iconName: plan.IconCommandName),
            SisterBackstageEntryKind.Divider => BackstageEntry.Divider(plan.DockBottom),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan.Kind, null),
        };
}
