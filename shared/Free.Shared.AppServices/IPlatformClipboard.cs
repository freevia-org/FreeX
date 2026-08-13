namespace Free.Shared.AppServices;

public enum PlatformClipboardReadStatus
{
    Success,
    Unavailable,
    Empty,
    Unsupported,
    Failed,
}

public enum PlatformClipboardWriteStatus
{
    Success,
    Unavailable,
    Unsupported,
    Failed,
}

public enum PlatformClipboardDataKind
{
    Text,
    Bytes,
}

public enum PlatformClipboardFormatScope
{
    Platform,
    Application,
}

public sealed record PlatformClipboardFormat(
    string Name,
    PlatformClipboardDataKind Kind,
    PlatformClipboardFormatScope Scope = PlatformClipboardFormatScope.Platform)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException("Clipboard format names cannot be empty.", nameof(Name))
        : Name;
}

public sealed record PlatformClipboardData
{
    private PlatformClipboardData(
        PlatformClipboardFormat format,
        string? text,
        byte[]? bytes)
    {
        Format = format ?? throw new ArgumentNullException(nameof(format));
        Text = text;
        Bytes = bytes?.ToArray();
    }

    public PlatformClipboardFormat Format { get; }

    public string? Text { get; }

    public byte[]? Bytes { get; }

    public static PlatformClipboardData FromText(
        string format,
        string? text,
        PlatformClipboardFormatScope scope = PlatformClipboardFormatScope.Platform) =>
        new(new PlatformClipboardFormat(format, PlatformClipboardDataKind.Text, scope), text, null);

    public static PlatformClipboardData FromBytes(
        string format,
        byte[]? bytes,
        PlatformClipboardFormatScope scope = PlatformClipboardFormatScope.Platform) =>
        new(new PlatformClipboardFormat(format, PlatformClipboardDataKind.Bytes, scope), null, bytes);
}

public sealed record PlatformClipboardImage(
    byte[] PngBytes,
    int? PixelWidth = null,
    int? PixelHeight = null)
{
    public byte[] PngBytes { get; } = PngBytes?.ToArray()
        ?? throw new ArgumentNullException(nameof(PngBytes));
}

public sealed record PlatformClipboardContent(
    string? Text = null,
    IReadOnlyList<string>? FilePaths = null,
    PlatformClipboardImage? Image = null,
    IReadOnlyList<PlatformClipboardData>? CustomData = null)
{
    public IReadOnlyList<string> FilePaths { get; } = FilePaths?.ToArray() ?? [];

    public IReadOnlyList<PlatformClipboardData> CustomData { get; } = CustomData?.ToArray() ?? [];

    public bool IsEmpty =>
        Text is null
        && FilePaths.Count == 0
        && Image is null
        && CustomData.All(static item => item.Text is null && item.Bytes is null);

    public string? GetText(string format, PlatformClipboardFormatScope? scope = null) =>
        CustomData.FirstOrDefault(item =>
            item.Format.Kind == PlatformClipboardDataKind.Text
            && string.Equals(item.Format.Name, format, StringComparison.Ordinal)
            && (!scope.HasValue || item.Format.Scope == scope.Value))?.Text;

    public byte[]? GetBytes(string format, PlatformClipboardFormatScope? scope = null) =>
        CustomData.FirstOrDefault(item =>
            item.Format.Kind == PlatformClipboardDataKind.Bytes
            && string.Equals(item.Format.Name, format, StringComparison.Ordinal)
            && (!scope.HasValue || item.Format.Scope == scope.Value))?.Bytes;
}

public sealed record PlatformClipboardReadRequest(
    bool IncludeText = false,
    bool IncludeFiles = false,
    bool IncludeImage = false,
    IReadOnlyList<PlatformClipboardFormat>? CustomFormats = null)
{
    public IReadOnlyList<PlatformClipboardFormat> CustomFormats { get; } =
        CustomFormats?.ToArray() ?? [];

    public static PlatformClipboardReadRequest Text { get; } = new(IncludeText: true);

    public static PlatformClipboardReadRequest Image { get; } = new(IncludeImage: true);

    public static PlatformClipboardReadRequest Files { get; } = new(IncludeFiles: true);
}

public readonly record struct PlatformClipboardReadResult<T>(
    PlatformClipboardReadStatus Status,
    T? Value = default,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == PlatformClipboardReadStatus.Success;

    public static PlatformClipboardReadResult<T> Success(T value) =>
        new(PlatformClipboardReadStatus.Success, value);

    public static PlatformClipboardReadResult<T> Unavailable(string? message = null) =>
        new(PlatformClipboardReadStatus.Unavailable, default, message);

    public static PlatformClipboardReadResult<T> Empty() =>
        new(PlatformClipboardReadStatus.Empty);

    public static PlatformClipboardReadResult<T> Unsupported(string? message = null) =>
        new(PlatformClipboardReadStatus.Unsupported, default, message);

    public static PlatformClipboardReadResult<T> Failed(string? message = null) =>
        new(PlatformClipboardReadStatus.Failed, default, message);
}

public readonly record struct PlatformClipboardWriteResult(
    PlatformClipboardWriteStatus Status,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == PlatformClipboardWriteStatus.Success;

    public static PlatformClipboardWriteResult Success() =>
        new(PlatformClipboardWriteStatus.Success);

    public static PlatformClipboardWriteResult Unavailable(string? message = null) =>
        new(PlatformClipboardWriteStatus.Unavailable, message);

    public static PlatformClipboardWriteResult Unsupported(string? message = null) =>
        new(PlatformClipboardWriteStatus.Unsupported, message);

    public static PlatformClipboardWriteResult Failed(string? message = null) =>
        new(PlatformClipboardWriteStatus.Failed, message);
}

public interface IPlatformClipboard
{
    bool IsAvailable => true;

    ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
        PlatformClipboardReadRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<PlatformClipboardWriteResult> WriteAsync(
        PlatformClipboardContent content,
        CancellationToken cancellationToken = default);

    ValueTask<PlatformClipboardWriteResult> ClearAsync(
        CancellationToken cancellationToken = default);

    string? TryGetChangeIdentity() => null;
}

public static class PlatformClipboardExtensions
{
    public static async ValueTask<PlatformClipboardReadResult<string>> ReadTextAsync(
        this IPlatformClipboard clipboard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        var result = await clipboard.ReadAsync(PlatformClipboardReadRequest.Text, cancellationToken);
        return Project(result, static content => content.Text);
    }

    public static async ValueTask<PlatformClipboardReadResult<PlatformClipboardImage>> ReadImageAsync(
        this IPlatformClipboard clipboard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        var result = await clipboard.ReadAsync(PlatformClipboardReadRequest.Image, cancellationToken);
        return Project(result, static content => content.Image);
    }

    public static async ValueTask<PlatformClipboardReadResult<IReadOnlyList<string>>> ReadFilesAsync(
        this IPlatformClipboard clipboard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        var result = await clipboard.ReadAsync(PlatformClipboardReadRequest.Files, cancellationToken);
        return Project(result, static content => content.FilePaths.Count == 0 ? null : content.FilePaths);
    }

    public static async ValueTask<PlatformClipboardReadResult<PlatformClipboardData>> ReadCustomAsync(
        this IPlatformClipboard clipboard,
        PlatformClipboardFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(format);
        var result = await clipboard.ReadAsync(
            new PlatformClipboardReadRequest(CustomFormats: [format]),
            cancellationToken);
        return Project(
            result,
            content => content.CustomData.FirstOrDefault(item => item.Format == format));
    }

    private static PlatformClipboardReadResult<TValue> Project<TValue>(
        PlatformClipboardReadResult<PlatformClipboardContent> result,
        Func<PlatformClipboardContent, TValue?> selector)
        where TValue : class
    {
        if (result.Status != PlatformClipboardReadStatus.Success || result.Value is null)
            return new PlatformClipboardReadResult<TValue>(result.Status, default, result.ErrorMessage);

        var value = selector(result.Value);
        return value is null
            ? PlatformClipboardReadResult<TValue>.Empty()
            : PlatformClipboardReadResult<TValue>.Success(value);
    }
}
