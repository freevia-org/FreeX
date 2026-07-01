namespace FreeP.App.Compositor;

public enum SlideShowTimingIntent
{
    None,
    RehearseTimings,
    RecordTimings
}

public enum SlideShowRecordingMediaIntent
{
    None,
    Narration,
    NarrationAndMedia
}

public enum SlideShowPresenterPointerMode
{
    Arrow,
    LaserPointer,
    Pen,
    Highlighter,
    Eraser
}

public enum SlideShowInkRetentionDecision
{
    KeepInk,
    ClearInk
}

public sealed record SlideShowDeferredCapability(
    string Name,
    bool IsAvailable,
    bool IsDeferred,
    string Reason)
{
    public static SlideShowDeferredCapability Available(string name) =>
        new(name, IsAvailable: true, IsDeferred: false, Reason: string.Empty);

    public static SlideShowDeferredCapability Deferred(string name, string reason) =>
        new(name, IsAvailable: false, IsDeferred: true, Reason: reason);
}

public sealed record SlideShowRecordingTimingPlan(
    SlideShowTimingIntent TimingIntent,
    SlideShowRecordingMediaIntent MediaIntent,
    bool ShouldTrackElapsed,
    bool ShouldTrackPerSlideTimings,
    bool ShouldPersistTimings,
    bool IsNarrationRequested,
    bool IsMediaCaptureRequested,
    SlideShowDeferredCapability NarrationCapture,
    SlideShowDeferredCapability MediaCapture,
    string StatusText);

public sealed record SlideShowInkState(
    string ColorHex,
    double ThicknessDip,
    double Opacity);

public sealed record SlideShowPointerInkPlan(
    SlideShowPresenterPointerMode PointerMode,
    SlideShowInkState InkState,
    bool UsesInkStroke,
    bool UsesLaserOverlay,
    bool UsesEraser,
    SlideShowInkRetentionDecision InkRetentionDecision,
    string StatusText);

public sealed record SlideShowPresenterToolPlan(
    SlideShowRecordingTimingPlan Recording,
    SlideShowPointerInkPlan PointerInk);

public static class SlideShowPresenterToolPlanner
{
    public const double MinInkThicknessDip = 1;
    public const double MaxInkThicknessDip = 64;
    public const string DeferredCaptureReason =
        "Audio, video, and PowerPoint-authored recording capture are deferred adapter capabilities.";

    public static SlideShowPresenterToolPlan BuildPlan(
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        SlideShowRecordingMediaIntent mediaIntent = SlideShowRecordingMediaIntent.None,
        SlideShowPresenterPointerMode pointerMode = SlideShowPresenterPointerMode.Arrow,
        string? inkColorHex = null,
        double inkThicknessDip = 0,
        SlideShowInkRetentionDecision inkRetentionDecision = SlideShowInkRetentionDecision.KeepInk) =>
        new(
            PlanRecordingTiming(timingIntent, mediaIntent),
            PlanPointerInk(pointerMode, inkColorHex, inkThicknessDip, inkRetentionDecision));

    public static SlideShowRecordingTimingPlan PlanRecordingTiming(
        SlideShowTimingIntent timingIntent,
        SlideShowRecordingMediaIntent mediaIntent)
    {
        var requestedNarration = mediaIntent is SlideShowRecordingMediaIntent.Narration
            or SlideShowRecordingMediaIntent.NarrationAndMedia;
        var requestedMedia = mediaIntent is SlideShowRecordingMediaIntent.NarrationAndMedia;
        var tracksTimings = timingIntent != SlideShowTimingIntent.None;

        return new SlideShowRecordingTimingPlan(
            timingIntent,
            mediaIntent,
            ShouldTrackElapsed: true,
            ShouldTrackPerSlideTimings: tracksTimings,
            ShouldPersistTimings: timingIntent == SlideShowTimingIntent.RecordTimings,
            IsNarrationRequested: requestedNarration,
            IsMediaCaptureRequested: requestedMedia,
            NarrationCapture: requestedNarration
                ? SlideShowDeferredCapability.Deferred("Narration capture", DeferredCaptureReason)
                : SlideShowDeferredCapability.Available("Narration not requested"),
            MediaCapture: requestedMedia
                ? SlideShowDeferredCapability.Deferred("Camera and media capture", DeferredCaptureReason)
                : SlideShowDeferredCapability.Available("Camera and media capture not requested"),
            StatusText: FormatRecordingStatus(timingIntent, mediaIntent));
    }

    public static SlideShowPointerInkPlan PlanPointerInk(
        SlideShowPresenterPointerMode pointerMode,
        string? inkColorHex,
        double inkThicknessDip,
        SlideShowInkRetentionDecision inkRetentionDecision)
    {
        var defaults = DefaultInkFor(pointerMode);
        var color = NormalizeColorHex(inkColorHex, defaults.ColorHex);
        var thickness = ClampThickness(inkThicknessDip > 0 ? inkThicknessDip : defaults.ThicknessDip);

        return new SlideShowPointerInkPlan(
            pointerMode,
            new SlideShowInkState(color, thickness, defaults.Opacity),
            UsesInkStroke: pointerMode is SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter,
            UsesLaserOverlay: pointerMode == SlideShowPresenterPointerMode.LaserPointer,
            UsesEraser: pointerMode == SlideShowPresenterPointerMode.Eraser,
            inkRetentionDecision,
            StatusText: FormatPointerStatus(pointerMode, inkRetentionDecision));
    }

    private static SlideShowInkState DefaultInkFor(SlideShowPresenterPointerMode pointerMode) =>
        pointerMode switch
        {
            SlideShowPresenterPointerMode.LaserPointer => new("#FF0000", 6, 0.80),
            SlideShowPresenterPointerMode.Highlighter => new("#FFFF00", 12, 0.45),
            SlideShowPresenterPointerMode.Eraser => new("#FFFFFF", 12, 1.00),
            SlideShowPresenterPointerMode.Pen => new("#FF0000", 3, 1.00),
            _ => new("#FF0000", 3, 1.00)
        };

    private static string NormalizeColorHex(string? colorHex, string fallback)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return fallback;
        }

        var trimmed = colorHex.Trim();
        if (trimmed.Length == 6 || trimmed.Length == 8)
        {
            trimmed = "#" + trimmed;
        }

        if ((trimmed.Length != 7 && trimmed.Length != 9) || trimmed[0] != '#')
        {
            return fallback;
        }

        for (var i = 1; i < trimmed.Length; i++)
        {
            if (!Uri.IsHexDigit(trimmed[i]))
            {
                return fallback;
            }
        }

        return trimmed.ToUpperInvariant();
    }

    private static double ClampThickness(double thicknessDip) =>
        Math.Clamp(thicknessDip, MinInkThicknessDip, MaxInkThicknessDip);

    private static string FormatRecordingStatus(
        SlideShowTimingIntent timingIntent,
        SlideShowRecordingMediaIntent mediaIntent) =>
        (timingIntent, mediaIntent) switch
        {
            (SlideShowTimingIntent.RehearseTimings, SlideShowRecordingMediaIntent.None) =>
                "Rehearse timings",
            (SlideShowTimingIntent.RecordTimings, SlideShowRecordingMediaIntent.None) =>
                "Record timings",
            (SlideShowTimingIntent.RecordTimings, SlideShowRecordingMediaIntent.Narration) =>
                "Record timings with deferred narration capture",
            (SlideShowTimingIntent.RecordTimings, SlideShowRecordingMediaIntent.NarrationAndMedia) =>
                "Record timings with deferred narration and media capture",
            _ => "Presenter tools"
        };

    private static string FormatPointerStatus(
        SlideShowPresenterPointerMode pointerMode,
        SlideShowInkRetentionDecision inkRetentionDecision)
    {
        var tool = pointerMode switch
        {
            SlideShowPresenterPointerMode.LaserPointer => "Laser pointer",
            SlideShowPresenterPointerMode.Highlighter => "Highlighter",
            SlideShowPresenterPointerMode.Eraser => "Eraser",
            SlideShowPresenterPointerMode.Pen => "Pen",
            _ => "Arrow"
        };

        var retention = inkRetentionDecision == SlideShowInkRetentionDecision.ClearInk
            ? "clear ink on exit"
            : "keep ink on exit";
        return $"{tool}; {retention}";
    }
}
