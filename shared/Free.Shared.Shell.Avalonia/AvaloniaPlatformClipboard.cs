using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaPlatformClipboardOptions(
    bool FlushAfterWrite = true,
    bool FallBackToText = false);

public sealed class AvaloniaPlatformClipboard : IPlatformClipboard
{
    private readonly Func<IClipboard?> _getClipboard;
    private readonly Func<string, CancellationToken, ValueTask<IStorageItem?>>? _resolveFile;
    private readonly AvaloniaPlatformClipboardOptions _options;

    public AvaloniaPlatformClipboard(
        Func<IClipboard?> getClipboard,
        AvaloniaPlatformClipboardOptions? options = null,
        Func<string, CancellationToken, ValueTask<IStorageItem?>>? resolveFile = null)
    {
        _getClipboard = getClipboard ?? throw new ArgumentNullException(nameof(getClipboard));
        _options = options ?? new AvaloniaPlatformClipboardOptions();
        _resolveFile = resolveFile;
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return _getClipboard() is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    public async ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
        PlatformClipboardReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                return await ReadCoreAsync(request, cancellationToken);

            var operation = Dispatcher.UIThread.InvokeAsync(
                () => ReadCoreAsync(request, cancellationToken).AsTask());
            return await operation;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            return PlatformClipboardReadResult<PlatformClipboardContent>.Unsupported(ex.Message);
        }
        catch (Exception ex)
        {
            return PlatformClipboardReadResult<PlatformClipboardContent>.Failed(ex.Message);
        }
    }

    public async ValueTask<PlatformClipboardWriteResult> WriteAsync(
        PlatformClipboardContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                return await WriteCoreAsync(content, cancellationToken);

            var operation = Dispatcher.UIThread.InvokeAsync(
                () => WriteCoreAsync(content, cancellationToken).AsTask());
            return await operation;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            return PlatformClipboardWriteResult.Unsupported(ex.Message);
        }
        catch (Exception ex)
        {
            return PlatformClipboardWriteResult.Failed(ex.Message);
        }
    }

    public async ValueTask<PlatformClipboardWriteResult> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                return await ClearCoreAsync();

            var operation = Dispatcher.UIThread.InvokeAsync(() => ClearCoreAsync().AsTask());
            return await operation;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            return PlatformClipboardWriteResult.Unsupported(ex.Message);
        }
        catch (Exception ex)
        {
            return PlatformClipboardWriteResult.Failed(ex.Message);
        }
    }

    public static DataTransfer BuildDataTransfer(
        PlatformClipboardContent content,
        out Bitmap? bitmap)
    {
        ArgumentNullException.ThrowIfNull(content);
        var item = new DataTransferItem();
        if (content.Text is not null)
            item.SetText(content.Text);

        foreach (var custom in content.CustomData)
        {
            if (custom.Format.Kind == PlatformClipboardDataKind.Text && custom.Text is not null)
                item.Set(CreateStringFormat(custom.Format), custom.Text);
            else if (custom.Format.Kind == PlatformClipboardDataKind.Bytes
                     && custom.Bytes is { Length: > 0 } bytes)
                item.Set(CreateBytesFormat(custom.Format), bytes);
        }

        bitmap = null;
        if (content.Image?.PngBytes is { Length: > 0 } pngBytes)
        {
            try
            {
                bitmap = new Bitmap(new MemoryStream(pngBytes, writable: false));
                item.SetBitmap(bitmap);
            }
            catch
            {
                // Keep the remaining flavors if the PNG cannot be decoded.
            }
        }

        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    public static async ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadDataTransferAsync(
        IAsyncDataTransfer? transfer,
        PlatformClipboardReadRequest request,
        IReadOnlyList<string>? filePaths = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (transfer is null && (filePaths is null || filePaths.Count == 0))
            return PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        string? text = null;
        PlatformClipboardImage? image = null;
        var custom = new List<PlatformClipboardData>();
        Exception? firstError = null;

        if (transfer is not null)
        {
            if (request.IncludeText)
            {
                try
                {
                    text = await transfer.TryGetTextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    firstError ??= ex;
                }
            }

            if (request.IncludeImage)
            {
                try
                {
                    using var bitmap = await transfer.TryGetBitmapAsync();
                    if (bitmap is not null)
                    {
                        using var stream = new MemoryStream();
                        bitmap.Save(stream);
                        image = new PlatformClipboardImage(
                            stream.ToArray(),
                            bitmap.PixelSize.Width,
                            bitmap.PixelSize.Height);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    firstError ??= ex;
                }
            }

            foreach (var format in request.CustomFormats)
            {
                try
                {
                    if (format.Kind == PlatformClipboardDataKind.Text)
                    {
                        var value = await transfer.TryGetValueAsync(CreateStringFormat(format));
                        if (value is not null)
                            custom.Add(PlatformClipboardData.FromText(format.Name, value, format.Scope));
                    }
                    else
                    {
                        var value = await transfer.TryGetValueAsync(CreateBytesFormat(format));
                        if (value is not null)
                            custom.Add(PlatformClipboardData.FromBytes(format.Name, value, format.Scope));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    firstError ??= ex;
                }
            }
        }

        var content = new PlatformClipboardContent(text, filePaths, image, custom);
        if (!content.IsEmpty)
            return PlatformClipboardReadResult<PlatformClipboardContent>.Success(content);
        if (firstError is NotSupportedException)
            return PlatformClipboardReadResult<PlatformClipboardContent>.Unsupported(firstError.Message);
        if (firstError is not null)
            return PlatformClipboardReadResult<PlatformClipboardContent>.Failed(firstError.Message);
        return PlatformClipboardReadResult<PlatformClipboardContent>.Empty();
    }

    public static DataFormat<string> CreateStringFormat(PlatformClipboardFormat format) =>
        format.Scope == PlatformClipboardFormatScope.Application
            ? DataFormat.CreateStringApplicationFormat(format.Name)
            : DataFormat.CreateStringPlatformFormat(format.Name);

    public static DataFormat<byte[]> CreateBytesFormat(PlatformClipboardFormat format) =>
        format.Scope == PlatformClipboardFormatScope.Application
            ? DataFormat.CreateBytesApplicationFormat(format.Name)
            : DataFormat.CreateBytesPlatformFormat(format.Name);

    private async ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadCoreAsync(
        PlatformClipboardReadRequest request,
        CancellationToken cancellationToken)
    {
        var clipboard = _getClipboard();
        if (clipboard is null)
            return PlatformClipboardReadResult<PlatformClipboardContent>.Unavailable(
                "The current top level does not expose a clipboard.");

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IReadOnlyList<string> files = [];
            if (request.IncludeFiles)
            {
                var storageItems = await clipboard.TryGetFilesAsync();
                files = storageItems?
                    .Select(static item => item.Path.IsFile ? item.Path.LocalPath : item.Path.AbsoluteUri)
                    .ToArray() ?? [];
            }

            using var transfer = request.IncludeText || request.IncludeImage || request.CustomFormats.Count > 0
                ? await clipboard.TryGetDataAsync()
                : null;
            return await ReadDataTransferAsync(transfer, request, files);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            return PlatformClipboardReadResult<PlatformClipboardContent>.Unsupported(ex.Message);
        }
        catch (Exception ex)
        {
            return PlatformClipboardReadResult<PlatformClipboardContent>.Failed(ex.Message);
        }
    }

    private async ValueTask<PlatformClipboardWriteResult> WriteCoreAsync(
        PlatformClipboardContent content,
        CancellationToken cancellationToken)
    {
        var clipboard = _getClipboard();
        if (clipboard is null)
            return PlatformClipboardWriteResult.Unavailable(
                "The current top level does not expose a clipboard.");

        try
        {
            if (content.FilePaths.Count > 0)
            {
                if (_resolveFile is null)
                    return PlatformClipboardWriteResult.Unsupported(
                        "Writing file paths requires an Avalonia storage-item resolver.");
                if (content.Text is not null || content.Image is not null || content.CustomData.Count > 0)
                    return PlatformClipboardWriteResult.Unsupported(
                        "Avalonia cannot publish files and other clipboard flavors atomically.");

                var items = new List<IStorageItem>();
                foreach (var path in content.FilePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await _resolveFile(path, cancellationToken) is { } item)
                        items.Add(item);
                }
                if (items.Count == 0)
                    return PlatformClipboardWriteResult.Failed("No file paths could be resolved.");
                await clipboard.SetFilesAsync(items);
                return PlatformClipboardWriteResult.Success();
            }

            var transfer = BuildDataTransfer(content, out var bitmap);
            try
            {
                await clipboard.SetDataAsync(transfer);
            }
            catch (OperationCanceledException)
            {
                bitmap?.Dispose();
                ((IDisposable)transfer).Dispose();
                throw;
            }
            catch
            {
                bitmap?.Dispose();
                ((IDisposable)transfer).Dispose();
                if (!_options.FallBackToText || content.Text is null)
                    throw;
                await clipboard.SetTextAsync(content.Text);
                return PlatformClipboardWriteResult.Success();
            }

            if (_options.FlushAfterWrite)
            {
                try
                {
                    await clipboard.FlushAsync();
                }
                catch
                {
                    // Ownership has already transferred; flush is advisory.
                }
            }
            return PlatformClipboardWriteResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            return PlatformClipboardWriteResult.Unsupported(ex.Message);
        }
        catch (Exception ex)
        {
            return PlatformClipboardWriteResult.Failed(ex.Message);
        }
    }

    private async ValueTask<PlatformClipboardWriteResult> ClearCoreAsync()
    {
        var clipboard = _getClipboard();
        if (clipboard is null)
            return PlatformClipboardWriteResult.Unavailable(
                "The current top level does not expose a clipboard.");

        try
        {
            await clipboard.ClearAsync();
            return PlatformClipboardWriteResult.Success();
        }
        catch (NotSupportedException ex)
        {
            return PlatformClipboardWriteResult.Unsupported(ex.Message);
        }
        catch (Exception ex)
        {
            return PlatformClipboardWriteResult.Failed(ex.Message);
        }
    }
}
