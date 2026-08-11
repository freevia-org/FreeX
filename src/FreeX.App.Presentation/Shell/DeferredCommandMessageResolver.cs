using Free.Shared.Localization;

namespace FreeX.App.Presentation.Shell;

public sealed record DeferredCommandMessage(string Title, string Body);

public static class DeferredCommandMessageResolver
{
    public static DeferredCommandMessage Resolve(
        DeferredCommandMessagePlan plan,
        ResourceKeyTextResolver text,
        Func<string, string>? bodyProjector = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(text);

        var body = ResolveText(plan.Body, text);
        return new DeferredCommandMessage(
            ResolveText(plan.Title, text),
            bodyProjector?.Invoke(body) ?? body);
    }

    public static string ResolveText(
        DeferredCommandMessageTextPlan plan,
        ResourceKeyTextResolver text)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(text);

        if (plan.ResourceKey is null)
            return plan.LiteralText ?? string.Empty;

        return plan.Arguments.Count == 0
            ? text.Get(plan.ResourceKey)
            : text.Format(
                plan.ResourceKey,
                plan.Arguments.Select(argument => ResolveArgument(argument, text)).ToArray());
    }

    private static object? ResolveArgument(
        DeferredCommandMessageArgument argument,
        ResourceKeyTextResolver text)
    {
        if (argument.ResourceKey is not null)
            return text.Get(argument.ResourceKey);

        if (argument.TextItems.Count > 0)
        {
            var values = argument.TextItems
                .Select(item => ResolveText(item, text))
                .Distinct(StringComparer.Ordinal);

            if (argument.SortResolvedText)
                values = values.OrderBy(value => value, StringComparer.Ordinal);

            return string.Join(", ", values);
        }

        return argument.LiteralText ?? string.Empty;
    }
}
