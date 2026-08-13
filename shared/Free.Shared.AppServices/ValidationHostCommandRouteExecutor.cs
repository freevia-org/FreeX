namespace Free.Shared.AppServices;

public delegate ValidationHostCommandRouteResult ValidationHostCommandRoute(
    IReadOnlyList<string> arguments);

public delegate bool ValidationHostImmediateCommand(
    IReadOnlyList<string> arguments,
    TextWriter output,
    TextWriter error,
    out int exitCode);

public delegate bool ValidationHostCommandParser<TOptions>(
    IReadOnlyList<string> arguments,
    out TOptions? options,
    out string[] remainingArguments,
    out string? error)
    where TOptions : class;

public sealed class ValidationHostCommandRouteResult
{
    private ValidationHostCommandRouteResult(
        ValidationHostCommandRouteStatus status,
        IReadOnlyList<string>? remainingArguments,
        int exitCode,
        string? error)
    {
        Status = status;
        RemainingArguments = remainingArguments;
        ExitCode = exitCode;
        Error = error;
    }

    internal ValidationHostCommandRouteStatus Status { get; }

    internal IReadOnlyList<string>? RemainingArguments { get; }

    internal int ExitCode { get; }

    internal string? Error { get; }

    public static ValidationHostCommandRouteResult NotMatched(
        IReadOnlyList<string> remainingArguments)
    {
        ArgumentNullException.ThrowIfNull(remainingArguments);
        return new(
            ValidationHostCommandRouteStatus.NotMatched,
            remainingArguments,
            exitCode: 0,
            error: null);
    }

    public static ValidationHostCommandRouteResult Handled(int exitCode) =>
        new(
            ValidationHostCommandRouteStatus.Handled,
            remainingArguments: null,
            exitCode,
            error: null);

    public static ValidationHostCommandRouteResult Invalid(string? error) =>
        new(
            ValidationHostCommandRouteStatus.Invalid,
            remainingArguments: null,
            exitCode: ValidationHostCommandRouteExecutor.UsageErrorExitCode,
            error);
}

internal enum ValidationHostCommandRouteStatus
{
    NotMatched,
    Handled,
    Invalid
}

/// <summary>
/// Runs ordered validation-host command routes while sharing parse-error and unmatched-command
/// reporting. Individual commands retain ownership of parsing, coordination, and exit codes.
/// </summary>
public static class ValidationHostCommandRouteExecutor
{
    public const int UsageErrorExitCode = 2;

    public static ValidationHostCommandRoute Immediate(
        ValidationHostImmediateCommand command,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        return arguments => command(arguments, output, error, out var exitCode)
            ? ValidationHostCommandRouteResult.Handled(exitCode)
            : ValidationHostCommandRouteResult.NotMatched(arguments);
    }

    public static ValidationHostCommandRoute Parsed<TOptions>(
        ValidationHostCommandParser<TOptions> parser,
        Func<TOptions, IReadOnlyList<string>, int> run)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(run);

        return arguments =>
        {
            if (!parser(arguments, out var options, out var remainingArguments, out var error))
                return ValidationHostCommandRouteResult.Invalid(error);

            return options is null
                ? ValidationHostCommandRouteResult.NotMatched(remainingArguments)
                : ValidationHostCommandRouteResult.Handled(run(options, remainingArguments));
        };
    }

    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter error,
        string unmatchedCommandError,
        params ValidationHostCommandRoute[] routes)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(unmatchedCommandError);
        ArgumentNullException.ThrowIfNull(routes);

        var remainingArguments = arguments;
        foreach (var route in routes)
        {
            ArgumentNullException.ThrowIfNull(route);
            var result = route(remainingArguments)
                ?? throw new InvalidOperationException("Validation host command route returned no result.");

            switch (result.Status)
            {
                case ValidationHostCommandRouteStatus.NotMatched:
                    remainingArguments = result.RemainingArguments
                        ?? throw new InvalidOperationException(
                            "An unmatched validation host command route returned no remaining arguments.");
                    break;
                case ValidationHostCommandRouteStatus.Handled:
                    return result.ExitCode;
                case ValidationHostCommandRouteStatus.Invalid:
                    error.WriteLine(result.Error);
                    return UsageErrorExitCode;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported validation host route status '{result.Status}'.");
            }
        }

        error.WriteLine(unmatchedCommandError);
        return UsageErrorExitCode;
    }
}
