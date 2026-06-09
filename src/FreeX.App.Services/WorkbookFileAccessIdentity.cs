using System.Text.Json.Serialization;

namespace FreeX.App.Services;

public sealed record WorkbookFileAccessIdentity
{
    public WorkbookFileAccessIdentity(
        string localPath,
        string? bookmarkKind = null,
        string? bookmarkPayload = null)
    {
        LocalPath = NormalizeLocalPath(localPath);
        BookmarkKind = NormalizeOptional(bookmarkKind);
        BookmarkPayload = NormalizeOptional(bookmarkPayload);
    }

    public string LocalPath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BookmarkKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BookmarkPayload { get; init; }

    [JsonIgnore]
    public bool HasBookmark =>
        !string.IsNullOrWhiteSpace(BookmarkKind) &&
        !string.IsNullOrWhiteSpace(BookmarkPayload);

    public static WorkbookFileAccessIdentity FromLocalPath(string localPath)
    {
        if (TryFromLocalPath(localPath, out var identity))
            return identity!;

        throw new ArgumentException("Workbook file access identity requires a local file path.", nameof(localPath));
    }

    public static bool TryFromLocalPath(string? localPath, out WorkbookFileAccessIdentity? identity)
    {
        identity = null;
        if (!LocalFilePath.TryNormalize(localPath, out var normalizedPath))
            return false;

        identity = new WorkbookFileAccessIdentity(normalizedPath);
        return true;
    }

    public WorkbookFileAccessIdentity WithLocalPath(string localPath) =>
        new(localPath, BookmarkKind, BookmarkPayload);

    public bool TryWithLocalPath(string localPath, out WorkbookFileAccessIdentity? identity)
    {
        identity = null;
        if (!TryFromLocalPath(localPath, out var normalizedIdentity) ||
            normalizedIdentity is null)
            return false;

        identity = new WorkbookFileAccessIdentity(
            normalizedIdentity.LocalPath,
            BookmarkKind,
            BookmarkPayload);
        return true;
    }

    private static string NormalizeLocalPath(string localPath)
    {
        if (!LocalFilePath.TryNormalize(localPath, out var normalizedPath))
            throw new ArgumentException("Workbook file access identity requires a local file path.", nameof(localPath));

        return normalizedPath;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
