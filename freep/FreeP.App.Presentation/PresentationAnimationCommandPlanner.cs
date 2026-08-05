using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationAnimationCommandIntentKind
{
    AddEffect,
    AddMotionPath,
    RemoveSelectedShapeAnimations,
    SetTrigger,
    SetDuration,
    SetDelay,
    MoveEarlier,
    MoveLater,
    ReverseMotionPath,
    TogglePane,
}

public enum PresentationMotionPathPreset
{
    Right,
    Left,
    Up,
    Down,
    ArcRight,
    ArcLeft,
    ArcUp,
    ArcDown,
    Circle,
    Loop,
    S,
    FigureEight,
}

public sealed record PresentationAnimationCommandPlan(
    string CommandId,
    PresentationAnimationCommandIntentKind Intent,
    AnimationKind? Kind = null,
    AnimationPreset? Preset = null,
    PresentationMotionPathPreset? MotionPathPreset = null);

public static class PresentationAnimationCommandPlanner
{
    public const int DefaultDurationMs = 500;

    public static readonly IReadOnlyList<PresentationAnimationCommandPlan> BuiltInPlans =
        new[]
        {
            new PresentationAnimationCommandPlan("freep.anim.entrance.appear", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Appear),
            new PresentationAnimationCommandPlan("freep.anim.entrance.fade", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Fade),
            new PresentationAnimationCommandPlan("freep.anim.entrance.fly-in", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.FlyIn),
            new PresentationAnimationCommandPlan("freep.anim.entrance.wipe", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Wipe),
            new PresentationAnimationCommandPlan("freep.anim.entrance.zoom", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Zoom),
            new PresentationAnimationCommandPlan("freep.anim.entrance.split", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Split),
            new PresentationAnimationCommandPlan("freep.anim.entrance.blinds", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Blinds),
            new PresentationAnimationCommandPlan("freep.anim.entrance.checkerboard", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Checkerboard),
            new PresentationAnimationCommandPlan("freep.anim.entrance.box", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Box),
            new PresentationAnimationCommandPlan("freep.anim.entrance.circle", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Circle),
            new PresentationAnimationCommandPlan("freep.anim.entrance.diamond", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Diamond),
            new PresentationAnimationCommandPlan("freep.anim.entrance.plus", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Plus),
            new PresentationAnimationCommandPlan("freep.anim.entrance.strips", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Strips),
            new PresentationAnimationCommandPlan("freep.anim.entrance.wedge", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Wedge),
            new PresentationAnimationCommandPlan("freep.anim.entrance.wheel", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.Wheel),
            new PresentationAnimationCommandPlan("freep.anim.entrance.random-bars", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Entrance, AnimationPreset.RandomBars),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.pulse", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Pulse),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.spin", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Spin),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.grow-shrink", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Grow),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.teeter", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Teeter),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.blink", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Blink),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.color-pulse", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.ColorPulse),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.change-color", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.ChangeColor),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.grow-with-color", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.GrowWithColor),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.wave", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Wave),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.shimmer", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Shimmer),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.bold", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Bold),
            new PresentationAnimationCommandPlan("freep.anim.emphasis.underline", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Emphasis, AnimationPreset.Underline),
            new PresentationAnimationCommandPlan("freep.anim.exit.disappear", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Appear),
            new PresentationAnimationCommandPlan("freep.anim.exit.fade-out", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Fade),
            new PresentationAnimationCommandPlan("freep.anim.exit.fly-out", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.FlyIn),
            new PresentationAnimationCommandPlan("freep.anim.exit.wipe", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Wipe),
            new PresentationAnimationCommandPlan("freep.anim.exit.split", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Split),
            new PresentationAnimationCommandPlan("freep.anim.exit.zoom-out", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Zoom),
            new PresentationAnimationCommandPlan("freep.anim.exit.blinds", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Blinds),
            new PresentationAnimationCommandPlan("freep.anim.exit.checkerboard", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Checkerboard),
            new PresentationAnimationCommandPlan("freep.anim.exit.box", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Box),
            new PresentationAnimationCommandPlan("freep.anim.exit.circle", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Circle),
            new PresentationAnimationCommandPlan("freep.anim.exit.diamond", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Diamond),
            new PresentationAnimationCommandPlan("freep.anim.exit.plus", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Plus),
            new PresentationAnimationCommandPlan("freep.anim.exit.strips", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Strips),
            new PresentationAnimationCommandPlan("freep.anim.exit.wedge", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Wedge),
            new PresentationAnimationCommandPlan("freep.anim.exit.wheel", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.Wheel),
            new PresentationAnimationCommandPlan("freep.anim.exit.random-bars", PresentationAnimationCommandIntentKind.AddEffect, AnimationKind.Exit, AnimationPreset.RandomBars),
            new PresentationAnimationCommandPlan("freep.anim.motion.right", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.Right),
            new PresentationAnimationCommandPlan("freep.anim.motion.left", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.Left),
            new PresentationAnimationCommandPlan("freep.anim.motion.up", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.Up),
            new PresentationAnimationCommandPlan("freep.anim.motion.down", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.Down),
            new PresentationAnimationCommandPlan("freep.anim.motion.arc-right", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.ArcRight),
            new PresentationAnimationCommandPlan("freep.anim.motion.arc-left", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.ArcLeft),
            new PresentationAnimationCommandPlan("freep.anim.motion.arc-up", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.ArcUp),
            new PresentationAnimationCommandPlan("freep.anim.motion.arc-down", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.ArcDown),
            new PresentationAnimationCommandPlan("freep.anim.motion.circle", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.Circle),
            new PresentationAnimationCommandPlan("freep.anim.motion.loop", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.Loop),
            new PresentationAnimationCommandPlan("freep.anim.motion.s", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.S),
            new PresentationAnimationCommandPlan("freep.anim.motion.figure-eight", PresentationAnimationCommandIntentKind.AddMotionPath, AnimationKind.Motion, MotionPathPreset: PresentationMotionPathPreset.FigureEight),
            new PresentationAnimationCommandPlan("freep.anim.motion.reverse", PresentationAnimationCommandIntentKind.ReverseMotionPath),
            new PresentationAnimationCommandPlan("freep.anim.none", PresentationAnimationCommandIntentKind.RemoveSelectedShapeAnimations),
            new PresentationAnimationCommandPlan("freep.anim.trigger", PresentationAnimationCommandIntentKind.SetTrigger),
            new PresentationAnimationCommandPlan("freep.anim.duration", PresentationAnimationCommandIntentKind.SetDuration),
            new PresentationAnimationCommandPlan("freep.anim.delay", PresentationAnimationCommandIntentKind.SetDelay),
            new PresentationAnimationCommandPlan("freep.anim.move-earlier", PresentationAnimationCommandIntentKind.MoveEarlier),
            new PresentationAnimationCommandPlan("freep.anim.move-later", PresentationAnimationCommandIntentKind.MoveLater),
            new PresentationAnimationCommandPlan("freep.anim.pane", PresentationAnimationCommandIntentKind.TogglePane),
        };

    public static bool TryPlan(string commandId, out PresentationAnimationCommandPlan plan)
    {
        foreach (var candidate in BuiltInPlans)
        {
            if (StringComparer.Ordinal.Equals(candidate.CommandId, commandId))
            {
                plan = candidate;
                return true;
            }
        }

        plan = default!;
        return false;
    }

    public static bool TryApply(
        EditingSession editor,
        PresentationAnimationCommandPlan plan,
        string? selectedValue = null,
        Action<PresentationAnimationCommandPlan>? onAnimationPane = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        switch (plan.Intent)
        {
            case PresentationAnimationCommandIntentKind.AddEffect:
                if (plan.Kind is not { } kind || plan.Preset is not { } preset)
                {
                    return false;
                }

                if (!TryGetSelectedShapeId(editor, out _))
                {
                    return false;
                }

                editor.AddAnimation(0, BuildAnimation(kind, preset));
                return true;

            case PresentationAnimationCommandIntentKind.AddMotionPath:
                if (plan.MotionPathPreset is not { } motionPathPreset
                    || !TryGetSelectedShapeId(editor, out _))
                {
                    return false;
                }

                editor.AddAnimation(0, BuildMotionAnimation(motionPathPreset));
                return true;

            case PresentationAnimationCommandIntentKind.RemoveSelectedShapeAnimations:
                return RemoveSelectedShapeAnimations(editor);

            case PresentationAnimationCommandIntentKind.SetTrigger:
                return TryApplyToSelectedShapeAnimation(editor, animation =>
                {
                    if (!TryParseTrigger(selectedValue, out var trigger))
                    {
                        return null;
                    }

                    var updated = CloneAnimation(animation);
                    updated.Trigger = trigger;
                    return updated;
                });

            case PresentationAnimationCommandIntentKind.SetDuration:
                return TryApplyToSelectedShapeAnimation(editor, animation =>
                {
                    if (!AnimationPanePlanner.TryParseDuration(selectedValue ?? string.Empty, out int durationMs))
                    {
                        return null;
                    }

                    var updated = CloneAnimation(animation);
                    updated.DurationMs = durationMs;
                    return updated;
                });

            case PresentationAnimationCommandIntentKind.SetDelay:
                return TryApplyToSelectedShapeAnimation(editor, animation =>
                {
                    if (!AnimationPanePlanner.TryParseDelay(selectedValue ?? string.Empty, out int delayMs))
                    {
                        return null;
                    }

                    var updated = CloneAnimation(animation);
                    updated.DelayMs = delayMs;
                    return updated;
                });

            case PresentationAnimationCommandIntentKind.MoveEarlier:
                return MoveSelectedShapeAnimation(editor, offset: -1);

            case PresentationAnimationCommandIntentKind.MoveLater:
                return MoveSelectedShapeAnimation(editor, offset: 1);

            case PresentationAnimationCommandIntentKind.ReverseMotionPath:
                return TryApplyToSelectedShapeAnimation(editor, animation =>
                {
                    if (animation.Kind != AnimationKind.Motion
                        || animation.Motion is not { Segments.Count: > 1 })
                    {
                        return null;
                    }

                    var updated = CloneAnimation(animation);
                    updated.Motion = MotionPath.ReversedClone(animation.Motion);
                    return updated;
                });

            case PresentationAnimationCommandIntentKind.TogglePane:
                if (onAnimationPane is null)
                {
                    return false;
                }

                onAnimationPane(plan);
                return true;

            default:
                return false;
        }
    }

    public static ShapeAnimation BuildAnimation(AnimationKind kind, AnimationPreset preset) =>
        new()
        {
            Kind = kind,
            Preset = preset,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = DefaultDurationMs,
        };

    public static ShapeAnimation BuildMotionAnimation(PresentationMotionPathPreset preset)
    {
        var motion = new MotionPath { Origin = "parent" };
        motion.Segments.Add(MotionPathSegment.MoveTo(0, 0));

        switch (preset)
        {
            case PresentationMotionPathPreset.Right:
                motion.Segments.Add(MotionPathSegment.LineTo(0.5, 0));
                break;
            case PresentationMotionPathPreset.Left:
                motion.Segments.Add(MotionPathSegment.LineTo(-0.5, 0));
                break;
            case PresentationMotionPathPreset.Up:
                motion.Segments.Add(MotionPathSegment.LineTo(0, -0.5));
                break;
            case PresentationMotionPathPreset.Down:
                motion.Segments.Add(MotionPathSegment.LineTo(0, 0.5));
                break;
            case PresentationMotionPathPreset.ArcRight:
                motion.Segments.Add(MotionPathSegment.CubicTo(0.15, -0.25, 0.35, -0.25, 0.5, 0));
                break;
            case PresentationMotionPathPreset.ArcLeft:
                motion.Segments.Add(MotionPathSegment.CubicTo(-0.15, -0.25, -0.35, -0.25, -0.5, 0));
                break;
            case PresentationMotionPathPreset.ArcUp:
                motion.Segments.Add(MotionPathSegment.CubicTo(0.25, -0.15, 0.25, -0.35, 0, -0.5));
                break;
            case PresentationMotionPathPreset.ArcDown:
                motion.Segments.Add(MotionPathSegment.CubicTo(0.25, 0.15, 0.25, 0.35, 0, 0.5));
                break;
            case PresentationMotionPathPreset.Circle:
                // Four cubic quarters around a radius-.5 loop, starting and ending at the origin.
                motion.Segments.Add(MotionPathSegment.CubicTo(0.276, 0, 0.5, -0.224, 0.5, -0.5));
                motion.Segments.Add(MotionPathSegment.CubicTo(0.5, -0.776, 0.276, -1, 0, -1));
                motion.Segments.Add(MotionPathSegment.CubicTo(-0.276, -1, -0.5, -0.776, -0.5, -0.5));
                motion.Segments.Add(MotionPathSegment.CubicTo(-0.5, -0.224, -0.276, 0, 0, 0));
                break;
            case PresentationMotionPathPreset.Loop:
                motion.Segments.Add(MotionPathSegment.CubicTo(0.7, 0, 0.8, -0.6, 0.25, -0.6));
                motion.Segments.Add(MotionPathSegment.CubicTo(-0.3, -0.6, -0.4, 0, 0, 0));
                break;
            case PresentationMotionPathPreset.S:
                motion.Segments.Add(MotionPathSegment.CubicTo(0.2, -0.3, 0.8, -0.3, 1, -0.5));
                motion.Segments.Add(MotionPathSegment.CubicTo(1.2, -0.7, 1.2, -1, 0.8, -1));
                motion.Segments.Add(MotionPathSegment.CubicTo(0.4, -1, 0.2, -0.7, 0, -0.5));
                motion.Segments.Add(MotionPathSegment.CubicTo(-0.2, -0.3, -0.2, -0.1, 0, 0));
                break;
            case PresentationMotionPathPreset.FigureEight:
                motion.Segments.Add(MotionPathSegment.CubicTo(0.6, -0.5, 0.6, -1, 0, -1));
                motion.Segments.Add(MotionPathSegment.CubicTo(-0.6, -1, -0.6, -0.5, 0, 0));
                motion.Segments.Add(MotionPathSegment.CubicTo(0.6, 0.5, 0.6, 1, 0, 1));
                motion.Segments.Add(MotionPathSegment.CubicTo(-0.6, 1, -0.6, 0.5, 0, 0));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
        }

        return new ShapeAnimation
        {
            Kind = AnimationKind.Motion,
            Preset = AnimationPreset.Appear,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = DefaultDurationMs,
            Motion = motion,
        };
    }

    public static bool TryParseTrigger(string? selectedValue, out AnimationTrigger trigger)
    {
        var text = selectedValue?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            trigger = AnimationTrigger.OnClick;
            return false;
        }

        var normalized = text.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (StringComparer.OrdinalIgnoreCase.Equals(normalized, "OnClick"))
        {
            trigger = AnimationTrigger.OnClick;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(normalized, "WithPrevious"))
        {
            trigger = AnimationTrigger.WithPrevious;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(normalized, "AfterPrevious"))
        {
            trigger = AnimationTrigger.AfterPrevious;
            return true;
        }

        trigger = AnimationTrigger.OnClick;
        return false;
    }

    public static ShapeAnimation CloneAnimation(ShapeAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return new ShapeAnimation
        {
            ShapeId = animation.ShapeId,
            Kind = animation.Kind,
            Preset = animation.Preset,
            Trigger = animation.Trigger,
            DelayMs = animation.DelayMs,
            DurationMs = animation.DurationMs,
            RepeatCount = animation.RepeatCount,
            RepeatIndefinitely = animation.RepeatIndefinitely,
            AutoReverse = animation.AutoReverse,
            Acceleration = animation.Acceleration,
            Deceleration = animation.Deceleration,
            Direction = animation.Direction,
            WheelSpokeCount = animation.WheelSpokeCount,
            EffectSubtype = animation.EffectSubtype,
            ScaleBehavior = animation.ScaleBehavior?.Clone(),
            RawPresetClass = animation.RawPresetClass,
            RawPresetId = animation.RawPresetId,
            RawPresetSubtype = animation.RawPresetSubtype,
            Motion = animation.Motion,
            TriggerShapeId = animation.TriggerShapeId,
        };
    }

    private static bool RemoveSelectedShapeAnimations(EditingSession editor)
    {
        if (!TryGetSelectedShapeId(editor, out uint shapeId))
        {
            return false;
        }

        var animations = editor.CurrentSlideAnimations;
        var removed = false;
        for (int i = animations.Count - 1; i >= 0; i--)
        {
            if (animations[i].ShapeId == shapeId)
            {
                editor.RemoveAnimation(i);
                removed = true;
            }
        }

        return removed;
    }

    private static bool TryApplyToSelectedShapeAnimation(
        EditingSession editor,
        Func<ShapeAnimation, ShapeAnimation?> update)
    {
        if (!TryGetSelectedShapeAnimationIndex(editor, out int index))
        {
            return false;
        }

        var updated = update(editor.CurrentSlideAnimations[index]);
        if (updated is null)
        {
            return false;
        }

        editor.SetAnimation(index, updated);
        return true;
    }

    private static bool MoveSelectedShapeAnimation(EditingSession editor, int offset)
    {
        if (!TryGetSelectedShapeAnimationIndex(editor, out int index))
        {
            return false;
        }

        var newIndex = index + offset;
        if (newIndex < 0 || newIndex >= editor.CurrentSlideAnimations.Count)
        {
            return false;
        }

        editor.MoveAnimation(index, newIndex);
        return true;
    }

    private static bool TryGetSelectedShapeAnimationIndex(EditingSession editor, out int index)
    {
        index = -1;
        if (!TryGetSelectedShapeId(editor, out uint shapeId))
        {
            return false;
        }

        var animations = editor.CurrentSlideAnimations;
        for (int i = animations.Count - 1; i >= 0; i--)
        {
            if (animations[i].ShapeId == shapeId)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSelectedShapeId(EditingSession editor, out uint shapeId)
    {
        if (editor.SelectedShapeIds.Count > 0)
        {
            shapeId = editor.SelectedShapeIds[0];
            return true;
        }

        shapeId = 0;
        return false;
    }
}
