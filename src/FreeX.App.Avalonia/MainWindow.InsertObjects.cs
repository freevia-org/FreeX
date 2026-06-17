using System.IO;

using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly FilePickerFileType PictureFileType = new("Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.tif", "*.tiff"],
        MimeTypes = ["image/*"],
    };

    /// <summary>
    /// Inserts a picture chosen from a file onto the active sheet at the active cell, through the shared
    /// session command path and the Core <see cref="FreeX.Core.Commands.InsertPictureCommand"/> the drawing
    /// overlay already paints. The native pixel size is decoded via Avalonia (falling back to a default when
    /// decoding fails); the user can then move/resize it with the existing drawing-object editing. Surfaces
    /// the Core guard message on failure.
    /// </summary>
    private async Task InsertPictureFromFileAsync()
    {
        if (!((IStorageProvider)StorageProvider).CanOpen)
        {
            ShowEditIssue("Insert Picture is unavailable on this platform.");
            return;
        }

        if (!TryCommitPendingFormulaEdit())
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert Picture",
            AllowMultiple = false,
            FileTypeFilter = [PictureFileType],
        });

        IStorageFile? file = null;
        foreach (var candidate in files)
        {
            file = candidate;
            break;
        }

        if (file is null)
            return;

        var contentType = InsertPictureCommandFactory.ContentTypeForPath(file.Name);
        if (contentType is null)
        {
            ShowEditIssue("Unsupported image format.");
            return;
        }

        byte[] imageBytes;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            imageBytes = memory.ToArray();
        }
        catch (IOException ex)
        {
            ShowEditIssue($"Could not read the image: {ex.Message}");
            return;
        }

        if (imageBytes.Length == 0)
        {
            ShowEditIssue("The selected image is empty.");
            return;
        }

        var (width, height) = DecodePictureSize(imageBytes);
        var anchor = _session.ActiveCell;
        var command = InsertPictureCommandFactory.Build(
            _session.ActiveSheet.Id, anchor, imageBytes, contentType, width, height);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Insert Picture failed.");
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell($"Inserted picture at {FormatCellReference(anchor)}");
    }

    /// <summary>Decodes the image's native pixel size via Avalonia, or (0,0) when decoding fails.</summary>
    private static (double Width, double Height) DecodePictureSize(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(stream);
            return (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Converts the current selection into a structured table through the shared session command path,
    /// reusing the Core <see cref="FreeX.Core.Commands.CreateStructuredTableCommand"/>. Header detection
    /// reuses the shell's <see cref="QuickAnalysisSelectionReader"/> heuristic so the menu and (future)
    /// Quick Analysis agree on whether the first row is a header; the Avalonia grid paints the table styling
    /// on the next refresh. Surfaces the Core guard message (e.g. range must include a header row and a data
    /// row) on failure rather than silently no-opping.
    /// </summary>
    private void InsertTableFromSelection()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var hasHeaderRow = QuickAnalysisSelectionReader.Describe(_session.ActiveSheet, range).HasHeaderRow;
        var command = InsertTableCommandFactory.Build(_session.ActiveSheet.Id, range, hasHeaderRow);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? "Insert Table failed.");
            return;
        }

        ClearSelectedDrawingObject();
        RefreshShell($"Created table from {FormatRangeReference(range)}");
    }
}
