using Free.Shared.IO;
using SharedFileDialogFilterBuilder = Free.Shared.IO.FileDialogFilterBuilder;

namespace FreeX.Core.IO;

public static class FileFormatResolver
{
    public static IFileAdapter? FindOpenAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format) =>
        FileFormatAdapterResolver.Find(
            adapters,
            static adapter => adapter.Formats,
            extension,
            static candidate => candidate.CanOpen,
            out format);

    public static IFileAdapter? FindSaveAdapter(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        out FileFormatDescriptor? format) =>
        FileFormatAdapterResolver.Find(
            adapters,
            static adapter => adapter.Formats,
            extension,
            static candidate => candidate.CanSave,
            out format);

    /// <summary>
    /// Resolves the adapter whose descriptor matches both <paramref name="extension"/> and
    /// <paramref name="formatName"/>. Needed when several adapters share one extension but differ by
    /// Save-As type (e.g. plain ".csv" vs "CSV UTF-8" ".csv"), where the extension-only resolver returns
    /// the first registered adapter. Falls back to <see cref="FindSaveAdapter"/> when no name matches.
    /// </summary>
    public static IFileAdapter? FindSaveAdapterByFormatName(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        string formatName,
        out FileFormatDescriptor? format)
    {
        var materialized = adapters as IReadOnlyCollection<IFileAdapter> ?? adapters.ToList();
        var byName = FileFormatAdapterResolver.Find(
            materialized,
            static adapter => adapter.Formats,
            extension,
            candidate => candidate.CanSave &&
                string.Equals(candidate.FormatName, formatName, StringComparison.OrdinalIgnoreCase),
            out format);
        return byName ?? FindSaveAdapter(materialized, extension, out format);
    }

    /// <summary>Open-side counterpart of <see cref="FindSaveAdapterByFormatName"/>.</summary>
    public static IFileAdapter? FindOpenAdapterByFormatName(
        IEnumerable<IFileAdapter> adapters,
        string extension,
        string formatName,
        out FileFormatDescriptor? format)
    {
        var materialized = adapters as IReadOnlyCollection<IFileAdapter> ?? adapters.ToList();
        var byName = FileFormatAdapterResolver.Find(
            materialized,
            static adapter => adapter.Formats,
            extension,
            candidate => candidate.CanOpen &&
                string.Equals(candidate.FormatName, formatName, StringComparison.OrdinalIgnoreCase),
            out format);
        return byName ?? FindOpenAdapter(materialized, extension, out format);
    }

    public static string SafeFileTypeFromExtension(string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length <= 1)
            return "unknown";

        var token = normalizedExtension[1..];
        return token.All(char.IsLetterOrDigit)
            ? token.ToLowerInvariant()
            : "unknown";
    }

    public static string NormalizeExtension(string extension) =>
        SharedFileDialogFilterBuilder.NormalizeExtension(extension);
}
