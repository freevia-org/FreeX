using Free.Shared.Ribbon;
using Free.Shared.Ribbon.KeyTips;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Presentation.Ribbon;

public enum FreeXRibbonKeyTipRouteKind
{
    RibbonTab,
    Backstage,
    BackstagePane,
    BackstageCommand,
    QuickAccessToolbar,
    RibbonCommand,
    Scope,
}

public sealed record FreeXRibbonKeyTipRoute(
    string Input,
    string RouteName,
    FreeXRibbonKeyTipRouteKind Kind,
    string? TabKeyTip = null,
    RibbonCommandId? CommandId = null,
    FreeXBackstagePaneId? BackstagePane = null,
    FreeXBackstageCommandId? BackstageCommand = null,
    int QuickAccessIndex = -1);

public readonly record struct FreeXRibbonKeyTipMatch(
    FreeXRibbonKeyTipRoute? ExactRoute,
    bool HasLongerRoute)
{
    public bool IsMatch => ExactRoute is not null || HasLongerRoute;
}

public sealed class FreeXRibbonKeyTipRouteCatalog
{
    private readonly IReadOnlyList<FreeXRibbonKeyTipRoute> _routes;

    internal FreeXRibbonKeyTipRouteCatalog(IReadOnlyList<FreeXRibbonKeyTipRoute> routes) =>
        _routes = routes;

    public IReadOnlyList<FreeXRibbonKeyTipRoute> Routes => _routes;

    public FreeXRibbonKeyTipMatch Match(string? input)
    {
        var normalized = RibbonKeyTipText.Normalize(input);
        if (normalized is null)
            return default;

        FreeXRibbonKeyTipRoute? exact = null;
        var hasLonger = false;
        foreach (var route in _routes)
        {
            if (string.Equals(route.Input, normalized, StringComparison.OrdinalIgnoreCase))
                exact ??= route;
            else if (route.Input.Length > normalized.Length &&
                     route.Input.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                hasLonger = true;
        }

        return new FreeXRibbonKeyTipMatch(exact, hasLonger);
    }

    public bool TryResolveExact(string? input, out FreeXRibbonKeyTipRoute route)
    {
        route = Match(input).ExactRoute!;
        return route is not null;
    }
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
    RibbonTab,
}

/// <summary>Owns FreeX key-tip path identity without owning either renderer's controls.</summary>
public static class FreeXRibbonKeyTipRoutePlanner
{
    public static FreeXRibbonKeyTipRouteCatalog Build(
        RibbonDefinition definition,
        int initialQuickAccessRouteCount = 3)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegative(initialQuickAccessRouteCount);

        var routes = new Dictionary<string, FreeXRibbonKeyTipRoute>(StringComparer.OrdinalIgnoreCase);

        void Add(FreeXRibbonKeyTipRoute route) => routes.TryAdd(route.Input, route);

        Add(new("F", "backstage", FreeXRibbonKeyTipRouteKind.Backstage));
        foreach (var entry in FreeXBackstageNavigationPlanner.Build())
        {
            if (string.IsNullOrWhiteSpace(entry.KeyTip))
                continue;

            var input = "F" + RibbonKeyTipText.NormalizeOrEmpty(entry.KeyTip);
            if (entry.Pane is { } pane)
            {
                Add(new(
                    input,
                    $"backstage:{entry.KeyTip}",
                    FreeXRibbonKeyTipRouteKind.BackstagePane,
                    BackstagePane: pane));
            }
            else if (entry.Command is { } command)
            {
                Add(new(
                    input,
                    $"backstage:{entry.KeyTip}",
                    FreeXRibbonKeyTipRouteKind.BackstageCommand,
                    BackstageCommand: command));
            }
        }

        // The current rail does not expose this historical File-surface scope.
        Add(new("FZ", "backstage:Z", FreeXRibbonKeyTipRouteKind.Scope));

        for (var index = 0; index < initialQuickAccessRouteCount; index++)
        {
            var keyTip = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Add(new(
                keyTip,
                $"qat:{keyTip}",
                FreeXRibbonKeyTipRouteKind.QuickAccessToolbar,
                QuickAccessIndex: index));
        }

        foreach (var tab in definition.Tabs)
        {
            var tabInput = RibbonKeyTipText.Normalize(tab.KeyTip);
            if (tabInput is null)
                continue;

            Add(new(
                tabInput,
                $"tab:{tab.Id}",
                FreeXRibbonKeyTipRouteKind.RibbonTab,
                TabKeyTip: tab.KeyTip));

            foreach (var control in tab.Groups.SelectMany(group => group.Controls))
            {
                var controlKeyTip = RibbonKeyTipText.Normalize(control.KeyTip);
                if (controlKeyTip is null)
                    continue;

                var controlInput = tabInput + controlKeyTip;
                var menu = control switch
                {
                    RibbonSplitButton split => split.Menu,
                    RibbonDropdown dropdown => dropdown.Menu,
                    _ => null,
                };
                Add(new(
                    controlInput,
                    menu is null ? $"command:{control.CommandId.Value}" : $"scope:{control.CommandId.Value}",
                    menu is null ? FreeXRibbonKeyTipRouteKind.RibbonCommand : FreeXRibbonKeyTipRouteKind.Scope,
                    CommandId: menu is null ? control.CommandId : (RibbonCommandId?)null));

                if (menu is not null)
                    AddMenuRoutes(routes, controlInput, menu.Items);
            }
        }

        // These dynamic native scopes are not controls in the declarative definition.
        Add(new("NCH", "group:InsertChartsGroup", FreeXRibbonKeyTipRouteKind.Scope));
        Add(new(
            "NSHR",
            "dynamic-menu:shape.rectangle",
            FreeXRibbonKeyTipRouteKind.RibbonCommand,
            CommandId: new RibbonCommandId(
                FreeXRibbonCommandIdentityCatalog.ShapeCommandId(DrawingShapeKind.Rectangle))));

        return new FreeXRibbonKeyTipRouteCatalog(
            routes.Values.OrderBy(route => route.Input, StringComparer.Ordinal).ToArray());
    }

    public static RibbonTopLevelKeyTipAction? ResolveTopLevel(
        string? keyTip,
        IEnumerable<RibbonTopLevelKeyTipEntry> entries)
    {
        var normalizedKeyTip = RibbonKeyTipText.Normalize(keyTip);
        if (normalizedKeyTip is null)
            return null;

        var candidates = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Header) &&
                            !string.IsNullOrWhiteSpace(entry.KeyTip))
            .ToArray();

        foreach (var entry in candidates)
        {
            if (string.Equals(
                    RibbonKeyTipText.Normalize(entry.KeyTip),
                    normalizedKeyTip,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CreateTopLevelAction(entry.Header);
            }
        }

        if (string.Equals(normalizedKeyTip, "D", StringComparison.OrdinalIgnoreCase))
        {
            var data = candidates.FirstOrDefault(entry =>
                string.Equals(entry.Header, "Data", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(data.Header))
                return RibbonTopLevelKeyTipAction.RibbonTab(data.Header);
        }

        return null;
    }

    public static bool HasLongerTopLevelKeyTipPrefix(
        string? keyTipPrefix,
        IEnumerable<string?> keyTips)
    {
        var normalizedPrefix = RibbonKeyTipText.Normalize(keyTipPrefix);
        if (normalizedPrefix is null)
            return false;

        return keyTips
            .Select(RibbonKeyTipText.Normalize)
            .Any(candidate =>
                candidate is not null &&
                candidate.Length > normalizedPrefix.Length &&
                candidate.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddMenuRoutes(
        IDictionary<string, FreeXRibbonKeyTipRoute> routes,
        string parentInput,
        IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            var itemKeyTip = RibbonKeyTipText.Normalize(item.KeyTip);
            if (itemKeyTip is null)
                continue;

            var input = parentInput + itemKeyTip;
            if (item.Children.Count > 0)
            {
                routes.TryAdd(input, new(
                    input,
                    $"scope:{item.Header}",
                    FreeXRibbonKeyTipRouteKind.Scope));
                AddMenuRoutes(routes, input, item.Children);
            }
            else if (item.CommandId is { } commandId)
            {
                routes.TryAdd(input, new(
                    input,
                    $"menu:{commandId.Value}",
                    FreeXRibbonKeyTipRouteKind.RibbonCommand,
                    CommandId: commandId));
            }
            else
            {
                routes.TryAdd(input, new(
                    input,
                    $"scope:{item.Header}",
                    FreeXRibbonKeyTipRouteKind.Scope));
            }
        }
    }

    private static RibbonTopLevelKeyTipAction CreateTopLevelAction(string header) =>
        string.Equals(header, "File", StringComparison.OrdinalIgnoreCase)
            ? RibbonTopLevelKeyTipAction.BackstageFile
            : RibbonTopLevelKeyTipAction.RibbonTab(header);
}
