using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

// ══════════════════════════════════════════════════════════════════════════════════
//  OS-Clipboard interop service (Wave 10B)
//
//  Design goals:
//  1. Keep EditingSession completely framework-free — System.Windows.Clipboard is
//     ONLY accessed here and in MainWindow (which injects an IOsClipboard).
//  2. The service is testable: IOsClipboard is the seam; tests inject FakeOsClipboard
//     so no real clipboard access happens during unit-test runs.
//  3. Graceful degradation: all real-Clipboard calls are wrapped in try/catch so a
//     clipboard-locked state (e.g. another process holds it) never crashes the app.
// ══════════════════════════════════════════════════════════════════════════════════

// ── Clipboard abstraction ──────────────────────────────────────────────────────────

/// <summary>
/// Abstraction over the OS clipboard.  The real implementation delegates to
/// <see cref="System.Windows.Clipboard"/>; the test implementation uses in-memory fields.
/// </summary>
public interface IOsClipboard
{
    /// <summary>Returns true when the clipboard contains image data.</summary>
    bool ContainsImage();

    /// <summary>Returns true when the clipboard contains text data.</summary>
    bool ContainsText();

    /// <summary>
    /// Retrieves the clipboard image as a PNG byte array, or null if unavailable.
    /// </summary>
    byte[]? GetImagePngBytes();

    /// <summary>Returns the clipboard text, or null if unavailable.</summary>
    string? GetText();

    /// <summary>
    /// Places a <see cref="DataObject"/> onto the OS clipboard.
    /// Implementations should be resilient to clipboard-locked errors.
    /// </summary>
    void SetDataObject(DataObject data);
}

/// <summary>
/// Real OS-clipboard implementation backed by <see cref="System.Windows.Clipboard"/>.
/// All operations are wrapped in try/catch so a clipboard-locked state never throws.
/// Must be called on the WPF dispatcher thread.
/// </summary>
public sealed class WpfOsClipboard : IOsClipboard
{
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
            if (!Clipboard.ContainsImage()) return null;
            var bmp = Clipboard.GetImage();
            if (bmp is null) return null;
            return BitmapSourceToPng(bmp);
        }
        catch { return null; }
    }

    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    public void SetDataObject(DataObject data)
    {
        try { Clipboard.SetDataObject(data, copy: true); }
        catch { /* clipboard locked — degrade silently */ }
    }

    private static byte[] BitmapSourceToPng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}

// ── Shape renderer abstraction ─────────────────────────────────────────────────────

/// <summary>
/// Abstraction for rendering a set of shapes to a PNG byte array.
/// Real implementation uses WPF RenderTargetBitmap; test stubs return fixed bytes.
/// </summary>
public interface IShapeRenderer
{
    /// <summary>
    /// Renders <paramref name="shapes"/> from <paramref name="slide"/> in the context of
    /// <paramref name="presentation"/> to a PNG.
    /// Returns an empty array if rendering fails or nothing is selected.
    /// </summary>
    byte[] RenderShapesToPng(
        Presentation          presentation,
        Slide                 slide,
        IReadOnlyList<SlideShape> shapes,
        int                   widthPx,
        int                   heightPx);
}

// ── Clipboard service ──────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates OS-clipboard interop for FreeP (Wave 10B).
///
/// Copy/Cut:
///   Renders selected shapes to a PNG (via <see cref="IShapeRenderer"/>) and the shapes'
///   text concatenated as plain text, then places both on the OS clipboard via
///   <see cref="IOsClipboard"/>.  This lets FreeP content be pasted into other apps.
///
/// Paste (Y6 fix — in-app copy→paste prefers the internal editable clipboard):
///   When the most-recent OS-clipboard write was produced by THIS service instance
///   (i.e. the user pressed Ctrl+C/X inside FreeP), and the internal shape clipboard is
///   still populated, <see cref="Paste"/> prefers the internal clipboard so the pasted
///   result is an editable deep-copied shape rather than a flattened PNG.
///   An image copied from ANOTHER app still inserts a picture (the generation token will
///   not match, so the OS-image path is taken).
///
///   Priority when internal clipboard carries our own copy (ownCopyIsCurrentOnOs=true):
///     internal > OS text > OS image > nothing
///
///   Priority when OS clipboard was set by an external app (ownCopyIsCurrentOnOs=false):
///     OS image > OS text > internal > nothing
///
/// Strategy is documented at the call site.
/// </summary>
public sealed class OsClipboardService
{
    private readonly IOsClipboard    _clipboard;
    private readonly IShapeRenderer  _renderer;

    // ── Own-copy generation token (Y6) ────────────────────────────────────────────
    // Incremented each time THIS service places content on the OS clipboard.
    // Stored so Paste() can detect whether the current OS clipboard content was
    // produced by this app instance and should therefore yield to the internal clipboard.
    private uint _ownCopyGeneration;        // monotonically increasing counter
    private uint _lastPlacedGeneration;     // value written during the last PlaceSelection call

    /// <summary>
    /// True when the OS clipboard currently holds content placed by THIS service instance
    /// (i.e. since the last in-app Ctrl+C / Ctrl+X).  Paste uses this to prefer the
    /// internal editable clipboard over the rasterised OS image.
    /// </summary>
    internal bool OwnCopyIsCurrentOnOs => _ownCopyGeneration > 0
                                       && _ownCopyGeneration == _lastPlacedGeneration;

    /// <summary>
    /// Size (in pixels) used when rendering the selection to a PNG for the OS clipboard.
    /// Defaults to 1280×720 (full HD equivalent at 96 DPI).
    /// </summary>
    public int RenderWidthPx  { get; set; } = 1280;
    public int RenderHeightPx { get; set; } = 720;

    public OsClipboardService(IOsClipboard clipboard, IShapeRenderer renderer)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _renderer  = renderer  ?? throw new ArgumentNullException(nameof(renderer));
    }

    // ── Copy / Cut ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places the currently selected shapes onto the OS clipboard as:
    /// (a) a PNG image rendered via <see cref="IShapeRenderer"/>, and
    /// (b) the concatenated plain text of all selected shapes (CF_TEXT).
    ///
    /// Call AFTER <see cref="EditingSession.CopySelectedShapes"/> or
    /// <see cref="EditingSession.CutSelectedShapes"/> so the internal clipboard is also updated.
    ///
    /// This method is a no-op when there is no selection or no current slide.
    ///
    /// After a successful write the generation token is bumped so <see cref="Paste"/> knows
    /// the OS clipboard content originates from this app and should yield to the internal
    /// editable clipboard (Y6 fix).
    /// </summary>
    public void PlaceSelectionOnOsClipboard(EditingSession editor)
    {
        var slide = editor.CurrentSlide;
        if (slide is null || editor.SelectedShapeIds.Count == 0) return;

        var selectedShapes = editor.SelectedShapeIds
            .Select(id => slide.Shapes.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        if (selectedShapes.Count == 0) return;

        // Build the DataObject with both formats.
        var dataObj = BuildDataObject(editor.Presentation, slide, selectedShapes);
        _clipboard.SetDataObject(dataObj);

        // Bump the own-copy generation token so Paste() can detect that the OS clipboard
        // content was placed by THIS instance and should yield to the internal clipboard.
        _ownCopyGeneration++;
        _lastPlacedGeneration = _ownCopyGeneration;
    }

    /// <summary>
    /// Builds the OS clipboard <see cref="DataObject"/>.
    /// Exposed internally for unit-testing the payload without touching the real clipboard.
    /// </summary>
    internal DataObject BuildDataObject(
        Presentation          presentation,
        Slide                 slide,
        IReadOnlyList<SlideShape> shapes)
    {
        var dataObj = new DataObject();

        // (a) PNG image of the selection.
        var pngBytes = _renderer.RenderShapesToPng(
            presentation, slide, shapes, RenderWidthPx, RenderHeightPx);
        if (pngBytes.Length > 0)
        {
            // Place as both PNG (raw bytes) and a BitmapSource (CF_BITMAP / CF_DIB).
            try
            {
                using var ms = new MemoryStream(pngBytes);
                var bmpSource = BitmapFrame.Create(
                    ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bmpSource.Freeze();
                dataObj.SetImage(bmpSource);
            }
            catch { /* renderer produced invalid PNG; skip image format */ }
        }

        // (b) Plain text — concatenate all text runs from all shapes.
        var text = ExtractText(shapes);
        if (!string.IsNullOrEmpty(text))
            dataObj.SetText(text);

        return dataObj;
    }

    // ── Paste ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a paste operation.
    ///
    /// When the OS clipboard was populated by THIS app instance's last Ctrl+C/X
    /// (<see cref="OwnCopyIsCurrentOnOs"/> is true) and the internal shape clipboard has
    /// data, the internal editable clipboard is preferred so the pasted result is an
    /// editable deep-copied shape (not a flattened PNG).  This is the Y6 fix.
    ///
    /// When the OS clipboard was populated by ANOTHER app, OS image or OS text wins as
    /// before (external image paste still inserts a picture shape).
    ///
    /// See <see cref="DecidePasteAction"/> for the pure routing logic (testable).
    /// </summary>
    public void Paste(EditingSession editor, bool preferOsClipboard = true)
    {
        // Y6: detect whether the OS clipboard content came from our own last copy/cut.
        // When it did, prefer the internal editable clipboard over the rasterised image.
        bool ownCopy = OwnCopyIsCurrentOnOs && editor.CanPaste;

        var action = DecidePasteAction(
            osHasImage:          _clipboard.ContainsImage(),
            osHasText:           _clipboard.ContainsText(),
            internalHasData:     editor.CanPaste,
            preferOsClipboard:   preferOsClipboard && !ownCopy,
            ownCopyIsCurrentOnOs: ownCopy);

        switch (action)
        {
            case PasteAction.OsImage:
                var pngBytes = _clipboard.GetImagePngBytes();
                if (pngBytes is { Length: > 0 })
                    editor.InsertPicture(pngBytes, "image/png");
                break;

            case PasteAction.OsText:
                // Y8/Y9: delegate to InsertTextBox(text) so the shape is built with the
                // clipboard text already in its run — a single undoable command, no
                // out-of-band mutation, and multi-line text is split into paragraphs.
                var text = _clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                    editor.InsertTextBox(text);
                break;

            case PasteAction.Internal:
                editor.Paste();
                break;

            case PasteAction.Nothing:
                break;
        }
    }

    /// <summary>
    /// Pure paste-routing decision function — no side effects, fully testable.
    ///
    /// Routing priority:
    /// <list type="bullet">
    ///   <item>When <paramref name="ownCopyIsCurrentOnOs"/> is true (the OS clipboard
    ///     content was placed by this app's last copy/cut) AND
    ///     <paramref name="internalHasData"/> is true:
    ///     internal > OS text > OS image > nothing  (Y6: editable shape preferred).</item>
    ///   <item>When <paramref name="preferOsClipboard"/> is true (external content):
    ///     OS image > OS text > internal > nothing.</item>
    ///   <item>When <paramref name="preferOsClipboard"/> is false:
    ///     internal > OS image > OS text > nothing.</item>
    /// </list>
    /// </summary>
    public static PasteAction DecidePasteAction(
        bool osHasImage,
        bool osHasText,
        bool internalHasData,
        bool preferOsClipboard   = true,
        bool ownCopyIsCurrentOnOs = false)
    {
        // Y6: own-copy path — prefer editable internal clipboard over the rasterised
        // OS image we placed ourselves.  OS text (e.g. plain-text description we also
        // placed) still falls through after Internal; external OS image is deprioritised.
        if (ownCopyIsCurrentOnOs && internalHasData)
        {
            // internal wins; fall through to OS text then OS image only as last resort.
            return PasteAction.Internal;
        }

        if (preferOsClipboard)
        {
            if (osHasImage)       return PasteAction.OsImage;
            if (osHasText)        return PasteAction.OsText;
            if (internalHasData)  return PasteAction.Internal;
        }
        else
        {
            if (internalHasData)  return PasteAction.Internal;
            if (osHasImage)       return PasteAction.OsImage;
            if (osHasText)        return PasteAction.OsText;
        }
        return PasteAction.Nothing;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Concatenates the plain text from all runs in all shapes, separated by newlines between shapes.
    /// </summary>
    internal static string ExtractText(IEnumerable<SlideShape> shapes)
    {
        var parts = new List<string>();
        foreach (var shape in shapes)
        {
            if (shape.TextBody is null) continue;
            var shapeText = string.Join(
                Environment.NewLine,
                shape.TextBody.Paragraphs.Select(p =>
                    string.Concat(p.Runs.Select(r => r.Text ?? string.Empty))));
            if (!string.IsNullOrEmpty(shapeText))
                parts.Add(shapeText);
        }
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}

/// <summary>The action chosen by <see cref="OsClipboardService.DecidePasteAction"/>.</summary>
public enum PasteAction
{
    /// <summary>Insert an image from the OS clipboard.</summary>
    OsImage,
    /// <summary>Insert a textbox with text from the OS clipboard.</summary>
    OsText,
    /// <summary>Use the EditingSession internal clipboard.</summary>
    Internal,
    /// <summary>No clipboard data available; do nothing.</summary>
    Nothing,
}
