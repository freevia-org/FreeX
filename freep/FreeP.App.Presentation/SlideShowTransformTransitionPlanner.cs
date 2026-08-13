namespace FreeP.App.Compositor;

public sealed record SlideShowTransitionSurfaceState(
    double Scale,
    double RotationDegrees,
    double TranslateXFactor,
    double TranslateYFactor,
    double? ClipOpening = null);

public sealed record SlideShowTransitionSurfaceGeometry(
    double Scale,
    double RotationDegrees,
    double TranslateX,
    double TranslateY,
    double? ClipOpening);

public sealed record SlideShowTransformTransitionPlan(
    SlideShowTransitionPlaybackActionKind ActionKind,
    SlideShowTransitionSurfaceState IncomingStart,
    SlideShowTransitionSurfaceState IncomingEnd,
    SlideShowTransitionSurfaceState OutgoingStart,
    SlideShowTransitionSurfaceState OutgoingEnd)
{
    public SlideShowTransitionSurfaceGeometry ResolveIncoming(
        double progress,
        double width,
        double height) =>
        Resolve(IncomingStart, IncomingEnd, progress, width, height);

    public SlideShowTransitionSurfaceGeometry ResolveOutgoing(
        double progress,
        double width,
        double height) =>
        Resolve(OutgoingStart, OutgoingEnd, progress, width, height);

    private static SlideShowTransitionSurfaceGeometry Resolve(
        SlideShowTransitionSurfaceState start,
        SlideShowTransitionSurfaceState end,
        double progress,
        double width,
        double height)
    {
        progress = Math.Clamp(progress, 0, 1);
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        return new SlideShowTransitionSurfaceGeometry(
            Lerp(start.Scale, end.Scale, progress),
            Lerp(start.RotationDegrees, end.RotationDegrees, progress),
            Lerp(start.TranslateXFactor, end.TranslateXFactor, progress) * width,
            Lerp(start.TranslateYFactor, end.TranslateYFactor, progress) * height,
            start.ClipOpening is { } startOpening && end.ClipOpening is { } endOpening
                ? Lerp(startOpening, endOpening, progress)
                : null);
    }

    private static double Lerp(double from, double to, double progress) =>
        from + (to - from) * progress;
}

/// <summary>
/// Owns normalized surface geometry for transform-driven slide transitions.
/// Native hosts animate the resolved states with framework transforms and clips.
/// </summary>
public static class SlideShowTransformTransitionPlanner
{
    private static readonly SlideShowTransitionSurfaceState Identity =
        new(1, 0, 0, 0);

    public static SlideShowTransformTransitionPlan Build(
        SlideShowTransitionPlaybackPlan playback)
    {
        ArgumentNullException.ThrowIfNull(playback);

        return playback.ActionKind switch
        {
            SlideShowTransitionPlaybackActionKind.Zoom => BuildZoom(playback),
            SlideShowTransitionPlaybackActionKind.Pan => BuildPan(playback),
            SlideShowTransitionPlaybackActionKind.Gallery => BuildGallery(playback),
            SlideShowTransitionPlaybackActionKind.Conveyor => BuildConveyor(playback),
            SlideShowTransitionPlaybackActionKind.Window => BuildWindow(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(playback),
                playback.ActionKind,
                "The transition does not use transform-surface playback.")
        };
    }

    private static SlideShowTransformTransitionPlan BuildZoom(
        SlideShowTransitionPlaybackPlan playback)
    {
        var startScale = playback.ZoomIn
            ? SlideShowPlaybackPlanner.ZoomInStartScale
            : SlideShowPlaybackPlanner.ZoomOutStartScale;
        return new(playback.ActionKind, new(startScale, 0, 0, 0), Identity, Identity, Identity);
    }

    private static SlideShowTransformTransitionPlan BuildPan(
        SlideShowTransitionPlaybackPlan playback) =>
        new(
            playback.ActionKind,
            new(
                SlideShowPlaybackPlanner.PanStartScale,
                0,
                playback.IncomingOffsetX,
                playback.IncomingOffsetY),
            Identity,
            Identity,
            Identity);

    private static SlideShowTransformTransitionPlan BuildGallery(
        SlideShowTransitionPlaybackPlan playback)
    {
        var translateX = playback.IncomingOffsetX * SlideShowPlaybackPlanner.GalleryTravelFactor;
        var translateY = playback.IncomingOffsetY * SlideShowPlaybackPlanner.GalleryTravelFactor;
        return new(
            playback.ActionKind,
            new(SlideShowPlaybackPlanner.GalleryStartScale, 0, translateX, translateY),
            Identity,
            Identity,
            new(SlideShowPlaybackPlanner.GalleryOutgoingEndScale, 0, translateX, translateY));
    }

    private static SlideShowTransformTransitionPlan BuildConveyor(
        SlideShowTransitionPlaybackPlan playback)
    {
        var horizontal = Math.Abs(playback.IncomingOffsetX) > 0;
        var translateX = playback.IncomingOffsetX * SlideShowPlaybackPlanner.ConveyorTravelFactor
            + (horizontal
                ? 0
                : Math.Sign(playback.IncomingOffsetY) * SlideShowPlaybackPlanner.ConveyorCrossAxisFactor);
        var translateY = playback.IncomingOffsetY * SlideShowPlaybackPlanner.ConveyorTravelFactor
            + (horizontal
                ? -Math.Sign(playback.IncomingOffsetX) * SlideShowPlaybackPlanner.ConveyorCrossAxisFactor
                : 0);
        var tilt = (horizontal
                ? -Math.Sign(playback.IncomingOffsetX)
                : Math.Sign(playback.IncomingOffsetY))
            * SlideShowPlaybackPlanner.ConveyorTiltDegrees;
        return new(
            playback.ActionKind,
            new(SlideShowPlaybackPlanner.ConveyorStartScale, tilt, translateX, translateY),
            Identity,
            Identity,
            new(
                SlideShowPlaybackPlanner.ConveyorOutgoingEndScale,
                -tilt,
                translateX,
                translateY));
    }

    private static SlideShowTransformTransitionPlan BuildWindow() =>
        new(
            SlideShowTransitionPlaybackActionKind.Window,
            new(
                SlideShowPlaybackPlanner.WindowStartScale,
                0,
                0,
                0,
                SlideShowPlaybackPlanner.WindowInitialOpenFactor),
            Identity with { ClipOpening = 1 },
            Identity,
            Identity);
}
