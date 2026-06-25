using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Editing;

/// <summary>
/// A selectable chart type for the "Change Chart Type" picker: the <see cref="ChartType"/> and the
/// English label a shell shows for it.
/// </summary>
public sealed record ChartTypeChoice(ChartType Type, string DisplayName);

/// <summary>
/// A "Change Chart Type" category for the left-hand category list (Column, Line, Pie, …): the
/// localization key for the category name (so each shell resolves it through its own UiText) and the
/// authorable subtype choices that belong to it, in catalog order. Mirrors the host
/// <c>ChartTypePickerCategory</c> grouping so each shell's category-list + subtype-gallery dialog
/// matches the "Change Chart Type" layout.
/// </summary>
public sealed record ChartTypeCategory(string NameKey, IReadOnlyList<ChartTypeChoice> Choices);

/// <summary>
/// The outcome of validating a requested "Change Chart Type": either an applicable
/// <see cref="ChartType"/> (when the type differs from the chart's current type and the renderer
/// supports authoring it) or an English reason the change was rejected / is a no-op.
/// </summary>
public readonly record struct ChartTypeChangePlan(ChartType? AppliedType, string? Message)
{
    /// <summary>True when a real type change should be dispatched to <see cref="ChangeChartTypeCommand"/>.</summary>
    public bool HasChange => AppliedType is not null;
}

/// <summary>
/// Portable (no UI) planner for the "Change Chart Type" editing dialog. Single-sources the list of
/// authorable chart types (the families the renderer can paint — <see cref="ChartTypeSupport.IsAuthorable"/>)
/// and their English labels, and validates a requested type change before the shell dispatches the Core
/// <see cref="ChangeChartTypeCommand"/>. Reused across every shell so the supported set and the
/// validation rules live in one place. Core still re-validates inside the command (data-range fit, pivot
/// guard); this planner just keeps the picker honest and avoids a pointless command for an unchanged type.
/// </summary>
public static class ChartTypeChangePlanner
{
    // English display labels for every renderer-authorable family, in a stable gallery-style order.
    private static readonly (ChartType Type, string DisplayName)[] Catalog =
    [
        (ChartType.Column, "Clustered Column"),
        (ChartType.StackedColumn, "Stacked Column"),
        (ChartType.PercentStackedColumn, "100% Stacked Column"),
        (ChartType.ThreeDColumn, "3-D Column"),
        (ChartType.Bar, "Clustered Bar"),
        (ChartType.StackedBar, "Stacked Bar"),
        (ChartType.PercentStackedBar, "100% Stacked Bar"),
        (ChartType.ThreeDBar, "3-D Bar"),
        (ChartType.Line, "Line"),
        (ChartType.ThreeDLine, "3-D Line"),
        (ChartType.Area, "Area"),
        (ChartType.ThreeDArea, "3-D Area"),
        (ChartType.Scatter, "Scatter"),
        (ChartType.Bubble, "Bubble"),
        (ChartType.Pie, "Pie"),
        (ChartType.ThreeDPie, "3-D Pie"),
        (ChartType.Doughnut, "Doughnut"),
        (ChartType.Radar, "Radar"),
        (ChartType.Stock, "Stock"),
        (ChartType.Surface, "Surface"),
        (ChartType.ThreeDSurface, "3-D Surface"),
        (ChartType.Treemap, "Treemap"),
        (ChartType.Sunburst, "Sunburst"),
        (ChartType.Histogram, "Histogram"),
        (ChartType.Pareto, "Pareto"),
        (ChartType.BoxAndWhisker, "Box & Whisker"),
        (ChartType.Waterfall, "Waterfall"),
        (ChartType.Funnel, "Funnel"),
    ];

    /// <summary>
    /// The authorable chart types the picker should offer, in catalog order. Filters out families the
    /// renderer recognizes for XLSX preservation but cannot author/convert to
    /// (<see cref="ChartTypeSupport.IsAuthorable"/>).
    /// </summary>
    public static IReadOnlyList<ChartTypeChoice> GetSupportedChoices()
    {
        var choices = new List<ChartTypeChoice>(Catalog.Length);
        foreach (var (type, displayName) in Catalog)
        {
            if (ChartTypeSupport.IsAuthorable(type))
                choices.Add(new ChartTypeChoice(type, displayName));
        }

        return choices;
    }

    // The WPF "All Charts" category grouping (ChartTypePickerPlanner.GetCategories), keyed by the same
    // ChartTypeCategory_* / MainWindow_Content_* localization keys the Windows dialog uses so the
    // category-list headers read identically across shells. Each category lists the chart types that
    // belong to it; only the authorable ones survive into the returned choices.
    private static readonly (string NameKey, ChartType[] Types)[] CategoryCatalog =
    [
        ("ChartTypeCategory_Column", [ChartType.Column, ChartType.StackedColumn, ChartType.PercentStackedColumn, ChartType.ThreeDColumn]),
        ("ChartTypeCategory_Line", [ChartType.Line, ChartType.ThreeDLine]),
        ("ChartTypeCategory_Pie", [ChartType.Pie, ChartType.ThreeDPie, ChartType.Doughnut]),
        ("ChartTypeCategory_Bar", [ChartType.Bar, ChartType.StackedBar, ChartType.PercentStackedBar, ChartType.ThreeDBar]),
        ("ChartTypeCategory_Area", [ChartType.Area, ChartType.ThreeDArea]),
        ("ChartTypeCategory_Scatter", [ChartType.Scatter, ChartType.Bubble]),
        ("ChartTypeCategory_Stock", [ChartType.Stock]),
        ("ChartTypeCategory_Radar", [ChartType.Radar]),
        ("ChartTypeCategory_Surface", [ChartType.Surface, ChartType.ThreeDSurface]),
        ("MainWindow_Content_Treemap", [ChartType.Treemap]),
        ("MainWindow_Content_Sunburst", [ChartType.Sunburst]),
        ("MainWindow_Content_Histogram", [ChartType.Histogram, ChartType.Pareto]),
        ("MainWindow_TooltipTitle_BoxAndWhiskerChart", [ChartType.BoxAndWhisker]),
        ("MainWindow_Content_Waterfall", [ChartType.Waterfall]),
        ("MainWindow_Content_Funnel", [ChartType.Funnel]),
    ];

    /// <summary>
    /// The authorable chart types grouped into the WPF "All Charts" categories, in catalog order, for a
    /// category-list + subtype-gallery picker. Categories whose subtypes are all non-authorable are
    /// dropped, matching <see cref="GetSupportedChoices"/>. Each category carries a localization
    /// <see cref="ChartTypeCategory.NameKey"/> the shell resolves through its own UiText.
    /// </summary>
    public static IReadOnlyList<ChartTypeCategory> GetCategories()
    {
        var categories = new List<ChartTypeCategory>(CategoryCatalog.Length);
        foreach (var (nameKey, types) in CategoryCatalog)
        {
            var choices = new List<ChartTypeChoice>(types.Length);
            foreach (var type in types)
            {
                if (ChartTypeSupport.IsAuthorable(type))
                    choices.Add(new ChartTypeChoice(type, DisplayName(type)));
            }

            if (choices.Count > 0)
                categories.Add(new ChartTypeCategory(nameKey, choices));
        }

        return categories;
    }

    /// <summary>The English display label for <paramref name="type"/> (falls back to the enum name).</summary>
    public static string DisplayName(ChartType type)
    {
        foreach (var (catalogType, displayName) in Catalog)
        {
            if (catalogType == type)
                return displayName;
        }

        return type.ToString();
    }

    /// <summary>
    /// Validates a requested type change against the chart's current type. Returns a plan whose
    /// <see cref="ChartTypeChangePlan.HasChange"/> is true only when the type actually differs and is
    /// authorable; otherwise the plan carries an English message and no applied type.
    /// </summary>
    public static ChartTypeChangePlan Plan(ChartType currentType, ChartType requestedType)
    {
        if (!ChartTypeSupport.IsAuthorable(requestedType))
            return new ChartTypeChangePlan(null, ChartAuthoringPlanner.DeferredAuthoringMessage);

        if (requestedType == currentType)
            return new ChartTypeChangePlan(null, "The chart is already this type.");

        return new ChartTypeChangePlan(requestedType, null);
    }
}
