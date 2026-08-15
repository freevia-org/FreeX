using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationTransitionCommandIntentKind
{
    NoOp,
    SetKind,
    SetDuration,
    ToggleAdvanceOnClick,
    SetAdvanceAfter,
    ApplyToAllSlides,
    RequestSoundPicker,
    ClearSound,
    ToggleSoundLoop,
}

public sealed record PresentationTransitionCommandPlan(
    string CommandId,
    PresentationTransitionCommandIntentKind Intent,
    TransitionKind? Kind = null);

public static class PresentationTransitionCommandPlanner
{
    public const int DefaultDurationMs = 500;

    public static readonly IReadOnlyList<PresentationTransitionCommandPlan> BuiltInPlans =
        new[]
        {
            new PresentationTransitionCommandPlan(
                "freep.transition.none",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.None),
            new PresentationTransitionCommandPlan(
                "freep.transition.fade",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Fade),
            new PresentationTransitionCommandPlan(
                "freep.transition.push",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Push),
            new PresentationTransitionCommandPlan(
                "freep.transition.wipe",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Wipe),
            new PresentationTransitionCommandPlan(
                "freep.transition.split",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Split),
            new PresentationTransitionCommandPlan(
                "freep.transition.box",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Box),
            new PresentationTransitionCommandPlan(
                "freep.transition.doors",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Doors),
            new PresentationTransitionCommandPlan(
                "freep.transition.reveal",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Reveal),
            new PresentationTransitionCommandPlan(
                "freep.transition.flash",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Flash),
            new PresentationTransitionCommandPlan(
                "freep.transition.morph",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Morph),
            new PresentationTransitionCommandPlan(
                "freep.transition.cut",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Cut),
            new PresentationTransitionCommandPlan(
                "freep.transition.cover",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Cover),
            new PresentationTransitionCommandPlan(
                "freep.transition.uncover",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Uncover),
            new PresentationTransitionCommandPlan(
                "freep.transition.blinds",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Blinds),
            new PresentationTransitionCommandPlan(
                "freep.transition.comb",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Comb),
            new PresentationTransitionCommandPlan(
                "freep.transition.random-bars",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.RandomBar),
            new PresentationTransitionCommandPlan(
                "freep.transition.strips",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Strips),
            new PresentationTransitionCommandPlan(
                "freep.transition.wheel-reverse",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.WheelReverse),
            new PresentationTransitionCommandPlan(
                "freep.transition.gallery",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Gallery),
            new PresentationTransitionCommandPlan(
                "freep.transition.conveyor",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Conveyor),
            new PresentationTransitionCommandPlan(
                "freep.transition.pan",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Pan),
            new PresentationTransitionCommandPlan(
                "freep.transition.window",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Window),
            new PresentationTransitionCommandPlan(
                "freep.transition.dissolve",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Dissolve),
            new PresentationTransitionCommandPlan(
                "freep.transition.zoom",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Zoom),
            new PresentationTransitionCommandPlan(
                "freep.transition.wheel",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Wheel),
            new PresentationTransitionCommandPlan(
                "freep.transition.more",
                PresentationTransitionCommandIntentKind.NoOp),
            new PresentationTransitionCommandPlan(
                "freep.transition.fly",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Fly),
            new PresentationTransitionCommandPlan(
                "freep.transition.random",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Random),
            new PresentationTransitionCommandPlan(
                "freep.transition.cube",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Cube),
            new PresentationTransitionCommandPlan(
                "freep.transition.rotate",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Rotate),
            new PresentationTransitionCommandPlan(
                "freep.transition.flip",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Flip),
            new PresentationTransitionCommandPlan(
                "freep.transition.ferris",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Ferris),
            new PresentationTransitionCommandPlan(
                "freep.transition.flythrough",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Flythrough),
            new PresentationTransitionCommandPlan(
                "freep.transition.switch",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Switch),
            new PresentationTransitionCommandPlan(
                "freep.transition.orbit",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Orbit),
            new PresentationTransitionCommandPlan(
                "freep.transition.honeycomb",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Honeycomb),
            new PresentationTransitionCommandPlan(
                "freep.transition.glitter",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Glitter),
            new PresentationTransitionCommandPlan(
                "freep.transition.vortex",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Vortex),
            new PresentationTransitionCommandPlan(
                "freep.transition.shred",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Shred),
            new PresentationTransitionCommandPlan(
                "freep.transition.wind",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Wind),
            new PresentationTransitionCommandPlan(
                "freep.transition.ripple",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Ripple),
            new PresentationTransitionCommandPlan(
                "freep.transition.warp",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Warp),
            new PresentationTransitionCommandPlan(
                "freep.transition.fracture",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Fracture),
            new PresentationTransitionCommandPlan(
                "freep.transition.crush",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Crush),
            new PresentationTransitionCommandPlan(
                "freep.transition.peel-off",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.PeelOff),
            new PresentationTransitionCommandPlan(
                "freep.transition.page-curl-double",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.PageCurlDouble),
            new PresentationTransitionCommandPlan(
                "freep.transition.page-curl-single",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.PageCurlSingle),
            new PresentationTransitionCommandPlan(
                "freep.transition.airplane",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Airplane),
            new PresentationTransitionCommandPlan(
                "freep.transition.origami",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Origami),
            new PresentationTransitionCommandPlan(
                "freep.transition.prism",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Prism),
            new PresentationTransitionCommandPlan(
                "freep.transition.curtains",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Curtains),
            new PresentationTransitionCommandPlan(
                "freep.transition.drape",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Drape),
            new PresentationTransitionCommandPlan(
                "freep.transition.prestige",
                PresentationTransitionCommandIntentKind.SetKind,
                TransitionKind.Prestige),
            new PresentationTransitionCommandPlan(
                "freep.transition.duration",
                PresentationTransitionCommandIntentKind.SetDuration),
            new PresentationTransitionCommandPlan(
                "freep.transition.advance-on-click",
                PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick),
            new PresentationTransitionCommandPlan(
                "freep.transition.advance-after",
                PresentationTransitionCommandIntentKind.SetAdvanceAfter),
            new PresentationTransitionCommandPlan(
                "freep.transition.apply-all",
                PresentationTransitionCommandIntentKind.ApplyToAllSlides),
            new PresentationTransitionCommandPlan(
                "freep.transition.sound",
                PresentationTransitionCommandIntentKind.RequestSoundPicker),
            new PresentationTransitionCommandPlan(
                "freep.transition.sound-none",
                PresentationTransitionCommandIntentKind.ClearSound),
            new PresentationTransitionCommandPlan(
                "freep.transition.sound-loop",
                PresentationTransitionCommandIntentKind.ToggleSoundLoop),
        };

    public static bool TryPlan(string commandId, out PresentationTransitionCommandPlan plan)
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
        PresentationTransitionCommandPlan plan,
        string? selectedValue = null,
        Action? onSoundPicker = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        switch (plan.Intent)
        {
            case PresentationTransitionCommandIntentKind.NoOp:
                return true;

            case PresentationTransitionCommandIntentKind.SetKind:
                if (plan.Kind is not { } kind)
                {
                    return false;
                }

                editor.SetTransition(BuildTransitionForKind(editor.CurrentSlideTransition, kind));
                return true;

            case PresentationTransitionCommandIntentKind.SetDuration:
                if (!FreePRibbonChoiceCatalog.TryResolve(
                        selectedValue,
                        FreePRibbonChoiceCatalog.TransitionDurationChoices,
                        out int durationMs) &&
                    !TryParseSeconds(selectedValue, allowZero: false, out durationMs))
                {
                    return false;
                }

                editor.SetTransition(BuildDurationTransition(editor.CurrentSlideTransition, durationMs));
                return true;

            case PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick:
                editor.SetTransition(BuildAdvanceOnClickTransition(editor.CurrentSlideTransition));
                return true;

            case PresentationTransitionCommandIntentKind.SetAdvanceAfter:
                if (!FreePRibbonChoiceCatalog.TryResolve(
                        selectedValue,
                        FreePRibbonChoiceCatalog.TransitionAdvanceAfterChoices,
                        out int advanceAfterMs) &&
                    !TryParseAdvanceAfterValue(selectedValue, out advanceAfterMs))
                {
                    return false;
                }

                editor.SetTransition(BuildAdvanceAfterTransition(
                    editor.CurrentSlideTransition,
                    advanceAfterMs == 0 ? null : advanceAfterMs));
                return true;

            case PresentationTransitionCommandIntentKind.ApplyToAllSlides:
                var transitions = BuildApplyToAllTransitions(
                    editor.Presentation.Slides.Count,
                    editor.CurrentSlideTransition);
                for (int i = 0; i < transitions.Count; i++)
                {
                    editor.Presentation.Slides[i].Transition = transitions[i];
                }

                return true;

            case PresentationTransitionCommandIntentKind.RequestSoundPicker:
                if (onSoundPicker is null)
                {
                    return false;
                }

                onSoundPicker();
                return true;

            case PresentationTransitionCommandIntentKind.ClearSound:
                if (editor.CurrentSlideTransition is null)
                {
                    return false;
                }

                editor.SetCurrentSlideTransitionSound(null);
                return true;

            case PresentationTransitionCommandIntentKind.ToggleSoundLoop:
                if (editor.CurrentSlideTransition?.Sound is not { } sound)
                {
                    return false;
                }

                var loopedSound = new TransitionSound
                {
                    AudioBytes = sound.AudioBytes is null ? null : (byte[])sound.AudioBytes.Clone(),
                    ContentType = sound.ContentType,
                    RelId = sound.RelId,
                    PartPath = sound.PartPath,
                    Loop = !sound.Loop,
                    IsBuiltIn = sound.IsBuiltIn,
                };
                editor.SetCurrentSlideTransitionSound(loopedSound);
                return true;

            default:
                return false;
        }
    }

    public static SlideTransition? BuildTransitionForKind(
        SlideTransition? currentTransition,
        TransitionKind kind)
    {
        if (kind == TransitionKind.None)
        {
            return null;
        }

        var transition = CloneTransition(currentTransition) ?? new SlideTransition();
        transition.Kind = kind;
        transition.RawXml = null;
        transition.MorphOption = null;
        transition.DurationMs = transition.DurationMs <= 0
            ? DefaultDurationMs
            : transition.DurationMs;
        return transition;
    }

    public static SlideTransition BuildDurationTransition(
        SlideTransition? currentTransition,
        int durationMs)
    {
        if (durationMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), durationMs, "Duration must be positive.");
        }

        var transition = CloneTransition(currentTransition) ?? new SlideTransition();
        transition.DurationMs = durationMs;
        return transition;
    }

    public static SlideTransition BuildAdvanceOnClickTransition(SlideTransition? currentTransition)
    {
        var transition = CloneTransition(currentTransition) ?? new SlideTransition();
        transition.AdvanceOnClick = !transition.AdvanceOnClick;
        return transition;
    }

    /// <summary>
    /// Returns the effective checked state for the Advance On Click ribbon toggle.
    /// A missing transition uses PresentationML's default: a click advances the slide.
    /// </summary>
    public static bool IsAdvanceOnClickChecked(SlideTransition? currentTransition) =>
        currentTransition?.AdvanceOnClick ?? true;

    /// <summary>Returns whether a transition toggle is currently available and selected.</summary>
    public static (bool IsEnabled, bool IsChecked) GetToggleState(
        SlideTransition? currentTransition,
        PresentationTransitionCommandIntentKind intent) =>
        intent switch
        {
            PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick =>
                (true, IsAdvanceOnClickChecked(currentTransition)),
            PresentationTransitionCommandIntentKind.ToggleSoundLoop =>
                (currentTransition?.Sound is not null, currentTransition?.Sound?.Loop == true),
            _ => (false, false),
        };

    public static SlideTransition BuildAdvanceAfterTransition(
        SlideTransition? currentTransition,
        int? advanceAfterMs)
    {
        if (advanceAfterMs is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(advanceAfterMs),
                advanceAfterMs,
                "Advance-after time cannot be negative.");
        }

        var transition = CloneTransition(currentTransition) ?? new SlideTransition();
        transition.AdvanceAfterMs = advanceAfterMs;
        return transition;
    }

    public static IReadOnlyList<SlideTransition?> BuildApplyToAllTransitions(
        int slideCount,
        SlideTransition? sourceTransition)
    {
        if (slideCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slideCount), slideCount, "Slide count cannot be negative.");
        }

        var transitions = new SlideTransition?[slideCount];
        for (int i = 0; i < transitions.Length; i++)
        {
            transitions[i] = CloneTransition(sourceTransition);
        }

        return transitions;
    }

    public static bool TryParseSeconds(string? selectedValue, bool allowZero, out int milliseconds)
    {
        var text = selectedValue?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            milliseconds = 0;
            return false;
        }

        if (text.EndsWith("sec", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^3].Trim();
        }
        else if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1].Trim();
        }

        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds)
            && (allowZero ? seconds >= 0 : seconds > 0))
        {
            milliseconds = (int)Math.Round(seconds * 1000.0);
            return true;
        }

        milliseconds = 0;
        return false;
    }

    public static bool TryParseAdvanceAfterValue(string? selectedValue, out int milliseconds)
    {
        if (TryParseSeconds(selectedValue, allowZero: true, out milliseconds))
        {
            return true;
        }

        var text = selectedValue?.Trim();
        if (text is not null &&
            (StringComparer.OrdinalIgnoreCase.Equals(text, "(none)") ||
             StringComparer.OrdinalIgnoreCase.Equals(text, "none")))
        {
            milliseconds = 0;
            return true;
        }

        return false;
    }

    public static SlideTransition? CloneTransition(SlideTransition? transition)
    {
        if (transition is null)
        {
            return null;
        }

        return new SlideTransition
        {
            Kind = transition.Kind,
            Direction = transition.Direction,
            SplitOrientation = transition.SplitOrientation,
            DurationMs = transition.DurationMs,
            AdvanceOnClick = transition.AdvanceOnClick,
            AdvanceAfterMs = transition.AdvanceAfterMs,
            RawXml = transition.RawXml,
            MorphOption = transition.MorphOption,
            WheelSpokeCount = transition.WheelSpokeCount,
            Sound = transition.Sound is null
                ? null
                : new TransitionSound
                {
                    AudioBytes = transition.Sound.AudioBytes,
                    ContentType = transition.Sound.ContentType,
                    RelId = transition.Sound.RelId,
                    PartPath = transition.Sound.PartPath,
                    Loop = transition.Sound.Loop,
                    IsBuiltIn = transition.Sound.IsBuiltIn,
                },
        };
    }
}
