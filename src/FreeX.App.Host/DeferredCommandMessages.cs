using FreeX.App.Services;
using FreeX.App.Presentation.Shell;
using FreeX.Core.IO;

namespace FreeX.App.Host;

public static class DeferredCommandMessages
{
    public static DeferredCommandMessage WorkbookTheme(string commandName) =>
        Resolve(DeferredCommandMessagePlanner.WorkbookTheme(commandName));

    public static DeferredCommandMessage MultiWindow(string commandName) =>
        Resolve(DeferredCommandMessagePlanner.MultiWindow(commandName));

    public static DeferredCommandMessage OnlineTemplatesExcluded() =>
        Resolve(DeferredCommandMessagePlanner.OnlineTemplatesExcluded());

    public static DeferredCommandMessage LocalAccountInfo(LocalAccountPlan? plan = null) =>
        Resolve(
            DeferredCommandMessagePlanner.LocalAccountInfo(),
            body => plan is null ? body : LocalAccountWorkflowPlanner.FormatMessageBody(plan, body));

    public static DeferredCommandMessage PivotTableModelFirst() =>
        Resolve(DeferredCommandMessagePlanner.PivotTableModelFirst());

    public static DeferredCommandMessage AutoCorrectOptions() =>
        Resolve(DeferredCommandMessagePlanner.AutoCorrectOptions());

    public static DeferredCommandMessage EditingLanguages() =>
        Resolve(DeferredCommandMessagePlanner.EditingLanguages());

    public static DeferredCommandMessage RibbonCustomizationImportExport() =>
        Resolve(DeferredCommandMessagePlanner.RibbonCustomizationImportExport());

    public static DeferredCommandMessage OfficeAddIns() =>
        Resolve(DeferredCommandMessagePlanner.OfficeAddIns());

    public static DeferredCommandMessage TrustCenterSettings() =>
        Resolve(DeferredCommandMessagePlanner.TrustCenterSettings());

    public static DeferredCommandMessage UnsupportedXlsxFeatureSaveWarning(XlsxFeatureReport report)
        => Resolve(DeferredCommandMessagePlanner.UnsupportedXlsxFeatureSaveWarning(report));

    public static DeferredCommandMessage UnsupportedXlsxFeatureOpenWarning(XlsxFeatureReport report)
        => Resolve(DeferredCommandMessagePlanner.UnsupportedXlsxFeatureOpenWarning(report));

    public static string FormatUnsupportedXlsxFeatureKind(XlsxUnsupportedFeatureKind kind) =>
        ResolveText(DeferredCommandMessagePlanner.UnsupportedXlsxFeatureKindText(kind));

    private static DeferredCommandMessage Resolve(
        DeferredCommandMessagePlan plan,
        Func<string, string>? bodyProjector = null)
    {
        var body = ResolveText(plan.Body);
        return new DeferredCommandMessage(
            ResolveText(plan.Title),
            bodyProjector?.Invoke(body) ?? body);
    }

    private static string ResolveText(DeferredCommandMessageTextPlan plan)
    {
        if (plan.ResourceKey is null)
            return plan.LiteralText ?? string.Empty;

        if (plan.Arguments.Count == 0)
            return UiText.Get(plan.ResourceKey);

        return UiText.Format(
            plan.ResourceKey,
            plan.Arguments.Select(ResolveArgument).ToArray());
    }

    private static object? ResolveArgument(DeferredCommandMessageArgument argument)
    {
        if (argument.ResourceKey is not null)
            return UiText.Get(argument.ResourceKey);

        if (argument.TextItems.Count > 0)
        {
            var values = argument.TextItems
                .Select(ResolveText)
                .Distinct(StringComparer.Ordinal);

            if (argument.SortResolvedText)
                values = values.OrderBy(value => value, StringComparer.Ordinal);

            return string.Join(", ", values);
        }

        return argument.LiteralText ?? string.Empty;
    }
}

public sealed record DeferredCommandMessage(string Title, string Body);
