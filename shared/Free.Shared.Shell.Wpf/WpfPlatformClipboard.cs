using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf;

public sealed record WpfPlatformClipboardOptions(
    int MaxWriteAttempts = 1,
    TimeSpan? WriteRetryDelay = null,
    bool RetryAllWriteFailures = false,
    bool FlushAfterWrite = true,
    bool VerifyTextAfterWrite = false,
    bool VerifyImageAfterWrite = false,
    bool FallBackToText = false);

public sealed class WpfPlatformClipboard : IPlatformClipboard
{
    private readonly Dispatcher _dispatcher;
    private readonly WpfPlatformClipboardOptions _options;

    public WpfPlatformClipboard(
        Dispatcher? dispatcher = null,
        WpfPlatformClipboardOptions? options = null)
    {
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _options = options ?? new WpfPlatformClipboardOptions();
        if (_options.MaxWriteAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "At least one write attempt is required.");
    }

    public async ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
        PlatformClipboardReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await InvokeAsync(() => ReadDataObject(Clipboard.GetDataObject(), request));
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
            return await InvokeAsync(() => WriteCore(content, cancellationToken));
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
            return await InvokeAsync(() =>
            {
                Clipboard.Clear();
                return PlatformClipboardWriteResult.Success();
            });
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

    public string? TryGetChangeIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            return NativeMethods.GetClipboardSequenceNumber().ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static DataObject BuildDataObject(PlatformClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var data = new DataObject();

        if (content.Text is not null)
            data.SetText(content.Text);

        if (content.FilePaths.Count > 0)
        {
            var files = new StringCollection();
            files.AddRange(content.FilePaths.ToArray());
            data.SetFileDropList(files);
        }

        if (content.Image?.PngBytes is { Length: > 0 } pngBytes)
        {
            try
            {
                using var stream = new MemoryStream(pngBytes, writable: false);
                var bitmap = BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                data.SetImage(bitmap);
            }
            catch
            {
                // Other formats remain usable when the PNG cannot be decoded.
            }
        }

        foreach (var item in content.CustomData)
        {
            if (item.Format.Kind == PlatformClipboardDataKind.Text && item.Text is not null)
            {
                if (item.Format.Scope == PlatformClipboardFormatScope.Application
                    || IsWellKnownTextFormat(item.Format.Name))
                {
                    data.SetData(item.Format.Name, item.Text, autoConvert: false);
                }
                else
                {
                    var bytes = Encoding.Unicode.GetBytes(item.Text + '\0');
                    data.SetData(
                        item.Format.Name,
                        new MemoryStream(bytes, writable: false),
                        autoConvert: false);
                }
            }
            else if (item.Format.Kind == PlatformClipboardDataKind.Bytes
                     && item.Bytes is { Length: > 0 } bytes)
            {
                data.SetData(
                    item.Format.Name,
                    new MemoryStream(bytes, writable: false),
                    autoConvert: false);
            }
        }

        return data;
    }

    public static PlatformClipboardReadResult<PlatformClipboardContent> ReadDataObject(
        IDataObject? data,
        PlatformClipboardReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (data is null)
            return PlatformClipboardReadResult<PlatformClipboardContent>.Empty();

        try
        {
            string? text = null;
            if (request.IncludeText && data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true))
                text = data.GetData(DataFormats.UnicodeText, autoConvert: true) as string;

            IReadOnlyList<string> files = [];
            if (request.IncludeFiles && data.GetDataPresent(DataFormats.FileDrop, autoConvert: true))
                files = data.GetData(DataFormats.FileDrop, autoConvert: true) is string[] paths
                    ? paths
                    : [];

            PlatformClipboardImage? image = null;
            if (request.IncludeImage
                && data.GetDataPresent(DataFormats.Bitmap, autoConvert: true)
                && data.GetData(DataFormats.Bitmap, autoConvert: true) is BitmapSource bitmap)
            {
                image = new PlatformClipboardImage(
                    BitmapSourceToPng(bitmap),
                    bitmap.PixelWidth,
                    bitmap.PixelHeight);
            }

            var custom = new List<PlatformClipboardData>();
            foreach (var format in request.CustomFormats)
            {
                var item = format.Kind switch
                {
                    PlatformClipboardDataKind.Text => ReadTextFormat(data, format),
                    PlatformClipboardDataKind.Bytes => ReadBytesFormat(data, format),
                    _ => null,
                };
                if (item is not null)
                    custom.Add(item);
            }

            var content = new PlatformClipboardContent(text, files, image, custom);
            return content.IsEmpty
                ? PlatformClipboardReadResult<PlatformClipboardContent>.Empty()
                : PlatformClipboardReadResult<PlatformClipboardContent>.Success(content);
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

    private PlatformClipboardWriteResult WriteCore(
        PlatformClipboardContent content,
        CancellationToken cancellationToken)
    {
        var delay = _options.WriteRetryDelay ?? TimeSpan.Zero;
        Exception? lastError = null;
        for (var attempt = 0; attempt < _options.MaxWriteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var data = BuildDataObject(content);
                Clipboard.SetDataObject(data, copy: true);
                if (_options.FlushAfterWrite)
                    Clipboard.Flush();
                if (_options.VerifyTextAfterWrite
                    && content.Text is not null
                    && !string.Equals(Clipboard.GetText(), content.Text, StringComparison.Ordinal))
                {
                    throw new ExternalException("Clipboard text verification failed.");
                }
                if (_options.VerifyImageAfterWrite
                    && content.Image is not null
                    && Clipboard.GetImage() is null)
                {
                    throw new ExternalException("Clipboard image verification failed.");
                }
                return PlatformClipboardWriteResult.Success();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt))
            {
                lastError = ex;
                if (delay > TimeSpan.Zero)
                    Thread.Sleep(delay);
            }
            catch (NotSupportedException ex)
            {
                return PlatformClipboardWriteResult.Unsupported(ex.Message);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        if (_options.FallBackToText && content.Text is not null)
        {
            for (var attempt = 0; attempt < _options.MaxWriteAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Clipboard.SetText(content.Text);
                    if (_options.FlushAfterWrite)
                        Clipboard.Flush();
                    if (!_options.VerifyTextAfterWrite
                        || string.Equals(Clipboard.GetText(), content.Text, StringComparison.Ordinal))
                    {
                        return PlatformClipboardWriteResult.Success();
                    }
                    lastError = new ExternalException("Clipboard text verification failed.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ShouldRetry(ex, attempt))
                {
                    lastError = ex;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    break;
                }

                if (delay > TimeSpan.Zero && attempt + 1 < _options.MaxWriteAttempts)
                    Thread.Sleep(delay);
            }
        }

        return PlatformClipboardWriteResult.Failed(lastError?.Message);

        bool ShouldRetry(Exception exception, int attempt) =>
            attempt + 1 < _options.MaxWriteAttempts
            && (_options.RetryAllWriteFailures || exception is ExternalException);
    }

    private async ValueTask<T> InvokeAsync<T>(Func<T> action)
    {
        if (_dispatcher.CheckAccess())
            return action();
        return await _dispatcher.InvokeAsync(action);
    }

    private static PlatformClipboardData? ReadTextFormat(
        IDataObject data,
        PlatformClipboardFormat format)
    {
        if (!data.GetDataPresent(format.Name, autoConvert: false))
            return null;

        var value = data.GetData(format.Name, autoConvert: false);
        if (value is string text)
            return PlatformClipboardData.FromText(format.Name, text, format.Scope);

        var bytes = ReadBytes(value);
        if (bytes is not { Length: >= 2 })
            return null;
        var decoded = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        return PlatformClipboardData.FromText(format.Name, decoded, format.Scope);
    }

    private static PlatformClipboardData? ReadBytesFormat(
        IDataObject data,
        PlatformClipboardFormat format)
    {
        if (!data.GetDataPresent(format.Name, autoConvert: false))
            return null;
        var bytes = ReadBytes(data.GetData(format.Name, autoConvert: false));
        return bytes is null
            ? null
            : PlatformClipboardData.FromBytes(format.Name, bytes, format.Scope);
    }

    private static byte[]? ReadBytes(object? value)
    {
        try
        {
            return value switch
            {
                byte[] bytes when bytes.Length > 0 => bytes.ToArray(),
                MemoryStream stream when stream.Length > 0 => stream.ToArray(),
                Stream stream => ReadStream(stream),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ReadStream(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.Length == 0 ? null : copy.ToArray();
    }

    private static byte[] BitmapSourceToPng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static bool IsWellKnownTextFormat(string format) =>
        string.Equals(format, DataFormats.Html, StringComparison.Ordinal)
        || string.Equals(format, DataFormats.CommaSeparatedValue, StringComparison.Ordinal)
        || string.Equals(format, DataFormats.Rtf, StringComparison.Ordinal)
        || format.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();
    }
}
