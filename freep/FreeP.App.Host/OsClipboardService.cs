using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Testable boundary around the WPF system clipboard.</summary>
public interface IOsClipboard
{
    bool ContainsImage();
    bool ContainsText();
    byte[]? GetImagePngBytes();
    string? GetText();
    void SetDataObject(DataObject data);
    long SequenceNumber { get; }

    PresentationClipboardContent Read() => new(
        PngBytes: ContainsImage() ? GetImagePngBytes() : null,
        Text: ContainsText() ? GetText() : null);

    void Write(PresentationClipboardContent content) =>
        SetDataObject(WpfOsClipboard.BuildDataObject(content));
}

/// <summary>
/// WPF system-clipboard adapter. The service above this boundary only sees the shared,
/// framework-neutral <see cref="PresentationClipboardContent"/> contract.
/// </summary>
public sealed class WpfOsClipboard : IOsClipboard
{
    internal const string SelectionFormat = PresentationClipboardFormats.Selection;
    internal const string OwnerTokenFormat = PresentationClipboardFormats.OwnerToken;
    internal const string RichTextFormat = PresentationClipboardFormats.RichText;
    internal const string WindowsXamlPackageFormat = PresentationClipboardFormats.WindowsXamlPackage;

    // Backward-read aliases for Avalonia application formats. This value is proven
    // against pinned Avalonia 12.0.4 tag a8dd6417fd8918570edefdbecd92d16ac7620069,
    // src/Windows/Avalonia.Win32/ClipboardFormatRegistry.cs. Current interop does not
    // depend on it: both hosts also publish the public platform names above.
    internal const string AvaloniaApplicationFormatPrefix = "avn-app-fmt:";
    internal const string LegacyAvaloniaSelectionFormat =
        AvaloniaApplicationFormatPrefix + PresentationClipboardFormats.Selection;
    internal const string LegacyAvaloniaOwnerTokenFormat =
        AvaloniaApplicationFormatPrefix + PresentationClipboardFormats.OwnerToken;

    public PresentationClipboardContent Read()
    {
        var data = Clipboard.GetDataObject();
        return ReadDataObject(data);
    }

    public void Write(PresentationClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Clipboard.SetDataObject(BuildDataObject(content), copy: true);
    }

    public bool ContainsImage()
    {
        try { return Clipboard.ContainsImage(); }
        catch { return false; }
    }

    public bool ContainsText()
    {
        try { return Clipboard.ContainsText(); }
        catch { return false; }
    }

    public byte[]? GetImagePngBytes()
    {
        try
        {
            var bitmap = Clipboard.GetImage();
            return bitmap is null ? null : BitmapSourceToPng(bitmap);
        }
        catch
        {
            return null;
        }
    }

    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    public void SetDataObject(DataObject data)
    {
        try { Clipboard.SetDataObject(data, copy: true); }
        catch { }
    }

    public long SequenceNumber
    {
        get
        {
            try { return NativeMethods.GetClipboardSequenceNumber(); }
            catch { return 0; }
        }
    }

    internal static DataObject BuildDataObject(PresentationClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var data = new DataObject();

        if (content.SelectionBytes is { Length: > 0 })
            SetRawBytes(data, SelectionFormat, content.SelectionBytes);

        if (content.RichTextBytes is { Length: > 0 })
            SetRawBytes(data, RichTextFormat, content.RichTextBytes);

        if (!string.IsNullOrEmpty(content.OwnerToken))
        {
            // Avalonia's Win32 clipboard backend serializes custom strings as a
            // null-terminated UTF-16 HGLOBAL.
            var bytes = Encoding.Unicode.GetBytes(content.OwnerToken + '\0');
            SetRawBytes(data, OwnerTokenFormat, bytes);
        }

        if (content.PngBytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(content.PngBytes, writable: false);
                var bitmap = BitmapFrame.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                bitmap.Freeze();
                data.SetImage(bitmap);
            }
            catch
            {
                // Native selection and text remain useful when image decoding fails.
            }
        }

        if (!string.IsNullOrEmpty(content.Text))
            data.SetText(content.Text);

        return data;
    }

    internal static PresentationClipboardContent ReadDataObject(IDataObject? data)
    {
        if (data is null)
            return new PresentationClipboardContent();

        var selection = TryReadBytes(data, SelectionFormat)
            ?? TryReadBytes(data, LegacyAvaloniaSelectionFormat);
        var richText = TryReadBytes(data, RichTextFormat);
        var xamlPackage = TryReadBytes(data, WindowsXamlPackageFormat);
        var rtf = TryReadBytes(data, DataFormats.Rtf);
        var ownerToken = TryReadCustomString(data, OwnerTokenFormat)
            ?? TryReadCustomString(data, LegacyAvaloniaOwnerTokenFormat);

        string? text = null;
        try
        {
            if (data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true))
                text = data.GetData(DataFormats.UnicodeText, autoConvert: true) as string;
        }
        catch
        {
        }

        byte[]? png = null;
        try
        {
            if (data.GetDataPresent(DataFormats.Bitmap, autoConvert: true)
                && data.GetData(DataFormats.Bitmap, autoConvert: true) is BitmapSource bitmap)
            {
                png = BitmapSourceToPng(bitmap);
            }
        }
        catch
        {
        }

        return new PresentationClipboardContent(selection, png, text, ownerToken, richText, xamlPackage, rtf);
    }

    private static void SetRawBytes(DataObject data, string format, byte[] bytes) =>
        data.SetData(format, new MemoryStream(bytes, writable: false), autoConvert: false);

    private static byte[]? TryReadBytes(IDataObject data, string format)
    {
        try
        {
            if (!data.GetDataPresent(format, autoConvert: false))
                return null;

            return data.GetData(format, autoConvert: false) switch
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

    private static string? TryReadCustomString(IDataObject data, string format)
    {
        try
        {
            if (!data.GetDataPresent(format, autoConvert: false))
                return null;

            var value = data.GetData(format, autoConvert: false);
            if (value is string text)
                return string.IsNullOrEmpty(text) ? null : text;

            var bytes = value switch
            {
                byte[] array => array,
                MemoryStream stream => stream.ToArray(),
                Stream stream => ReadStream(stream),
                _ => null,
            };
            if (bytes is not { Length: >= 2 })
                return null;

            var decoded = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            return string.IsNullOrEmpty(decoded) ? null : decoded;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ReadStream(Stream stream)
    {
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.Length == 0 ? null : copy.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BitmapSourceToPng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();
    }
}

public interface IShapeRenderer
{
    byte[] RenderShapesToPng(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes,
        int widthPx,
        int heightPx);
}

/// <summary>Coordinates shared FreeP clipboard content with the WPF IO boundary.</summary>
public sealed class OsClipboardService
{
    private readonly IOsClipboard _clipboard;
    private readonly IShapeRenderer _renderer;
    private readonly PresentationClipboardOwnershipTracker _ownership = new();

    internal bool OwnCopyIsCurrentOnOs =>
        _ownership.HasCurrentPlatformIdentity(CurrentSequenceIdentity());

    public int RenderWidthPx { get; set; } = 1280;
    public int RenderHeightPx { get; set; } = 720;

    public OsClipboardService(IOsClipboard clipboard, IShapeRenderer renderer)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>Exports the current selection without changing the editor's clipboard.</summary>
    public void PlaceSelectionOnOsClipboard(EditingSession editor) =>
        TryPlaceSelectionOnOsClipboard(editor);

    internal bool TryPlaceSelectionOnOsClipboard(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return TryWrite(PrepareWrite(editor));
    }

    internal PresentationClipboardWriteRequest PrepareWrite(EditingSession editor) =>
        PresentationClipboardWorkflow.PrepareWrite(
            editor,
            (presentation, slide, shapes) => _renderer.RenderShapesToPng(
                presentation,
                slide,
                shapes,
                RenderWidthPx,
                RenderHeightPx),
            Guid.NewGuid().ToString("N"));

    internal bool TryWrite(PresentationClipboardWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Content is null)
            return false;

        try
        {
            _clipboard.Write(request.Content);
            _ownership.RecordSuccessfulWrite(request.Content, CurrentSequenceIdentity());
            return true;
        }
        catch
        {
            _ownership.Invalidate();
            return false;
        }
    }

    public void Paste(EditingSession editor, bool preferOsClipboard = true) =>
        PasteWithResult(editor, preferOsClipboard);

    internal PresentationClipboardPasteSource PasteWithResult(
        EditingSession editor,
        bool preferOsClipboard = true)
    {
        ArgumentNullException.ThrowIfNull(editor);

        PresentationClipboardContent content;
        try
        {
            content = _clipboard.Read();
        }
        catch
        {
            content = new PresentationClipboardContent();
        }

        var ownCopy = preferOsClipboard
            && _ownership.IsCurrent(content, CurrentSequenceIdentity(), editor.CanPaste);
        return PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            content,
            ownCopy,
            preferOsClipboard);
    }

    internal DataObject BuildDataObject(
        Presentation presentation,
        Slide slide,
        IReadOnlyList<SlideShape> shapes)
    {
        var content = PresentationClipboardContentFactory.CreateSelection(
            presentation,
            slide,
            shapes,
            (sourcePresentation, sourceSlide, sourceShapes) => _renderer.RenderShapesToPng(
                sourcePresentation,
                sourceSlide,
                sourceShapes,
                RenderWidthPx,
                RenderHeightPx),
            Guid.NewGuid().ToString("N"));
        return WpfOsClipboard.BuildDataObject(content);
    }

    public static PasteAction DecidePasteAction(
        bool osHasImage,
        bool osHasText,
        bool internalHasData,
        bool preferOsClipboard = true,
        bool ownCopyIsCurrentOnOs = false,
        bool osHasRichText = false,
        bool osHasXamlPackage = false)
    {
        var source = !preferOsClipboard && internalHasData
            ? PresentationClipboardPasteSource.Internal
            : PresentationClipboardPastePlanner.Decide(
                hasNativeSelection: false,
                hasImage: osHasImage,
                hasText: osHasText,
                internalHasData: internalHasData,
                ownCopyIsCurrent: ownCopyIsCurrentOnOs,
                hasRichText: osHasRichText,
                hasXamlPackage: osHasXamlPackage);
        return source switch
        {
            PresentationClipboardPasteSource.Image => PasteAction.OsImage,
            PresentationClipboardPasteSource.RichText => PasteAction.OsText,
            PresentationClipboardPasteSource.XamlPackage => PasteAction.OsText,
            PresentationClipboardPasteSource.Text => PasteAction.OsText,
            PresentationClipboardPasteSource.Internal => PasteAction.Internal,
            _ => PasteAction.Nothing,
        };
    }

    internal static string ExtractText(IEnumerable<SlideShape> shapes) =>
        PresentationClipboardContentFactory.ExtractText(shapes) ?? string.Empty;

    private string? CurrentSequenceIdentity()
    {
        var sequence = _clipboard.SequenceNumber;
        return sequence > 0
            ? sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }
}

public enum PasteAction
{
    OsImage,
    OsText,
    Internal,
    Nothing,
}
