using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.Localization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

public enum ManageConditionalFormatScope
{
    Sheet,
    Table,
    Selection
}

public enum ManageConditionalFormatsFocusTarget
{
    ScopeSelector,
    RulesList
}

public sealed record ManageConditionalFormatScopeOption(
    ManageConditionalFormatScope Scope,
    string LabelKey,
    GridRange? Range);

public sealed record ManageConditionalFormatsDialogPlan(
    IReadOnlyList<ManageConditionalFormatScopeOption> ScopeOptions,
    ManageConditionalFormatScope DefaultScope,
    GridRange DefaultNewRuleRange)
{
    public ManageConditionalFormatScopeOption DefaultScopeOption =>
        ScopeOptions.First(option => option.Scope == DefaultScope);
}

public sealed record ConditionalFormatAppliesToRangeSelectionRequest(
    Guid RuleId,
    string CurrentText,
    bool CollapseDialog = true);

public abstract record ManageConditionalFormatDescriptionArgument
{
    public static ManageConditionalFormatDescriptionArgument Literal(string? text) =>
        new LiteralDescriptionArgument(text ?? string.Empty);

    public static ManageConditionalFormatDescriptionArgument Resource(string resourceKey) =>
        new ResourceDescriptionArgument(resourceKey);

    public static ManageConditionalFormatDescriptionArgument ResourceList(IReadOnlyList<string> resourceKeys) =>
        new ResourceListDescriptionArgument(resourceKeys);
}

public sealed record LiteralDescriptionArgument(string Text) : ManageConditionalFormatDescriptionArgument;

public sealed record ResourceDescriptionArgument(string ResourceKey) : ManageConditionalFormatDescriptionArgument;

public sealed record ResourceListDescriptionArgument(
    IReadOnlyList<string> ResourceKeys,
    string SeparatorKey = ManageConditionalFormatsPlanner.ListSeparatorKey) : ManageConditionalFormatDescriptionArgument;

public sealed record ManageConditionalFormatRuleDescription(
    string? ResourceKey,
    IReadOnlyList<ManageConditionalFormatDescriptionArgument> Arguments,
    string? LiteralText = null)
{
    public static ManageConditionalFormatRuleDescription Resource(
        string resourceKey,
        params ManageConditionalFormatDescriptionArgument[] arguments) =>
        new(resourceKey, arguments);

    public static ManageConditionalFormatRuleDescription Literal(string text) =>
        new(null, [], text);
}

public sealed record ManageConditionalFormatPreviewFill(IReadOnlyList<PresentationRgb> Stops)
{
    public bool IsGradient => Stops.Count > 1;

    public static ManageConditionalFormatPreviewFill Solid(PresentationRgb color) =>
        new([color]);

    public static ManageConditionalFormatPreviewFill Gradient(params PresentationRgb[] stops) =>
        new(stops);
}

public sealed record ManageConditionalFormatPreviewPlan(
    ManageConditionalFormatPreviewFill Fill,
    PresentationRgb Foreground,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    string SampleTextKey = ManageConditionalFormatsPlanner.FormatPreviewSampleKey);

/// <summary>
/// Portable rule-list planner for the conditional-format manager. Shells own the dialog chrome and
/// range-picking UI; this planner owns the app-neutral edits to rule order, priority, identity, and
/// filtered-scope merge behavior so every shell can share one contract.
/// </summary>
public static class ManageConditionalFormatsPlanner
{
    public const string ScopeThisWorksheetKey = "ManageConditionalFormats_ScopeThisWorksheet";
    public const string ScopeThisTableKey = "ManageConditionalFormats_ScopeThisTable";
    public const string ScopeCurrentSelectionKey = "ManageConditionalFormats_ScopeCurrentSelection";
    public const string FormatPreviewSampleKey = "ManageConditionalFormats_FormatPreviewSample";
    public const string StopIfTrueEnabledKey = "ManageConditionalFormats_Yes";
    public const string ListSeparatorKey = "ManageConditionalFormats_ListSeparator";

    private static readonly PresentationRgb NeutralPreviewFill = new(211, 211, 211);
    private static readonly PresentationRgb Black = new(0, 0, 0);

    public static ManageConditionalFormatsFocusTarget InitialFocusTarget =>
        ManageConditionalFormatsFocusTarget.ScopeSelector;

    public static ManageConditionalFormatsFocusTarget MissingSelectionFocusTarget =>
        ManageConditionalFormatsFocusTarget.RulesList;

    public static ManageConditionalFormatsDialogPlan CreateDialogPlan(Sheet sheet, GridRange? selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var options = new List<ManageConditionalFormatScopeOption>
        {
            new(ManageConditionalFormatScope.Sheet, ScopeThisWorksheetKey, null)
        };

        if (FindSelectionTableRange(sheet, selection) is { } tableRange)
            options.Add(new ManageConditionalFormatScopeOption(ManageConditionalFormatScope.Table, ScopeThisTableKey, tableRange));

        if (selection is { } selectionRange)
            options.Add(new ManageConditionalFormatScopeOption(ManageConditionalFormatScope.Selection, ScopeCurrentSelectionKey, selectionRange));

        return new ManageConditionalFormatsDialogPlan(
            options,
            selection.HasValue ? ManageConditionalFormatScope.Selection : ManageConditionalFormatScope.Sheet,
            DefaultNewRuleRange(sheet, selection));
    }

    public static GridRange DefaultNewRuleRange(Sheet sheet, GridRange? selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (selection is { } selectionRange)
            return selectionRange;

        foreach (var rule in sheet.ConditionalFormats)
            return rule.AppliesTo;

        return new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
    }

    public static GridRange? FindSelectionTableRange(Sheet sheet, GridRange? selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (selection is not { } selectionRange)
            return null;

        return StructuredTableSelectionPlanner.FindOverlappingTableRange(sheet, selectionRange);
    }

    public static ConditionalFormatAppliesToRangeSelectionRequest CreateAppliesToRangeSelectionRequest(
        Guid ruleId,
        string? currentText) =>
        new(ruleId, NormalizeAppliesToText(currentText), CollapseDialog: true);

    public static string FormatAppliesToRange(GridRange range)
    {
        var startColumn = CellAddress.NumberToColumnName(range.Start.Col);
        var endColumn = CellAddress.NumberToColumnName(range.End.Col);
        return $"${startColumn}${range.Start.Row}:${endColumn}${range.End.Row}";
    }

    public static string NormalizeAppliesToText(string? text) =>
        (text ?? string.Empty).Trim();

    public static GridRange ParseAppliesToTextOrFallback(string? text, SheetId sheetId, GridRange fallback) =>
        TryParseAppliesToText(text, sheetId, out var parsed)
            ? parsed
            : fallback;

    public static bool TryParseAppliesToText(string? text, SheetId sheetId, out GridRange range)
    {
        range = default;
        var normalized = NormalizeAppliesToText(text).Replace("$", "", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (!normalized.Contains(':', StringComparison.Ordinal))
            normalized = $"{normalized}:{normalized}";

        try
        {
            range = GridRange.Parse(normalized, sheetId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static ManageConditionalFormatRuleDescription DescribeRule(ConditionalFormat cf)
    {
        ArgumentNullException.ThrowIfNull(cf);

        return cf.RuleType switch
        {
            CfRuleType.Formula => ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleFormula",
                ManageConditionalFormatDescriptionArgument.Literal(cf.FormulaText)),
            CfRuleType.DataBar => ManageConditionalFormatRuleDescription.Resource(
                cf.DataBarShowValue
                    ? "ManageConditionalFormats_RuleDataBar"
                    : "ManageConditionalFormats_RuleDataBarOnly"),
            CfRuleType.ColorScale => ManageConditionalFormatRuleDescription.Resource(
                cf.UseThreeColorScale
                    ? "ManageConditionalFormats_RuleThreeColorScale"
                    : "ManageConditionalFormats_RuleTwoColorScale"),
            CfRuleType.IconSet => BuildIconSetDescription(cf),
            CfRuleType.ContainsText => ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleTextContains",
                ManageConditionalFormatDescriptionArgument.Literal(cf.TextRuleText)),
            CfRuleType.NotContainsText => ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleTextDoesNotContain",
                ManageConditionalFormatDescriptionArgument.Literal(cf.TextRuleText)),
            CfRuleType.BeginsWith => ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleTextBeginsWith",
                ManageConditionalFormatDescriptionArgument.Literal(cf.TextRuleText)),
            CfRuleType.EndsWith => ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleTextEndsWith",
                ManageConditionalFormatDescriptionArgument.Literal(cf.TextRuleText)),
            CfRuleType.DateOccurring => ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleDateOccurring",
                ManageConditionalFormatDescriptionArgument.Resource(DatePeriodLabelKey(cf.DateOccurringPeriod))),
            CfRuleType.DuplicateValues => ManageConditionalFormatRuleDescription.Resource("ManageConditionalFormats_RuleDuplicateValues"),
            CfRuleType.UniqueValues => ManageConditionalFormatRuleDescription.Resource("ManageConditionalFormats_RuleUniqueValues"),
            CfRuleType.AboveAverage => ManageConditionalFormatRuleDescription.Resource(
                cf.AboveAverage
                    ? "ManageConditionalFormats_RuleAboveAverage"
                    : "ManageConditionalFormats_RuleBelowAverage"),
            CfRuleType.Top10 => ManageConditionalFormatRuleDescription.Resource(
                cf.AboveAverage
                    ? "ManageConditionalFormats_RuleTopRank"
                    : "ManageConditionalFormats_RuleBottomRank",
                ManageConditionalFormatDescriptionArgument.Literal(cf.TopBottomRank.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ManageConditionalFormatDescriptionArgument.Literal(cf.TopBottomPercent ? "%" : string.Empty)),
            CfRuleType.CellValue => BuildCellValueDescription(cf),
            _ => ManageConditionalFormatRuleDescription.Literal(cf.RuleType.ToString())
        };
    }

    public static string ResolveDescription(
        ManageConditionalFormatRuleDescription description,
        ResourceKeyTextResolver text)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(text);

        if (description.ResourceKey is null)
            return description.LiteralText ?? string.Empty;

        if (description.Arguments.Count == 0)
            return text.Get(description.ResourceKey);

        var arguments = description.Arguments
            .Select(argument => ResolveDescriptionArgument(argument, text))
            .Cast<object?>()
            .ToArray();
        return text.Format(description.ResourceKey, arguments);
    }

    public static ManageConditionalFormatPreviewPlan CreatePreviewPlan(ConditionalFormat cf)
    {
        ArgumentNullException.ThrowIfNull(cf);

        var style = cf.FormatIfTrue;
        return new ManageConditionalFormatPreviewPlan(
            PreviewFill(cf),
            style?.FontColor is { } fontColor ? PresentationRgb.FromCellColor(fontColor) : Black,
            style?.Bold == true,
            style?.Italic == true,
            style?.Underline == true,
            style?.Strikethrough == true);
    }

    public static string? StopIfTrueTextKey(ConditionalFormat cf)
    {
        ArgumentNullException.ThrowIfNull(cf);
        return cf.StopIfTrue ? StopIfTrueEnabledKey : null;
    }

    public static IReadOnlyList<ConditionalFormat> BuildResultRules(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? selection,
        bool filterToSelection,
        IReadOnlyList<ConditionalFormat> editedRules)
    {
        if (!filterToSelection || selection is null)
            return Reprioritize(editedRules);

        var result = new List<ConditionalFormat>();
        var matchingRuleCount = sheetRules.Count(rule => RuleOverlapsSelection(rule, selection.Value));
        var editedRuleIndex = 0;

        foreach (var rule in sheetRules)
        {
            if (!RuleOverlapsSelection(rule, selection.Value))
            {
                result.Add(rule);
                continue;
            }

            matchingRuleCount--;

            if (editedRuleIndex < editedRules.Count)
                result.Add(editedRules[editedRuleIndex++]);

            if (matchingRuleCount == 0)
            {
                while (editedRuleIndex < editedRules.Count)
                    result.Add(editedRules[editedRuleIndex++]);
            }
        }

        while (editedRuleIndex < editedRules.Count)
            result.Add(editedRules[editedRuleIndex++]);

        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> DuplicateRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        Guid? newId = null)
    {
        var result = Reprioritize(rules).ToList();
        var index = FindRuleIndex(result, ruleId);
        if (index < 0)
            return result;

        result.Insert(index + 1, CloneWithPriority(result[index], index + 2, newId ?? Guid.NewGuid()));
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> AddRule(
        IReadOnlyList<ConditionalFormat> rules,
        ConditionalFormat newRule)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(newRule);

        var result = Reprioritize(rules).ToList();
        result.Add(CloneWithPriority(newRule, result.Count + 1));
        return result;
    }

    public static IReadOnlyList<ConditionalFormat> ReplaceRule(
        IReadOnlyList<ConditionalFormat> rules,
        ConditionalFormat editedRule)
    {
        var result = Reprioritize(rules).ToList();
        var index = FindRuleIndex(result, editedRule.Id);
        if (index < 0)
            return result;

        result[index] = CloneWithPriority(editedRule, index + 1);
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> DeleteRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId)
    {
        return Reprioritize(rules.Where(rule => rule.Id != ruleId).ToList());
    }

    public static IReadOnlyList<ConditionalFormat> MoveRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction) =>
        MoveRule(rules, scope: null, ruleId, direction);

    public static IReadOnlyList<ConditionalFormat> MoveRule(
        IReadOnlyList<ConditionalFormat> rules,
        GridRange? scope,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction)
    {
        var result = Reprioritize(rules).ToList();
        var visible = scope is not { } range
            ? result
            : result.Where(rule => RuleOverlapsSelection(rule, range)).ToList();
        var visibleIndex = FindRuleIndex(visible, ruleId);
        if (visibleIndex < 0)
            return result;

        var visibleTarget = direction == ConditionalFormatRuleMoveDirection.Up
            ? visibleIndex - 1
            : visibleIndex + 1;
        if (visibleTarget < 0 || visibleTarget >= visible.Count)
            return result;

        var index = FindRuleIndex(result, ruleId);
        var target = FindRuleIndex(result, visible[visibleTarget].Id);
        (result[index], result[target]) = (result[target], result[index]);
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> ApplyRuleRange(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        GridRange range)
    {
        var result = Reprioritize(rules).ToList();
        var index = FindRuleIndex(result, ruleId);
        if (index < 0)
            return result;

        var updated = CloneWithPriority(result[index], index + 1);
        updated.AppliesTo = range;
        updated.AdditionalRanges = null;
        result[index] = updated;
        return result;
    }

    public static IReadOnlyList<ConditionalFormat> Reprioritize(IReadOnlyList<ConditionalFormat> rules) =>
        rules.Select((rule, index) => CloneWithPriority(rule, index + 1)).ToList();

    public static ConditionalFormat CloneWithPriority(ConditionalFormat src, int priority, Guid? id = null)
    {
        var cf = src.Clone(id);
        cf.Priority = priority;
        return cf;
    }

    public static bool RangesOverlap(GridRange a, GridRange b)
    {
        if (a.Start.Sheet != b.Start.Sheet)
            return false;

        return a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row
            && a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
    }

    /// <summary>
    /// Matches the Manage-Rules dialog's display predicate (which filters on <see cref="ConditionalFormat.AllRanges"/>),
    /// so the edited-rule merge in <see cref="BuildResultRules"/> aligns 1:1 with the rules actually shown to the user.
    /// </summary>
    private static bool RuleOverlapsSelection(ConditionalFormat rule, GridRange selection) =>
        rule.AllRanges.Any(range => RangesOverlap(range, selection));

    private static int FindRuleIndex(IReadOnlyList<ConditionalFormat> rules, Guid ruleId)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].Id == ruleId)
                return i;
        }

        return -1;
    }

    private static ManageConditionalFormatRuleDescription BuildIconSetDescription(ConditionalFormat cf)
    {
        var style = string.IsNullOrWhiteSpace(cf.IconSetStyle) ? "3TrafficLights1" : cf.IconSetStyle;
        var flags = new List<string>();
        if (cf.IconSetReverse)
            flags.Add("ManageConditionalFormats_IconFlagReverse");
        if (!cf.IconSetShowValue)
            flags.Add("ManageConditionalFormats_IconFlagIconsOnly");
        if (cf.IconOverrides.Count > 0)
            flags.Add("ManageConditionalFormats_IconFlagCustomIcons");

        return flags.Count == 0
            ? ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleIconSet",
                ManageConditionalFormatDescriptionArgument.Literal(style))
            : ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleIconSetWithFlags",
                ManageConditionalFormatDescriptionArgument.Literal(style),
                ManageConditionalFormatDescriptionArgument.ResourceList(flags));
    }

    private static string ResolveDescriptionArgument(
        ManageConditionalFormatDescriptionArgument argument,
        ResourceKeyTextResolver text) =>
        argument switch
        {
            LiteralDescriptionArgument literal => literal.Text,
            ResourceDescriptionArgument resource => text.Get(resource.ResourceKey),
            ResourceListDescriptionArgument resourceList => string.Join(
                text.Get(resourceList.SeparatorKey),
                resourceList.ResourceKeys.Select(text.Get)),
            _ => string.Empty
        };

    private static string DatePeriodLabelKey(string? value) => value switch
    {
        "yesterday" => "ManageConditionalFormats_DateYesterday",
        "today" => "ManageConditionalFormats_DateToday",
        "tomorrow" => "ManageConditionalFormats_DateTomorrow",
        "last7Days" => "ManageConditionalFormats_DateLast7Days",
        "lastWeek" => "ManageConditionalFormats_DateLastWeek",
        "thisWeek" => "ManageConditionalFormats_DateThisWeek",
        "nextWeek" => "ManageConditionalFormats_DateNextWeek",
        "lastMonth" => "ManageConditionalFormats_DateLastMonth",
        "thisMonth" => "ManageConditionalFormats_DateThisMonth",
        "nextMonth" => "ManageConditionalFormats_DateNextMonth",
        _ => "ManageConditionalFormats_DateToday"
    };

    private static ManageConditionalFormatRuleDescription BuildCellValueDescription(ConditionalFormat cf)
    {
        var op = cf.Operator switch
        {
            CfOperator.Between => ManageConditionalFormatDescriptionArgument.Resource("ManageConditionalFormats_OperatorBetween"),
            CfOperator.NotBetween => ManageConditionalFormatDescriptionArgument.Resource("ManageConditionalFormats_OperatorNotBetween"),
            CfOperator.GreaterThan => ManageConditionalFormatDescriptionArgument.Literal(">"),
            CfOperator.LessThan => ManageConditionalFormatDescriptionArgument.Literal("<"),
            CfOperator.Equal => ManageConditionalFormatDescriptionArgument.Literal("="),
            CfOperator.NotEqual => ManageConditionalFormatDescriptionArgument.Literal("<>"),
            CfOperator.GreaterThanOrEqual => ManageConditionalFormatDescriptionArgument.Literal(">="),
            CfOperator.LessThanOrEqual => ManageConditionalFormatDescriptionArgument.Literal("<="),
            _ => ManageConditionalFormatDescriptionArgument.Literal("?")
        };

        if (cf.Operator is CfOperator.Between or CfOperator.NotBetween)
        {
            return ManageConditionalFormatRuleDescription.Resource(
                "ManageConditionalFormats_RuleCellValueBetween",
                op,
                ManageConditionalFormatDescriptionArgument.Literal(cf.Value1),
                ManageConditionalFormatDescriptionArgument.Literal(cf.Value2));
        }

        return ManageConditionalFormatRuleDescription.Resource(
            "ManageConditionalFormats_RuleCellValue",
            op,
            ManageConditionalFormatDescriptionArgument.Literal(cf.Value1));
    }

    private static ManageConditionalFormatPreviewFill PreviewFill(ConditionalFormat cf)
    {
        if (cf.RuleType == CfRuleType.IconSet)
            return ManageConditionalFormatPreviewFill.Solid(NeutralPreviewFill);

        if (cf.RuleType == CfRuleType.DataBar)
            return ManageConditionalFormatPreviewFill.Solid(PresentationRgb.FromRgbColor(cf.DataBarColor));

        if (cf.RuleType == CfRuleType.ColorScale)
        {
            return cf.UseThreeColorScale
                ? ManageConditionalFormatPreviewFill.Gradient(
                    PresentationRgb.FromRgbColor(cf.MinColor),
                    PresentationRgb.FromRgbColor(cf.MidColor),
                    PresentationRgb.FromRgbColor(cf.MaxColor))
                : ManageConditionalFormatPreviewFill.Gradient(
                    PresentationRgb.FromRgbColor(cf.MinColor),
                    PresentationRgb.FromRgbColor(cf.MaxColor));
        }

        if (cf.FormatIfTrue?.FillColor is { } fillColor)
            return ManageConditionalFormatPreviewFill.Solid(PresentationRgb.FromCellColor(fillColor));

        return ManageConditionalFormatPreviewFill.Solid(NeutralPreviewFill);
    }
}
