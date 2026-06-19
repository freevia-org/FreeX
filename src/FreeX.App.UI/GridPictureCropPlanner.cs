using System.Windows;

using FreeX.App.Presentation.Charts;

using EnginePlanner = FreeX.App.Presentation.DrawingInteraction.PictureCropPlanner;
using EngineHandle = FreeX.App.Presentation.DrawingInteraction.PictureCropHandle;
using EngineRatios = FreeX.App.Presentation.DrawingInteraction.PictureCropRatios;

namespace FreeX.App.UI;

public enum PictureCropHandle
{
    None,
    CropNW,
    CropN,
    CropNE,
    CropE,
    CropSE,
    CropS,
    CropSW,
    CropW
}

public readonly record struct PictureCropRatios(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Thin WPF adapter over the portable <see cref="EnginePlanner"/>: the picture-crop math lives in the
/// Presentation layer (no WPF types); this surface only translates between WPF
/// <see cref="Point"/>/<see cref="Rect"/> and the engine's <see cref="LayoutPoint"/>/<see cref="LayoutRect"/>,
/// and between the host-facing <see cref="PictureCropHandle"/>/<see cref="PictureCropRatios"/> and their
/// engine equivalents.
/// </summary>
public static class GridPictureCropPlanner
{
    public const double MinimumVisibleFraction = EnginePlanner.MinimumVisibleFraction;
    public const double DefaultHandleOffset = EnginePlanner.DefaultHandleOffset;
    public const double DefaultHandleSize = EnginePlanner.DefaultHandleSize;
    public const double DefaultHandleHitPadding = EnginePlanner.DefaultHandleHitPadding;

    public static PictureCropHandle HitTestHandle(
        Point position,
        Rect pictureRect,
        double handleOffset = DefaultHandleOffset,
        double handleSize = DefaultHandleSize,
        double handleHitPadding = DefaultHandleHitPadding) =>
        FromEngine(EnginePlanner.HitTestHandle(
            ToLayoutPoint(position),
            ToLayoutRect(pictureRect),
            handleOffset,
            handleSize,
            handleHitPadding));

    public static PictureCropRatios CalculateCrop(
        PictureCropHandle handle,
        PictureCropRatios startCrop,
        Rect pictureRect,
        Point startPosition,
        Point currentPosition,
        double minimumVisibleFraction = MinimumVisibleFraction) =>
        FromEngine(EnginePlanner.CalculateCrop(
            ToEngine(handle),
            ToEngine(startCrop),
            ToLayoutRect(pictureRect),
            ToLayoutPoint(startPosition),
            ToLayoutPoint(currentPosition),
            minimumVisibleFraction));

    public static Rect CalculateVisibleCropRect(Rect pictureRect, PictureCropRatios crop) =>
        ToWpfRect(EnginePlanner.CalculateVisibleCropRect(ToLayoutRect(pictureRect), ToEngine(crop)));

    public static IReadOnlyList<(PictureCropHandle Handle, Point Center)> GetHandleCenters(
        Rect pictureRect,
        double handleOffset = DefaultHandleOffset)
    {
        var engineCenters = EnginePlanner.GetHandleCenters(ToLayoutRect(pictureRect), handleOffset);
        if (engineCenters.Count == 0)
            return [];

        var centers = new (PictureCropHandle Handle, Point Center)[engineCenters.Count];
        for (var i = 0; i < engineCenters.Count; i++)
        {
            var (handle, center) = engineCenters[i];
            centers[i] = (FromEngine(handle), ToWpfPoint(center));
        }

        return centers;
    }

    private static LayoutRect ToLayoutRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect ToWpfRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static LayoutPoint ToLayoutPoint(Point point) => new(point.X, point.Y);

    private static Point ToWpfPoint(LayoutPoint point) => new(point.X, point.Y);

    private static EngineHandle ToEngine(PictureCropHandle handle) => (EngineHandle)handle;

    private static PictureCropHandle FromEngine(EngineHandle handle) => (PictureCropHandle)handle;

    private static EngineRatios ToEngine(PictureCropRatios crop) =>
        new(crop.Left, crop.Top, crop.Right, crop.Bottom);

    private static PictureCropRatios FromEngine(EngineRatios crop) =>
        new(crop.Left, crop.Top, crop.Right, crop.Bottom);
}
