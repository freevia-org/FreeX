using System.Collections.Generic;

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

public enum SlideShowPresenterWorkflowActionKind
{
    StartElapsedClock,
    TrackPerSlideTiming,
    PersistPerSlideTiming,
    RequestNarrationCapture,
    RequestMediaCapture,
    SelectPointerMode,
    ConfigureInkStroke,
    ConfigureLaserOverlay,
    ConfigureEraser,
    KeepInkOnExit,
    ClearInkOnExit
}

public sealed record SlideShowPresenterWorkflowAction(
    SlideShowPresenterWorkflowActionKind Kind,
    bool IsDeferred,
    string StatusText);

public sealed record SlideShowPresenterCommandState(
    string CommandId,
    string Label,
    bool IsEnabled,
    bool IsChecked,
    bool IsDeferred,
    string StatusText);

public sealed record SlideShowPresenterToolPlan(
    SlideShowRecordingTimingPlan Recording,
    SlideShowPointerInkPlan PointerInk,
    IReadOnlyList<SlideShowPresenterWorkflowAction> WorkflowActions,
    IReadOnlyList<SlideShowPresenterCommandState> CommandStates);

public static class SlideShowPresenterToolPlanner
{
    public const double MinInkThicknessDip = 1;
    public const double MaxInkThicknessDip = 64;
    public const string DeferredCaptureReason =
        "Audio, video, and PowerPoint-authored recording capture are deferred adapter capabilities.";
    public const string RehearseTimingsCommandId = "freep.presenter.timing.rehearse";
    public const string RecordTimingsCommandId = "freep.presenter.timing.record";
    public const string NarrationCommandId = "freep.presenter.recording.narration";
    public const string NarrationAndMediaCommandId = "freep.presenter.recording.narration-media";
    public const string ArrowPointerCommandId = "freep.presenter.pointer.arrow";
    public const string LaserPointerCommandId = "freep.presenter.pointer.laser";
    public const string PenPointerCommandId = "freep.presenter.pointer.pen";
    public const string HighlighterPointerCommandId = "freep.presenter.pointer.highlighter";
    public const string EraserPointerCommandId = "freep.presenter.pointer.eraser";
    public const string KeepInkCommandId = "freep.presenter.ink.keep";
    public const string ClearInkCommandId = "freep.presenter.ink.clear";

    public static SlideShowPresenterToolPlan BuildPlan(
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        SlideShowRecordingMediaIntent mediaIntent = SlideShowRecordingMediaIntent.None,
        SlideShowPresenterPointerMode pointerMode = SlideShowPresenterPointerMode.Arrow,
        string? inkColorHex = null,
        double inkThicknessDip = 0,
        SlideShowInkRetentionDecision inkRetentionDecision = SlideShowInkRetentionDecision.KeepInk,
        SlideShowRecordingCaptureAdapterReadiness? captureReadiness = null)
    {
        var recording = PlanRecordingTiming(timingIntent, mediaIntent, captureReadiness);
        var pointerInk = PlanPointerInk(pointerMode, inkColorHex, inkThicknessDip, inkRetentionDecision);

        return new(
            recording,
            pointerInk,
            PlanWorkflowActions(recording, pointerInk),
            PlanCommandStates(recording, pointerInk));
    }

    public static SlideShowRecordingTimingPlan PlanRecordingTiming(
        SlideShowTimingIntent timingIntent,
        SlideShowRecordingMediaIntent mediaIntent,
        SlideShowRecordingCaptureAdapterReadiness? captureReadiness = null)
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
            NarrationCapture: PlanCaptureCapability(
                "Narration capture",
                requestedNarration,
                captureReadiness?.CanCaptureNarration,
                captureReadiness),
            MediaCapture: PlanCaptureCapability(
                "Camera and media capture",
                requestedMedia,
                captureReadiness?.CanCaptureCamera,
                captureReadiness),
            StatusText: FormatRecordingStatus(timingIntent, mediaIntent));
    }

    private static SlideShowDeferredCapability PlanCaptureCapability(
        string name,
        bool requested,
        bool? hostReportsAvailable,
        SlideShowRecordingCaptureAdapterReadiness? captureReadiness)
    {
        if (!requested)
        {
            return SlideShowDeferredCapability.Available($"{name} not requested");
        }

        if (captureReadiness is null)
        {
            return SlideShowDeferredCapability.Deferred(name, DeferredCaptureReason);
        }

        if (hostReportsAvailable == true)
        {
            return SlideShowDeferredCapability.Available(
                $"{name} available via {captureReadiness.AdapterName}");
        }

        var reason = string.IsNullOrWhiteSpace(captureReadiness.UnavailableReason)
            ? $"{name} is unavailable on {captureReadiness.HostName}."
            : captureReadiness.UnavailableReason;
        if (captureReadiness.RequiresUserPermission)
        {
            reason = $"{reason} Permission may be required.";
        }

        return SlideShowDeferredCapability.Deferred(name, reason);
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

    public static IReadOnlyList<SlideShowPresenterWorkflowAction> PlanWorkflowActions(
        SlideShowRecordingTimingPlan recording,
        SlideShowPointerInkPlan pointerInk)
    {
        var actions = new List<SlideShowPresenterWorkflowAction>
        {
            new(
                SlideShowPresenterWorkflowActionKind.StartElapsedClock,
                IsDeferred: false,
                "Track presenter elapsed time"),
        };

        if (recording.ShouldTrackPerSlideTimings)
        {
            actions.Add(new(
                SlideShowPresenterWorkflowActionKind.TrackPerSlideTiming,
                IsDeferred: false,
                "Track per-slide timing"));
        }

        if (recording.ShouldPersistTimings)
        {
            actions.Add(new(
                SlideShowPresenterWorkflowActionKind.PersistPerSlideTiming,
                IsDeferred: false,
                "Persist recorded timings"));
        }

        if (recording.IsNarrationRequested)
        {
            actions.Add(new(
                SlideShowPresenterWorkflowActionKind.RequestNarrationCapture,
                IsDeferred: recording.NarrationCapture.IsDeferred,
                recording.NarrationCapture.Reason));
        }

        if (recording.IsMediaCaptureRequested)
        {
            actions.Add(new(
                SlideShowPresenterWorkflowActionKind.RequestMediaCapture,
                IsDeferred: recording.MediaCapture.IsDeferred,
                recording.MediaCapture.Reason));
        }

        actions.Add(new(
            SlideShowPresenterWorkflowActionKind.SelectPointerMode,
            IsDeferred: false,
            pointerInk.PointerMode.ToString()));

        if (pointerInk.UsesInkStroke)
        {
            actions.Add(new(
                SlideShowPresenterWorkflowActionKind.ConfigureInkStroke,
                IsDeferred: false,
                $"{pointerInk.InkState.ColorHex}; {pointerInk.InkState.ThicknessDip:0.##} DIP; {pointerInk.InkState.Opacity:0.##} opacity"));
        }
        else if (pointerInk.UsesLaserOverlay)
        {
            actions.Add(new(
                SlideShowPresenterWorkflowActionKind.ConfigureLaserOverlay,
                IsDeferred: false,
                $"{pointerInk.InkState.ColorHex}; {pointerInk.InkState.ThicknessDip:0.##} DIP"));
        }
        else if (pointerInk.UsesEraser)
        {
            actions.Add(new(
                SlideShowPresenterWorkflowActionKind.ConfigureEraser,
                IsDeferred: false,
                $"{pointerInk.InkState.ThicknessDip:0.##} DIP"));
        }

        if (pointerInk.UsesInkStroke || pointerInk.UsesEraser)
        {
            actions.Add(new(
                pointerInk.InkRetentionDecision == SlideShowInkRetentionDecision.ClearInk
                    ? SlideShowPresenterWorkflowActionKind.ClearInkOnExit
                    : SlideShowPresenterWorkflowActionKind.KeepInkOnExit,
                IsDeferred: false,
                pointerInk.InkRetentionDecision == SlideShowInkRetentionDecision.ClearInk
                    ? "Clear ink on slideshow exit"
                    : "Keep ink on slideshow exit"));
        }

        return actions;
    }

    public static IReadOnlyList<SlideShowPresenterCommandState> PlanCommandStates(
        SlideShowRecordingTimingPlan recording,
        SlideShowPointerInkPlan pointerInk)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(pointerInk);

        return new[]
        {
            Command(
                RehearseTimingsCommandId,
                "Rehearse Timings",
                recording.TimingIntent == SlideShowTimingIntent.RehearseTimings,
                isDeferred: false,
                "Track per-slide timings without saving them"),
            Command(
                RecordTimingsCommandId,
                "Record Timings",
                recording.TimingIntent == SlideShowTimingIntent.RecordTimings,
                isDeferred: false,
                "Track and save per-slide timings"),
            Command(
                NarrationCommandId,
                "Narration",
                recording.MediaIntent == SlideShowRecordingMediaIntent.Narration,
                recording.NarrationCapture.IsDeferred,
                recording.NarrationCapture.IsDeferred
                    ? recording.NarrationCapture.Reason
                    : "Narration capture not requested"),
            Command(
                NarrationAndMediaCommandId,
                "Narration and Camera",
                recording.MediaIntent == SlideShowRecordingMediaIntent.NarrationAndMedia,
                recording.MediaCapture.IsDeferred,
                recording.MediaCapture.IsDeferred
                    ? recording.MediaCapture.Reason
                    : "Camera and media capture not requested"),
            PointerCommand(
                ArrowPointerCommandId,
                "Arrow",
                pointerInk,
                SlideShowPresenterPointerMode.Arrow),
            PointerCommand(
                LaserPointerCommandId,
                "Laser Pointer",
                pointerInk,
                SlideShowPresenterPointerMode.LaserPointer),
            PointerCommand(
                PenPointerCommandId,
                "Pen",
                pointerInk,
                SlideShowPresenterPointerMode.Pen),
            PointerCommand(
                HighlighterPointerCommandId,
                "Highlighter",
                pointerInk,
                SlideShowPresenterPointerMode.Highlighter),
            PointerCommand(
                EraserPointerCommandId,
                "Eraser",
                pointerInk,
                SlideShowPresenterPointerMode.Eraser),
            Command(
                KeepInkCommandId,
                "Keep Ink",
                pointerInk.InkRetentionDecision == SlideShowInkRetentionDecision.KeepInk,
                isDeferred: false,
                "Keep presenter ink after slideshow exit"),
            Command(
                ClearInkCommandId,
                "Clear Ink",
                pointerInk.InkRetentionDecision == SlideShowInkRetentionDecision.ClearInk,
                isDeferred: false,
                "Clear presenter ink after slideshow exit")
        };
    }

    private static SlideShowPresenterCommandState PointerCommand(
        string commandId,
        string label,
        SlideShowPointerInkPlan pointerInk,
        SlideShowPresenterPointerMode pointerMode) =>
        Command(
            commandId,
            label,
            pointerInk.PointerMode == pointerMode,
            isDeferred: false,
            pointerInk.PointerMode == pointerMode ? pointerInk.StatusText : $"Switch to {label}");

    private static SlideShowPresenterCommandState Command(
        string commandId,
        string label,
        bool isChecked,
        bool isDeferred,
        string statusText) =>
        new(commandId, label, IsEnabled: true, isChecked, isDeferred, statusText);

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
