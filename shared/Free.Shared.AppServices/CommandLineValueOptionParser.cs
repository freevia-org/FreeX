namespace Free.Shared.AppServices;

public sealed record CommandLineValueOptionSpec(
    string Key,
    string Name,
    string MissingValueMessage,
    string BlankValueMessage,
    string DuplicateMessage,
    bool AllowEqualsSyntax = false);

public sealed record CommandLineValueOptionParseResult(
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlySet<string> PresentKeys,
    string[] RemainingArguments,
    string? Error)
{
    public bool IsPresent(string key) => PresentKeys.Contains(key);

    public string? Value(string key) =>
        Values.TryGetValue(key, out var value) ? value : null;
}

public readonly record struct CommandLineValueOption(bool IsPresent, string? Value);

/// <summary>
/// Extracts named command-line options that require values while preserving all unrelated arguments.
/// </summary>
public static class CommandLineValueOptionParser
{
    public static CommandLineValueOptionParseResult Parse(
        IReadOnlyList<string> args,
        IReadOnlyList<CommandLineValueOptionSpec> specs,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(specs);

        var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
        if (specs.Any(spec => !uniqueKeys.Add(spec.Key)))
            throw new ArgumentException("Command-line option keys must be unique.", nameof(specs));

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var remaining = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var match = FindMatch(args[index], specs, comparison);
            if (match is null)
            {
                remaining.Add(args[index]);
                continue;
            }

            var (spec, inlineValue) = match.Value;
            if (!present.Add(spec.Key))
                return Result(values, present, remaining, spec.DuplicateMessage);

            string? value;
            if (inlineValue is not null)
            {
                value = inlineValue;
            }
            else
            {
                if (index + 1 >= args.Count)
                    return Result(values, present, remaining, spec.MissingValueMessage);
                value = args[++index];
            }

            if (string.IsNullOrWhiteSpace(value))
                return Result(values, present, remaining, spec.BlankValueMessage);

            values[spec.Key] = value;
        }

        return Result(values, present, remaining, null);
    }

    public static CommandLineValueOption ReadFirst(
        IReadOnlyList<string> args,
        string name,
        StringComparison comparison = StringComparison.Ordinal,
        bool allowEqualsSyntax = false)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], name, comparison))
                continue;

            var value = index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1])
                ? args[index + 1]
                : null;
            return new CommandLineValueOption(true, value);
        }

        if (allowEqualsSyntax)
        {
            var prefix = name + "=";
            foreach (var argument in args)
            {
                if (!argument.StartsWith(prefix, comparison))
                    continue;

                var value = argument[prefix.Length..];
                return new CommandLineValueOption(
                    true,
                    string.IsNullOrWhiteSpace(value) ? null : value);
            }
        }

        return new CommandLineValueOption(false, null);
    }

    private static (CommandLineValueOptionSpec Spec, string? InlineValue)? FindMatch(
        string argument,
        IReadOnlyList<CommandLineValueOptionSpec> specs,
        StringComparison comparison)
    {
        foreach (var spec in specs)
        {
            if (string.Equals(argument, spec.Name, comparison))
                return (spec, null);

            if (!spec.AllowEqualsSyntax)
                continue;

            var prefix = spec.Name + "=";
            if (argument.StartsWith(prefix, comparison))
                return (spec, argument[prefix.Length..]);
        }

        return null;
    }

    private static CommandLineValueOptionParseResult Result(
        IReadOnlyDictionary<string, string?> values,
        IReadOnlySet<string> present,
        IEnumerable<string> remaining,
        string? error) =>
        new(values, present, remaining.ToArray(), error);
}
