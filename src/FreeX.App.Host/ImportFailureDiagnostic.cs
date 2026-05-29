using System.IO;

namespace FreeX.App.Host;

internal readonly record struct ImportFailureDiagnostic(
    string Reason,
    string UserMessage,
    string? Detail);

internal static class ImportFailureDiagnosticFactory
{
    public static ImportFailureDiagnostic FromException(string extension, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsXsltTransformFailure(extension, exception))
        {
            return new ImportFailureDiagnostic(
                "xslt_transform_failed",
                $"Failed to import XML data after applying the XSLT transform:\n{exception.Message}",
                exception.Message);
        }

        return new ImportFailureDiagnostic(
            exception.GetType().Name,
            $"Failed to import data:\n{exception.Message}",
            null);
    }

    private static bool IsXsltTransformFailure(string extension, Exception exception) =>
        string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) &&
        exception is InvalidDataException &&
        ExceptionChainContainsXslt(exception);

    private static bool ExceptionChainContainsXslt(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("XSLT", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
