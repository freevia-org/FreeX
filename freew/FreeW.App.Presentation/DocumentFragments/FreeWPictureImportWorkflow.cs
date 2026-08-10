using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentFragments;

public enum FreeWPictureImportSourceKind
{
    PreservedRaster,
    Svg,
    NativeRasterization,
}

public sealed record FreeWPictureImportPickerPlan(
    string Title,
    FileDialogPickerTypeDescriptor PictureFiles,
    bool IncludeAllFiles)
{
    public IReadOnlyList<FileDialogPickerTypeDescriptor> FileTypes => IncludeAllFiles
        ? [PictureFiles, new FileDialogPickerTypeDescriptor("All files", ["*.*"])]
        : [PictureFiles];

    public string BuildWpfFilter()
    {
        var patterns = string.Join(';', PictureFiles.Patterns);
        var pictureFilter = $"{PictureFiles.DisplayName}|{patterns}";
        return IncludeAllFiles
            ? $"{pictureFilter}|{FileDialogFilterBuilder.AllFilesFilterEntry}"
            : pictureFilter;
    }
}

public sealed record FreeWPictureImportSizingPolicy(
    double ReferenceDpi = 96,
    double FallbackWidthPt = 200,
    double FallbackHeightPt = 150,
    double MaximumLongEdgePt = 400,
    int VectorRasterMaximumPixelEdge = 400);

public sealed record FreeWPictureImportRequest(
    string CommandName,
    FreeWPictureImportPickerPlan PickerPlan,
    FreeWPictureImportSizingPolicy SizingPolicy);

public enum FreeWPictureImportPickerStatus
{
    Selected,
    Cancelled,
    Unavailable,
}

public sealed record FreeWPictureImportSelection(string Name, object Source);

public sealed record FreeWPictureImportPickerResult(
    FreeWPictureImportPickerStatus Status,
    FreeWPictureImportSelection? Selection = null,
    string? Message = null)
{
    public static FreeWPictureImportPickerResult Selected(string name, object source) =>
        new(FreeWPictureImportPickerStatus.Selected, new FreeWPictureImportSelection(name, source));

    public static FreeWPictureImportPickerResult Cancelled { get; } =
        new(FreeWPictureImportPickerStatus.Cancelled);

    public static FreeWPictureImportPickerResult Unavailable(string message) =>
        new(FreeWPictureImportPickerStatus.Unavailable, Message: message);
}

public interface IFreeWPictureImportPickerPort
{
    Task<FreeWPictureImportPickerResult> PickAsync(
        FreeWPictureImportRequest request,
        CancellationToken cancellationToken);
}

public interface IFreeWPictureImportSourceReaderPort
{
    Task<byte[]> ReadAsync(
        FreeWPictureImportSelection selection,
        CancellationToken cancellationToken);
}

public sealed record FreeWPictureDecoderFacts(
    int PixelWidth,
    int PixelHeight,
    double SourceDpiX = 96,
    double SourceDpiY = 96)
{
    public static FreeWPictureDecoderFacts Unavailable { get; } = new(0, 0, 0, 0);

    public bool HasNaturalSize => PixelWidth > 0 && PixelHeight > 0;
}

public interface IFreeWPictureDecoderPort
{
    ValueTask<FreeWPictureDecoderFacts> DecodeAsync(
        FreeWPictureImportSelection selection,
        byte[] bytes,
        CancellationToken cancellationToken);
}

public sealed record FreeWPictureRasterizationRequest(
    FreeWPictureImportSelection Selection,
    byte[] SourceBytes,
    FreeWPictureImportSourceKind SourceKind,
    int MaximumPixelEdge);

public sealed record FreeWPictureRasterizationOutcome(
    byte[] Bytes,
    FreeWPictureDecoderFacts DecoderFacts,
    ImageFormat Format = ImageFormat.Png);

public interface IFreeWPictureRasterizerPort
{
    ValueTask<FreeWPictureRasterizationOutcome> RasterizeAsync(
        FreeWPictureRasterizationRequest request,
        CancellationToken cancellationToken);
}

public sealed record FreeWPictureImportSizingPlan(
    double WidthPt,
    double HeightPt,
    double EffectiveDpiX,
    double EffectiveDpiY,
    bool UsedFallbackSize,
    bool WasScaled);

public sealed record FreeWPictureInsertionRequest(
    byte[] Bytes,
    ImageFormat Format,
    double WidthPt,
    double HeightPt,
    int OriginalPixelWidth,
    int OriginalPixelHeight);

public sealed record FreeWPictureInsertionResult(bool Applied, string? Message = null)
{
    public static FreeWPictureInsertionResult Success { get; } = new(true);

    public static FreeWPictureInsertionResult NotApplied(string? message = null) =>
        new(false, message);
}

public interface IFreeWPictureInsertionPort
{
    FreeWPictureInsertionResult Insert(FreeWPictureInsertionRequest request);
}

public enum FreeWPictureImportStatus
{
    Succeeded,
    Cancelled,
    Unavailable,
    NotApplied,
    Failed,
}

public sealed record FreeWPictureImportResult(
    FreeWPictureImportRequest Request,
    FreeWPictureImportStatus Status,
    string? SourceName = null,
    FreeWPictureInsertionRequest? Insertion = null,
    string? Message = null,
    Exception? Exception = null);

public enum FreeWPictureImportFailureSurface
{
    Status,
    ModalError,
    None,
}

public sealed record FreeWPictureImportOutcomePresentation(
    string? StatusText = null,
    string? ModalTitle = null,
    string? ModalMessage = null)
{
    public static FreeWPictureImportOutcomePresentation Empty { get; } = new();
}

public static class FreeWPictureImportPlanner
{
    private static readonly IReadOnlyList<string> PicturePatterns =
        ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.tif", "*.tiff", "*.svg"];

    private static readonly IReadOnlyList<string> PictureMimeTypes =
        ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff", "image/svg+xml"];

    public static FreeWPictureImportRequest CreateRequest() =>
        new(
            FreeWFileTextResources.Document.InsertPictureCommand,
            new FreeWPictureImportPickerPlan(
                FreeWFileTextResources.Document.InsertPicturePickerTitle,
                new FileDialogPickerTypeDescriptor(
                    FreeWFileTextResources.PictureFileTypeName,
                    PicturePatterns,
                    PictureMimeTypes),
                IncludeAllFiles: true),
            new FreeWPictureImportSizingPolicy());

    public static FreeWPictureImportSourceKind ClassifySource(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var extension = Path.GetExtension(sourceName);
        if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
            return FreeWPictureImportSourceKind.Svg;

        return InlineImage.FormatForExtension(extension) is not null
            ? FreeWPictureImportSourceKind.PreservedRaster
            : FreeWPictureImportSourceKind.NativeRasterization;
    }

    public static ImageFormat ResolvePreservedFormat(string sourceName) =>
        InlineImage.FormatForExtension(Path.GetExtension(sourceName))
        ?? throw new ArgumentException("The selected picture does not have a preservable image format.", nameof(sourceName));

    public static FreeWPictureImportSizingPlan PlanSize(
        FreeWPictureDecoderFacts facts,
        FreeWPictureImportSizingPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        policy ??= new FreeWPictureImportSizingPolicy();
        Validate(policy);

        if (!facts.HasNaturalSize)
        {
            return new FreeWPictureImportSizingPlan(
                policy.FallbackWidthPt,
                policy.FallbackHeightPt,
                policy.ReferenceDpi,
                policy.ReferenceDpi,
                UsedFallbackSize: true,
                WasScaled: false);
        }

        // Desktop image insertion has historically interpreted pixels at 96 DPI in both renderers.
        // Source DPI remains in the decoder facts for diagnostics, while one reference DPI keeps parity.
        var widthPt = facts.PixelWidth * 72.0 / policy.ReferenceDpi;
        var heightPt = facts.PixelHeight * 72.0 / policy.ReferenceDpi;
        var longestEdge = Math.Max(widthPt, heightPt);
        var wasScaled = longestEdge > policy.MaximumLongEdgePt;
        if (wasScaled)
        {
            var scale = policy.MaximumLongEdgePt / longestEdge;
            widthPt *= scale;
            heightPt *= scale;
        }

        return new FreeWPictureImportSizingPlan(
            widthPt,
            heightPt,
            policy.ReferenceDpi,
            policy.ReferenceDpi,
            UsedFallbackSize: false,
            WasScaled: wasScaled);
    }

    private static void Validate(FreeWPictureImportSizingPolicy policy)
    {
        if (!IsPositiveFinite(policy.ReferenceDpi))
            throw new ArgumentOutOfRangeException(nameof(policy), "Reference DPI must be positive and finite.");
        if (!IsPositiveFinite(policy.FallbackWidthPt) || !IsPositiveFinite(policy.FallbackHeightPt))
            throw new ArgumentOutOfRangeException(nameof(policy), "Fallback dimensions must be positive and finite.");
        if (!IsPositiveFinite(policy.MaximumLongEdgePt))
            throw new ArgumentOutOfRangeException(nameof(policy), "Maximum size must be positive and finite.");
        if (policy.VectorRasterMaximumPixelEdge <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "Vector raster size must be positive.");
    }

    private static bool IsPositiveFinite(double value) => value > 0 && double.IsFinite(value);
}

public static class FreeWPictureImportOutcomePlanner
{
    public static FreeWPictureImportOutcomePresentation Plan(
        FreeWPictureImportResult result,
        SisterAppFileTextSpec fileText,
        FreeWPictureImportFailureSurface failureSurface)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(fileText);

        if (result.Status is FreeWPictureImportStatus.Succeeded
            or FreeWPictureImportStatus.Cancelled
            or FreeWPictureImportStatus.NotApplied
            || failureSurface == FreeWPictureImportFailureSurface.None)
        {
            return FreeWPictureImportOutcomePresentation.Empty;
        }

        var reason = result.Message ?? string.Empty;
        return failureSurface switch
        {
            FreeWPictureImportFailureSurface.Status =>
                new FreeWPictureImportOutcomePresentation(
                    StatusText: result.Status == FreeWPictureImportStatus.Unavailable
                        ? SisterAppFileTextPlanner.FormatCommandUnavailable(fileText, result.Request.CommandName)
                        : SisterAppFileTextPlanner.FormatCommandFailed(fileText, result.Request.CommandName, reason)),
            FreeWPictureImportFailureSurface.ModalError =>
                new FreeWPictureImportOutcomePresentation(
                    ModalTitle: "FreeW",
                    ModalMessage: $"Could not insert the image:\n{reason}"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureSurface), failureSurface, null),
        };
    }
}

/// <summary>
/// Owns picture selection, format policy, sizing, rasterization decisions, and insertion outcomes.
/// Native hosts only implement file, codec, raster, editor, focus, and feedback realization.
/// </summary>
public sealed class FreeWPictureImportWorkflow
{
    private readonly IFreeWPictureImportPickerPort _picker;
    private readonly IFreeWPictureImportSourceReaderPort _reader;
    private readonly IFreeWPictureDecoderPort _decoder;
    private readonly IFreeWPictureRasterizerPort _rasterizer;
    private readonly IFreeWPictureInsertionPort _insertion;

    public FreeWPictureImportWorkflow(
        IFreeWPictureImportPickerPort picker,
        IFreeWPictureImportSourceReaderPort reader,
        IFreeWPictureDecoderPort decoder,
        IFreeWPictureRasterizerPort rasterizer,
        IFreeWPictureInsertionPort insertion)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
        _insertion = insertion ?? throw new ArgumentNullException(nameof(insertion));
    }

    public async Task<FreeWPictureImportResult> ImportAsync(
        CancellationToken cancellationToken = default)
    {
        var request = FreeWPictureImportPlanner.CreateRequest();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pickerResult = await _picker.PickAsync(request, cancellationToken);
            if (pickerResult.Status == FreeWPictureImportPickerStatus.Cancelled)
                return new FreeWPictureImportResult(request, FreeWPictureImportStatus.Cancelled);
            if (pickerResult.Status == FreeWPictureImportPickerStatus.Unavailable)
            {
                return new FreeWPictureImportResult(
                    request,
                    FreeWPictureImportStatus.Unavailable,
                    Message: pickerResult.Message);
            }

            var selection = pickerResult.Selection
                ?? throw new InvalidOperationException("The picture picker did not return a selection.");
            var sourceBytes = await _reader.ReadAsync(selection, cancellationToken);
            var sourceKind = FreeWPictureImportPlanner.ClassifySource(selection.Name);

            byte[] insertionBytes;
            ImageFormat insertionFormat;
            FreeWPictureDecoderFacts decoderFacts;
            if (sourceKind == FreeWPictureImportSourceKind.PreservedRaster)
            {
                insertionBytes = sourceBytes;
                insertionFormat = FreeWPictureImportPlanner.ResolvePreservedFormat(selection.Name);
                decoderFacts = await _decoder.DecodeAsync(selection, sourceBytes, cancellationToken);
            }
            else
            {
                var rasterized = await _rasterizer.RasterizeAsync(
                    new FreeWPictureRasterizationRequest(
                        selection,
                        sourceBytes,
                        sourceKind,
                        request.SizingPolicy.VectorRasterMaximumPixelEdge),
                    cancellationToken);
                insertionBytes = rasterized.Bytes;
                insertionFormat = rasterized.Format;
                decoderFacts = rasterized.DecoderFacts;
            }

            if (insertionBytes.Length == 0)
                throw new InvalidDataException("The selected picture produced no image data.");

            var size = FreeWPictureImportPlanner.PlanSize(decoderFacts, request.SizingPolicy);
            var insertionRequest = new FreeWPictureInsertionRequest(
                insertionBytes,
                insertionFormat,
                size.WidthPt,
                size.HeightPt,
                Math.Max(0, decoderFacts.PixelWidth),
                Math.Max(0, decoderFacts.PixelHeight));
            var insertionResult = _insertion.Insert(insertionRequest);
            return new FreeWPictureImportResult(
                request,
                insertionResult.Applied
                    ? FreeWPictureImportStatus.Succeeded
                    : FreeWPictureImportStatus.NotApplied,
                selection.Name,
                insertionRequest,
                insertionResult.Message);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new FreeWPictureImportResult(
                request,
                FreeWPictureImportStatus.Cancelled,
                Exception: ex);
        }
        catch (Exception ex)
        {
            return new FreeWPictureImportResult(
                request,
                FreeWPictureImportStatus.Failed,
                Message: ex.Message,
                Exception: ex);
        }
    }
}
