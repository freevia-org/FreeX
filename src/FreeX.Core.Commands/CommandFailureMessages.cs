namespace FreeX.Core.Commands;

public static class CommandFailureMessages
{
    public const string SheetNotFound = "Sheet not found.";

    private static readonly string[] FailurePrefixes =
    [
        "Command failed: ",
        "Undo failed: ",
        "Redo failed: "
    ];

    public static string FormatExceptionFailure(string prefix, Exception exception) =>
        IsMissingSheetFailure(exception)
            ? SheetNotFound
            : $"{prefix}: {exception.Message}";

    public static string? NormalizeForPresentation(string? message)
    {
        if (message is null)
            return null;

        return IsMissingSheetFailureMessage(message)
            ? SheetNotFound
            : message;
    }

    public static bool IsMissingSheetFailure(Exception exception)
    {
        if (IsMissingSheetFailureMessage(exception.Message))
            return true;

        if (exception is AggregateException aggregate)
        {
            foreach (var innerException in aggregate.InnerExceptions)
            {
                if (IsMissingSheetFailure(innerException))
                    return true;
            }
        }

        return exception.InnerException is { } inner &&
               IsMissingSheetFailure(inner);
    }

    private static bool IsMissingSheetFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var trimmed = message.Trim();
        foreach (var prefix in FailurePrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal) &&
                IsRawMissingSheetMessage(trimmed[prefix.Length..]))
                return true;
        }

        return IsRawMissingSheetMessage(trimmed);
    }

    private static bool IsRawMissingSheetMessage(string message)
    {
        var trimmed = message.Trim();
        if (trimmed.EndsWith(".", StringComparison.Ordinal))
            trimmed = trimmed[..^1];

        const string prefix = "Sheet ";
        const string suffix = " not found";
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) ||
            !trimmed.EndsWith(suffix, StringComparison.Ordinal) ||
            trimmed.Length < prefix.Length + suffix.Length)
            return false;

        var sheetId = trimmed[prefix.Length..^suffix.Length];
        return sheetId.Length == 8 && sheetId.All(IsAsciiHexDigit);
    }

    private static bool IsAsciiHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
