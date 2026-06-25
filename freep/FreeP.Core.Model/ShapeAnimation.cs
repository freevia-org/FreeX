namespace FreeP.Core.Model;

/// <summary>
/// A single animation build step targeting one shape on a slide.
/// Represents the common case: a preset entrance/emphasis/exit effect in the main sequence.
/// Maps to one <c>p:par</c> inside the main <c>p:seq</c> inside <c>p:timing/p:tnLst</c>.
/// </summary>
public sealed class ShapeAnimation
{
    /// <summary>The shape this animation targets (matches <see cref="SlideShape.Id"/>).</summary>
    public uint ShapeId { get; set; }

    /// <summary>Entrance, Emphasis, or Exit.</summary>
    public AnimationKind Kind { get; set; } = AnimationKind.Entrance;

    /// <summary>The specific visual preset effect.</summary>
    public AnimationPreset Preset { get; set; } = AnimationPreset.Appear;

    /// <summary>When this animation step fires relative to the previous step.</summary>
    public AnimationTrigger Trigger { get; set; } = AnimationTrigger.OnClick;

    /// <summary>
    /// Delay before the animation starts, in milliseconds.
    /// For <see cref="AnimationTrigger.WithPrevious"/> or <see cref="AnimationTrigger.AfterPrevious"/>,
    /// this is an offset from the trigger event. For OnClick this is typically 0.
    /// </summary>
    public int DelayMs { get; set; } = 0;

    /// <summary>Duration of the animation effect in milliseconds. Typical: 500 (fast), 1000 (medium), 2000 (slow).</summary>
    public int DurationMs { get; set; } = 500;

    /// <summary>Optional direction modifier (e.g. FlyIn from left vs. right).</summary>
    public AnimationDirection? Direction { get; set; }
}

/// <summary>The role of the animation in the build sequence.</summary>
public enum AnimationKind
{
    Entrance,
    Emphasis,
    Exit,
}

/// <summary>
/// Preset animation effects. Maps to OOXML presetClass + presetID combinations.
/// See mapping table in PptxAnimationMap.
/// </summary>
public enum AnimationPreset
{
    // Entrance / Exit
    Appear,
    Fade,
    FlyIn,
    Wipe,
    Zoom,
    Split,
    Blinds,
    Box,
    Checkerboard,
    Circle,
    Crawl,
    Diamond,
    Dissolve,
    Flash,
    Peek,
    Plus,
    RandomBars,
    Spiral,
    Strips,
    Swivel,
    Wedge,
    Wheel,
    Bounce,
    Float,
    Swoop,
    Boomerang,

    // Emphasis
    Grow,
    Shrink,
    Spin,
    Pulse,
    ColorPulse,
    Teeter,
    Blink,
    Bold,
    Wave,
    Underline,
    GrowWithColor,
    ChangeColor,
    Shimmer,
}

/// <summary>When an animation step is triggered.</summary>
public enum AnimationTrigger
{
    /// <summary>Fires on next mouse click.</summary>
    OnClick,
    /// <summary>Fires simultaneously with the previous animation.</summary>
    WithPrevious,
    /// <summary>Fires after the previous animation completes.</summary>
    AfterPrevious,
}

/// <summary>
/// Direction modifier for animations that support it (FlyIn, Wipe, Split, etc.).
/// </summary>
public enum AnimationDirection
{
    Left,
    Right,
    Up,
    Down,
    LeftUp,
    LeftDown,
    RightUp,
    RightDown,
    Horizontal,
    Vertical,
    In,
    Out,
    FromLeft,
    FromRight,
    FromTop,
    FromBottom,
    FromTopLeft,
    FromTopRight,
    FromBottomLeft,
    FromBottomRight,
}
