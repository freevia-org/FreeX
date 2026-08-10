using PresentationRibbon = FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Host;

/// <summary>Native WPF facade over the renderer-neutral FreeX route planner.</summary>
public static class RibbonTopLevelKeyTipRouter
{
    public static RibbonTopLevelKeyTipAction? Resolve(
        string keyTip,
        IEnumerable<RibbonTopLevelKeyTipEntry> entries)
    {
        var action = PresentationRibbon.FreeXRibbonKeyTipRoutePlanner.ResolveTopLevel(
            keyTip,
            entries.Select(entry =>
                new PresentationRibbon.RibbonTopLevelKeyTipEntry(entry.Header, entry.KeyTip)));

        return action?.Kind switch
        {
            PresentationRibbon.RibbonTopLevelKeyTipActionKind.BackstageFile =>
                RibbonTopLevelKeyTipAction.BackstageFile,
            PresentationRibbon.RibbonTopLevelKeyTipActionKind.RibbonTab =>
                RibbonTopLevelKeyTipAction.RibbonTab(action.Value.RibbonTabHeader!),
            _ => null,
        };
    }

    public static bool HasLongerKeyTipPrefix(
        string keyTipPrefix,
        IEnumerable<string?> keyTips) =>
        PresentationRibbon.FreeXRibbonKeyTipRoutePlanner.HasLongerTopLevelKeyTipPrefix(
            keyTipPrefix,
            keyTips);
}

public readonly record struct RibbonTopLevelKeyTipEntry(string Header, string? KeyTip);

public readonly record struct RibbonTopLevelKeyTipAction(
    RibbonTopLevelKeyTipActionKind Kind,
    string? RibbonTabHeader)
{
    public static RibbonTopLevelKeyTipAction BackstageFile { get; } =
        new(RibbonTopLevelKeyTipActionKind.BackstageFile, null);

    public static RibbonTopLevelKeyTipAction RibbonTab(string header) =>
        new(RibbonTopLevelKeyTipActionKind.RibbonTab, header);
}

public enum RibbonTopLevelKeyTipActionKind
{
    BackstageFile,
    RibbonTab
}
