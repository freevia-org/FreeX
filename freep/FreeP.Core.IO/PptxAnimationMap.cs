using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Bidirectional mapping tables between FreeP model enums and PresentationML XML string values
/// for transitions and animations.
///
/// TRANSITION MAPPING
/// ==================
/// TransitionKind  | p:transition child element name
/// None            | (no child, omit p:transition)
/// Fade            | p:fade
/// Cut             | p:cut
/// Push            | p:push  (dir attr)
/// Wipe            | p:wipe  (dir attr)
/// Cover           | p:cover (dir attr)
/// Uncover         | p:uncover (dir attr)
/// Split           | p:split (orient + dir attr)
/// Blinds          | p:blinds (dir attr = horz|vert)
/// Dissolve        | p:dissolve
/// Zoom            | p:zoom  (dir attr = in|out)
/// Wheel           | p:wheel (spokes attr)
/// RandomBar       | p:randomBar (dir attr)
/// Strips          | p:strips (dir attr)
/// Fly             | (not standard — mapped to p:push as fallback)
/// Random          | p:random
///
/// ANIMATION PRESET MAPPING
/// ========================
/// AnimationPreset | presetClass | presetID (int)
/// Appear          | entr        | 1
/// Fade            | entr        | 10
/// FlyIn           | entr        | 2
/// Wipe            | entr        | 8
/// Zoom            | entr        | 11
/// Split           | entr        | 3
/// Blinds          | entr        | 4
/// Box             | entr        | 5
/// Checkerboard    | entr        | 6
/// Circle          | entr        | 7
/// Crawl           | entr        | 26
/// Diamond         | entr        | 9
/// Dissolve        | entr        | 25
/// Flash           | entr        | 22
/// Peek            | entr        | 27
/// Plus            | entr        | 13
/// RandomBars      | entr        | 14
/// Spiral          | entr        | 28
/// Strips          | entr        | 16
/// Swivel          | entr        | 17
/// Wedge           | entr        | 18
/// Wheel           | entr        | 19
/// Bounce          | entr        | 21
/// Float           | entr        | 23
/// Swoop           | entr        | 24
/// Boomerang       | entr        | 29
/// Grow            | emph        | 5   (Grow/Shrink)
/// Shrink          | emph        | 5   (same preset, direction=shrink)
/// Spin            | emph        | 8
/// Pulse           | emph        | 14
/// ColorPulse      | emph        | 6
/// Teeter          | emph        | 32
/// Blink           | emph        | 15
/// Bold            | emph        | 1
/// Wave            | emph        | 34
/// Underline       | emph        | 2
/// GrowWithColor   | emph        | 12
/// ChangeColor     | emph        | 7
/// Shimmer         | emph        | 36
/// ChangeFontColor | emph        | 3   (uses the ChangeColor playback contract)
/// ChangeFontSize  | emph        | 4   (uses the Grow/Shrink amount contract)
/// ChangeFillColor | emph        | 1   (native fillcolor behavior; raw ID retained)
///
/// Exit effects share the same presetIDs as Entrance (presetClass = "exit").
/// </summary>
internal static class PptxAnimationMap
{
    // ── Transition kind <-> element name ──────────────────────────────────────────

    public static string? TransitionKindToElementName(TransitionKind kind) => kind switch
    {
        // ── Legacy ────────────────────────────────────────────────────────────────
        TransitionKind.Fade          => "fade",
        TransitionKind.Cut           => "cut",
        TransitionKind.Push          => "push",
        TransitionKind.Wipe          => "wipe",
        TransitionKind.Cover         => "cover",
        TransitionKind.Uncover       => "uncover",
        TransitionKind.Split         => "split",
        TransitionKind.Blinds        => "blinds",
        TransitionKind.Dissolve      => "dissolve",
        TransitionKind.Zoom          => "zoom",
        TransitionKind.Wheel         => "wheel",
        TransitionKind.RandomBar     => "randomBar",
        TransitionKind.Strips        => "strips",
        TransitionKind.Fly           => "push",           // no standard "fly" element — push as fallback
        TransitionKind.Random        => "random",
        // ── Modern / Subtle ───────────────────────────────────────────────────────
        TransitionKind.Morph         => "morph",
        TransitionKind.Flash         => "flash",
        TransitionKind.Reveal        => "reveal",
        // ── Exciting / 3-D ───────────────────────────────────────────────────────
        TransitionKind.Cube          => "cube",
        TransitionKind.Box           => "box",
        TransitionKind.Rotate        => "rotate",
        TransitionKind.Flip          => "flip",
        TransitionKind.Gallery       => "gallery",
        TransitionKind.Conveyor      => "conveyor",
        TransitionKind.Ferris        => "ferris",
        TransitionKind.Flythrough    => "flythrough",
        TransitionKind.Switch        => "switch",
        TransitionKind.Orbit         => "orbit",
        TransitionKind.Doors         => "doors",
        TransitionKind.Window        => "window",
        TransitionKind.Pan           => "pan",
        TransitionKind.Honeycomb     => "honeycomb",
        TransitionKind.Comb          => "comb",
        TransitionKind.Glitter       => "glitter",
        TransitionKind.Vortex        => "vortex",
        TransitionKind.Shred         => "shred",
        TransitionKind.Wind          => "wind",
        TransitionKind.Ripple        => "ripple",
        TransitionKind.Warp          => "warp",
        TransitionKind.Fracture      => "fracture",
        TransitionKind.Crush         => "crush",
        TransitionKind.PeelOff       => "peelOff",
        TransitionKind.PageCurlDouble => "pageCurlDouble",
        TransitionKind.PageCurlSingle => "pageCurlSingle",
        TransitionKind.Airplane      => "airplane",
        TransitionKind.Origami       => "origami",
        TransitionKind.Prism         => "prism",
        TransitionKind.Curtains      => "curtains",
        TransitionKind.Drape         => "drape",
        TransitionKind.Prestige      => "prestige",
        TransitionKind.WheelReverse  => "wheelReverse",
        _                            => null              // None, Other, or unhandled → caller uses RawXml
    };

    /// <summary>
    /// Maps a PresentationML child element local-name to a <see cref="TransitionKind"/>.
    /// Returns <see cref="TransitionKind.Other"/> for unrecognized names so the caller
    /// can capture RawXml for a lossless round-trip.
    /// </summary>
    public static TransitionKind ElementNameToTransitionKind(string? name) => name switch
    {
        // ── Legacy ────────────────────────────────────────────────────────────────
        "fade"           => TransitionKind.Fade,
        "cut"            => TransitionKind.Cut,
        "push"           => TransitionKind.Push,
        "wipe"           => TransitionKind.Wipe,
        "cover"          => TransitionKind.Cover,
        "uncover"        => TransitionKind.Uncover,
        "split"          => TransitionKind.Split,
        "blinds"         => TransitionKind.Blinds,
        "dissolve"       => TransitionKind.Dissolve,
        "zoom"           => TransitionKind.Zoom,
        "wheel"          => TransitionKind.Wheel,
        "randomBar"      => TransitionKind.RandomBar,
        "strips"         => TransitionKind.Strips,
        "random"         => TransitionKind.Random,
        // ── Modern / Subtle ───────────────────────────────────────────────────────
        "morph"          => TransitionKind.Morph,
        "flash"          => TransitionKind.Flash,
        "reveal"         => TransitionKind.Reveal,
        // ── Exciting / 3-D ───────────────────────────────────────────────────────
        "cube"           => TransitionKind.Cube,
        "box"            => TransitionKind.Box,
        "rotate"         => TransitionKind.Rotate,
        "flip"           => TransitionKind.Flip,
        "gallery"        => TransitionKind.Gallery,
        "conveyor"       => TransitionKind.Conveyor,
        "ferris"         => TransitionKind.Ferris,
        "flythrough"     => TransitionKind.Flythrough,
        "switch"         => TransitionKind.Switch,
        "orbit"          => TransitionKind.Orbit,
        "doors"          => TransitionKind.Doors,
        "window"         => TransitionKind.Window,
        "pan"            => TransitionKind.Pan,
        "honeycomb"      => TransitionKind.Honeycomb,
        "comb"           => TransitionKind.Comb,
        "glitter"        => TransitionKind.Glitter,
        "vortex"         => TransitionKind.Vortex,
        "shred"          => TransitionKind.Shred,
        "wind"           => TransitionKind.Wind,
        "ripple"         => TransitionKind.Ripple,
        "warp"           => TransitionKind.Warp,
        "fracture"       => TransitionKind.Fracture,
        "crush"          => TransitionKind.Crush,
        "peelOff"        => TransitionKind.PeelOff,
        "pageCurlDouble" => TransitionKind.PageCurlDouble,
        "pageCurlSingle" => TransitionKind.PageCurlSingle,
        "airplane"       => TransitionKind.Airplane,
        "origami"        => TransitionKind.Origami,
        "prism"          => TransitionKind.Prism,
        "curtains"       => TransitionKind.Curtains,
        "drape"          => TransitionKind.Drape,
        "prestige"       => TransitionKind.Prestige,
        "wheelReverse"   => TransitionKind.WheelReverse,
        // ── Unrecognized → preserve via RawXml ───────────────────────────────────
        _                => TransitionKind.Other
    };

    // Direction attrs on directional transitions
    public static string? TransitionDirectionToAttr(TransitionDirection? d) => d switch
    {
        TransitionDirection.Left      => "l",
        TransitionDirection.Right     => "r",
        TransitionDirection.Up        => "u",
        TransitionDirection.Down      => "d",
        TransitionDirection.LeftUp    => "lu",
        TransitionDirection.LeftDown  => "ld",
        TransitionDirection.RightUp   => "ru",
        TransitionDirection.RightDown => "rd",
        TransitionDirection.Horizontal => "horz",
        TransitionDirection.Vertical   => "vert",
        TransitionDirection.In        => "in",
        TransitionDirection.Out       => "out",
        _                             => null
    };

    public static TransitionDirection? AttrToTransitionDirection(string? attr) => attr switch
    {
        "l"    => TransitionDirection.Left,
        "r"    => TransitionDirection.Right,
        "u"    => TransitionDirection.Up,
        "d"    => TransitionDirection.Down,
        "lu"   => TransitionDirection.LeftUp,
        "ld"   => TransitionDirection.LeftDown,
        "ru"   => TransitionDirection.RightUp,
        "rd"   => TransitionDirection.RightDown,
        "horz" => TransitionDirection.Horizontal,
        "vert" => TransitionDirection.Vertical,
        "in"   => TransitionDirection.In,
        "out"  => TransitionDirection.Out,
        _      => (TransitionDirection?)null
    };

    // DurationMs <-> spd string
    public static string DurationToSpd(int ms) => ms switch
    {
        <= 600  => "fast",
        <= 1000 => "med",
        _       => "slow"
    };

    public static int SpdToDuration(string? spd) => spd switch
    {
        "fast" => 500,
        "med"  => 750,
        "slow" => 1500,
        _      => 500
    };

    // ── Animation preset <-> (presetClass, presetID) ──────────────────────────────

    public static (string presetClass, int presetId) AnimationPresetToOoxml(AnimationPreset preset, AnimationKind kind)
    {
        string pc = kind switch
        {
            AnimationKind.Entrance  => "entr",
            AnimationKind.Exit      => "exit",
            AnimationKind.Emphasis  => "emph",
            _                       => "entr"
        };

        if (kind == AnimationKind.Emphasis)
        {
            int emphId = preset switch
            {
                AnimationPreset.Bold          => 1,
                AnimationPreset.Underline      => 2,
                AnimationPreset.Spin           => 8,
                AnimationPreset.Teeter         => 32,
                AnimationPreset.Grow           => 5,
                AnimationPreset.Shrink         => 5,
                AnimationPreset.ColorPulse     => 6,
                AnimationPreset.ChangeColor    => 7,
                AnimationPreset.ChangeFillColor => 1,
                AnimationPreset.Shimmer        => 36,
                AnimationPreset.GrowWithColor  => 12,
                AnimationPreset.Wave           => 34,
                AnimationPreset.Pulse          => 14,
                AnimationPreset.Blink          => 15,
                _                              => 14  // default to pulse
            };
            return (pc, emphId);
        }

        // Entrance / Exit share the same presetID table
        int id = preset switch
        {
            AnimationPreset.Appear     => 1,
            AnimationPreset.FlyIn      => 2,
            AnimationPreset.Split      => 3,
            AnimationPreset.Blinds     => 4,
            AnimationPreset.Box        => 5,
            AnimationPreset.Checkerboard => 6,
            AnimationPreset.Circle     => 7,
            AnimationPreset.Wipe       => 8,
            AnimationPreset.Diamond    => 9,
            AnimationPreset.Fade       => 10,
            AnimationPreset.Zoom       => 11,
            AnimationPreset.Plus       => 13,
            AnimationPreset.RandomBars => 14,
            AnimationPreset.Strips     => 16,
            AnimationPreset.Swivel     => 17,
            AnimationPreset.Wedge      => 18,
            AnimationPreset.Wheel      => 19,
            AnimationPreset.Bounce     => 21,
            AnimationPreset.Flash      => 22,
            AnimationPreset.Float      => 23,
            AnimationPreset.Swoop      => 24,
            AnimationPreset.Dissolve   => 25,
            AnimationPreset.Crawl      => 26,
            AnimationPreset.Peek       => 27,
            AnimationPreset.Spiral     => 28,
            AnimationPreset.Boomerang  => 29,
            _                          => 1   // default to Appear
        };
        return (pc, id);
    }

    public static (AnimationKind kind, AnimationPreset preset) OoxmlToAnimationPreset(string presetClass, int presetId)
    {
        var kind = presetClass switch
        {
            "entr" => AnimationKind.Entrance,
            "exit" => AnimationKind.Exit,
            "emph" => AnimationKind.Emphasis,
            _      => AnimationKind.Entrance
        };

        if (kind == AnimationKind.Emphasis)
        {
            var emphPreset = presetId switch
            {
                1  => AnimationPreset.Bold,
                2  => AnimationPreset.Underline,
                8  => AnimationPreset.Spin,
                32 => AnimationPreset.Teeter,
                5  => AnimationPreset.Grow,
                6  => AnimationPreset.ColorPulse,
                7  => AnimationPreset.ChangeColor,
                // PowerPoint ChangeFontColor emits the same animClr payload shape
                // as other color emphasis effects. Preserve its raw ID while
                // using the existing color-emphasis playback contract.
                3  => AnimationPreset.ChangeColor,
                // PowerPoint ChangeFontSize emits a numeric p:anim targeting
                // style.fontSize. Preserve that raw behavior while using the
                // existing amount-aware scale playback contract.
                4  => AnimationPreset.Grow,
                36 => AnimationPreset.Shimmer,
                12 => AnimationPreset.GrowWithColor,
                34 => AnimationPreset.Wave,
                14 => AnimationPreset.Pulse,
                15 => AnimationPreset.Blink,
                // PowerPoint FlashBulb and Flicker are not modeled as separate
                // authoring presets yet; keep their raw IDs for package fidelity
                // while using the closest existing visibility playback contract.
                26 => AnimationPreset.Blink,
                27 => AnimationPreset.Blink,
                // PowerPoint ColorWave is not modeled as a separate authoring
                // preset; retain its raw ID while using the existing color pulse
                // playback contract until wave-specific color timing is modeled.
                20 => AnimationPreset.ColorPulse,
                _  => AnimationPreset.Pulse
            };
            return (kind, emphPreset);
        }

        var p = presetId switch
        {
            1  => AnimationPreset.Appear,
            2  => AnimationPreset.FlyIn,
            3  => AnimationPreset.Split,
            4  => AnimationPreset.Blinds,
            5  => AnimationPreset.Box,
            6  => AnimationPreset.Checkerboard,
            7  => AnimationPreset.Circle,
            8  => AnimationPreset.Wipe,
            9  => AnimationPreset.Diamond,
            10 => AnimationPreset.Fade,
            11 => AnimationPreset.Zoom,
            13 => AnimationPreset.Plus,
            14 => AnimationPreset.RandomBars,
            16 => AnimationPreset.Strips,
            17 => AnimationPreset.Swivel,
            18 => AnimationPreset.Wedge,
            19 => AnimationPreset.Wheel,
            21 => AnimationPreset.Bounce,
            22 => AnimationPreset.Flash,
            23 => AnimationPreset.Float,
            24 => AnimationPreset.Swoop,
            25 => AnimationPreset.Dissolve,
            26 => AnimationPreset.Crawl,
            27 => AnimationPreset.Peek,
            28 => AnimationPreset.Spiral,
            29 => AnimationPreset.Boomerang,
            _  => AnimationPreset.Appear
        };
        return (kind, p);
    }

    /// <summary>
    /// Returns whether the OOXML preset pair has a faithful FreeP enum mapping.
    /// Unknown pairs still use a deterministic playback fallback, but callers
    /// can retain the source tokens for lossless package round-trip.
    /// </summary>
    internal static bool IsKnownOoxmlPreset(string? presetClass, int presetId)
    {
        if (presetClass is not ("entr" or "exit" or "emph"))
            return false;

        if (presetClass == "emph")
            return presetId is 1 or 2 or 5 or 6 or 7 or 8 or 12 or 14 or 15 or 32 or 34 or 36;

        return presetId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or
            13 or 14 or 16 or 17 or 18 or 19 or 21 or 22 or 23 or 24 or 25 or 26 or 27 or 28 or 29;
    }

    // Animation direction string (accel direction / subtype)
    public static string? AnimationDirectionToSubtype(AnimationDirection? d) => d switch
    {
        AnimationDirection.Left        => "left",
        AnimationDirection.Right       => "right",
        AnimationDirection.Up          => "top",
        AnimationDirection.Down        => "bottom",
        AnimationDirection.LeftUp      => "topLeft",
        AnimationDirection.LeftDown    => "bottomLeft",
        AnimationDirection.RightUp     => "topRight",
        AnimationDirection.RightDown   => "bottomRight",
        AnimationDirection.Horizontal  => "horizontal",
        AnimationDirection.Vertical    => "vertical",
        AnimationDirection.In          => "in",
        AnimationDirection.Out         => "out",
        AnimationDirection.FromLeft    => "fromLeft",
        AnimationDirection.FromRight   => "fromRight",
        AnimationDirection.FromTop     => "fromTop",
        AnimationDirection.FromBottom  => "fromBottom",
        AnimationDirection.FromTopLeft => "fromTopLeft",
        AnimationDirection.FromTopRight => "fromTopRight",
        AnimationDirection.FromBottomLeft => "fromBottomLeft",
        AnimationDirection.FromBottomRight => "fromBottomRight",
        AnimationDirection.HorizontalIn => "1",
        AnimationDirection.HorizontalOut => "0",
        AnimationDirection.VerticalIn => "3",
        AnimationDirection.VerticalOut => "2",
        _                              => null
    };

    public static AnimationDirection? SubtypeToAnimationDirection(string? s) => s switch
    {
        "left"            => AnimationDirection.Left,
        "right"           => AnimationDirection.Right,
        "top"             => AnimationDirection.Up,
        "bottom"          => AnimationDirection.Down,
        "topLeft"         => AnimationDirection.LeftUp,
        "bottomLeft"      => AnimationDirection.LeftDown,
        "topRight"        => AnimationDirection.RightUp,
        "bottomRight"     => AnimationDirection.RightDown,
        "horizontal"      => AnimationDirection.Horizontal,
        "vertical"        => AnimationDirection.Vertical,
        "in"              => AnimationDirection.In,
        "out"             => AnimationDirection.Out,
        "fromLeft"        => AnimationDirection.FromLeft,
        "fromRight"       => AnimationDirection.FromRight,
        "fromTop"         => AnimationDirection.FromTop,
        "fromBottom"      => AnimationDirection.FromBottom,
        "fromTopLeft"     => AnimationDirection.FromTopLeft,
        "fromTopRight"    => AnimationDirection.FromTopRight,
        "fromBottomLeft"  => AnimationDirection.FromBottomLeft,
        "fromBottomRight" => AnimationDirection.FromBottomRight,
        _                 => (AnimationDirection?)null
    };

    public static AnimationDirection? SubtypeToAnimationDirection(
        string? s,
        AnimationPreset preset) => preset == AnimationPreset.Split
            ? s switch
            {
                "0" => AnimationDirection.HorizontalOut,
                "1" => AnimationDirection.HorizontalIn,
                "2" => AnimationDirection.VerticalOut,
                "3" => AnimationDirection.VerticalIn,
                "horizontal" => AnimationDirection.Horizontal,
                "vertical" => AnimationDirection.Vertical,
                _ => SubtypeToAnimationDirection(s),
            }
            : SubtypeToAnimationDirection(s);
}
