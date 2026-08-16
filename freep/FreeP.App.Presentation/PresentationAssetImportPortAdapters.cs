namespace FreeP.App.Compositor;

/// <summary>
/// Adapts a native picker callback to the renderer-neutral asset import contract.
/// </summary>
public sealed class PresentationAssetPickerAdapter : IPresentationAssetPickerPort
{
    private readonly Func<PresentationAssetImportRequest, CancellationToken,
        Task<PresentationAssetPickerResult>> _pickAsync;

    public PresentationAssetPickerAdapter(
        Func<PresentationAssetImportRequest, CancellationToken,
            Task<PresentationAssetPickerResult>> pickAsync) =>
        _pickAsync = pickAsync ?? throw new ArgumentNullException(nameof(pickAsync));

    public Task<PresentationAssetPickerResult> PickAsync(
        PresentationAssetImportRequest request,
        CancellationToken cancellationToken) =>
        _pickAsync(request, cancellationToken);
}

/// <summary>
/// Performs shared source-type validation and lifetime handling around a native asset read.
/// </summary>
public sealed class PresentationAssetReaderAdapter<TSource> : IPresentationAssetReaderPort
    where TSource : notnull
{
    private readonly Func<TSource, CancellationToken, Task<byte[]>> _readAsync;
    private readonly Action<TSource>? _release;
    private readonly Func<object, string> _invalidSourceMessage;

    public PresentationAssetReaderAdapter(
        Func<TSource, CancellationToken, Task<byte[]>> readAsync,
        Action<TSource>? release = null,
        Func<object, string>? invalidSourceMessage = null)
    {
        _readAsync = readAsync ?? throw new ArgumentNullException(nameof(readAsync));
        _release = release;
        _invalidSourceMessage = invalidSourceMessage ??
            (source => $"Expected an asset source of type {typeof(TSource).Name}, but received {source?.GetType().Name ?? "<null>"}.");
    }

    public async Task<byte[]> ReadAsync(
        PresentationAssetSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Source is not TSource source)
            throw new InvalidOperationException(_invalidSourceMessage(selection.Source));

        try
        {
            return await _readAsync(source, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _release?.Invoke(source);
        }
    }
}
