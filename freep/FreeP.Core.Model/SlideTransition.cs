using System;
using System.Collections.Generic;

namespace FreeP.Core.Model;

/// <summary>
/// Specifies the visual transition played when this slide enters during a slideshow.
/// Maps to the <c>p:transition</c> element in PresentationML.
/// </summary>
public sealed class SlideTransition
{
    /// <summary>The transition effect kind.</summary>
    public TransitionKind Kind { get; set; } = TransitionKind.None;

    /// <summary>
    /// Direction modifier used by directional transitions (Push, Wipe, Cover, etc.).
    /// Null for non-directional effects (Fade, Cut, Dissolve, …).
    /// </summary>
    public TransitionDirection? Direction { get; set; }

    /// <summary>
    /// Axis used by the split transition. PowerPoint stores this separately
    /// from the in/out direction as the <c>orient</c> attribute.
    /// </summary>
    public TransitionDirection? SplitOrientation { get; set; }

    /// <summary>
    /// Duration of the transition animation in milliseconds.
    /// Corresponds to <c>spd</c> (slow≈1500/med≈750/fast≈500) or a <c>dur</c> attribute in newer schemas.
    /// Default 500 ms maps to <c>fast</c>.
    /// </summary>
    public int DurationMs { get; set; } = 500;

    /// <summary>
    /// Whether a mouse click advances to the next slide.
    /// Corresponds to absence/presence of <c>advClick="0"</c> on p:transition (default is true/click advances).
    /// </summary>
    public bool AdvanceOnClick { get; set; } = true;

    /// <summary>
    /// If non-null, the slide automatically advances after this many milliseconds.
    /// Corresponds to <c>advTm</c> on p:transition.
    /// </summary>
    public int? AdvanceAfterMs { get; set; }

    /// <summary>
    /// Verbatim XML of the entire <c>p:transition</c> element (without the
    /// mc:AlternateContent wrapper, if any).  Populated for <see cref="TransitionKind.Other"/>
    /// transitions (unrecognized child element) and optionally for known kinds when the
    /// original file contained extra attributes/children we don't model.
    /// When non-null the writer re-emits this XML verbatim so PowerPoint re-opens the
    /// exact transition without loss — this is the guarantee that NO transition is silently dropped.
    /// For known kinds RawXml is null; the writer synthesizes the element from the structured fields.
    /// </summary>
    public string? RawXml { get; set; }

    /// <summary>
    /// Whether the original <c>p:transition</c> (captured in <see cref="RawXml"/>) was wrapped in an
    /// <c>mc:AlternateContent</c>/<c>mc:Choice</c>/<c>mc:Fallback</c> block on read. When true, the
    /// writer must re-wrap <see cref="RawXml"/> the same way instead of emitting it as a bare
    /// <c>p:transition</c>, or an unrecognized extension effect ends up as an invalid direct child
    /// of <c>p:transition</c> and the original <c>mc:Fallback</c> degrade-path content is lost.
    /// </summary>
    public bool WasAlternateContent { get; set; }

    /// <summary>
    /// The original <c>mc:Choice</c> <c>Requires</c> attribute value (possibly a space-separated
    /// list of tokens, e.g. "p14 p159"). Null when <see cref="WasAlternateContent"/> is false.
    /// </summary>
    public string? McRequiresToken { get; set; }

    /// <summary>
    /// Namespace URI for each token in <see cref="McRequiresToken"/>, resolved from the source
    /// document's xmlns scope at read time (e.g. "p188" -&gt; the PowerPoint 2018/8 namespace).
    /// A token with no entry here had no resolvable xmlns binding in the source.
    /// </summary>
    public Dictionary<string, string> McRequiresNsUris { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Verbatim XML of the original <c>mc:Fallback</c> <c>p:transition</c> element, when
    /// <see cref="WasAlternateContent"/> is true and a Fallback was present. Null if there was
    /// no Fallback (the writer synthesizes a fade fallback in that case).
    /// </summary>
    public string? AlternateContentFallbackXml { get; set; }

    /// <summary>
    /// Option for <see cref="TransitionKind.Morph"/>: "byWord", "byChar", or "byObject" (default null).
    /// Maps to the <c>option</c> attribute on the <c>p:morph</c> child element.
    /// </summary>
    public string? MorphOption { get; set; }

    /// <summary>
    /// Optional PowerPoint wheel spoke count. The classic <c>p:wheel</c>
    /// transition stores this as the <c>spokes</c> attribute; when omitted,
    /// the slideshow uses PowerPoint's default spoke count.
    /// </summary>
    public int? WheelSpokeCount { get; set; }

    /// <summary>
    /// Transition sound (p:sndAc / p:stSnd). Null if no sound is attached.
    /// </summary>
    public TransitionSound? Sound { get; set; }
}

/// <summary>
/// Transition sound descriptor, corresponding to <c>p:sndAc &gt; p:stSnd</c>.
/// </summary>
public sealed class TransitionSound
{
    /// <summary>Raw embedded audio bytes (from the referenced audio part). Null if the part could not be resolved.</summary>
    public byte[]? AudioBytes { get; set; }

    /// <summary>Content-type of the embedded audio (e.g. "audio/mpeg"). Null if unknown.</summary>
    public string? ContentType { get; set; }

    /// <summary>Relationship ID used when the sound references an audio part in the slide package.</summary>
    public string? RelId { get; set; }

    /// <summary>Original audio part path inside the ZIP (for re-embedding on write). Null if resolved from bytes.</summary>
    public string? PartPath { get; set; }

    /// <summary>Whether the sound loops (snd loop="1" attribute).</summary>
    public bool Loop { get; set; }

    /// <summary>Whether the sound should be played on entering the slide (default) or something else.</summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>Identifies the transition effect element name in PresentationML.</summary>
public enum TransitionKind
{
    // ── Legacy / widely-supported ────────────────────────────────────────────────
    None,
    Fade,
    Cut,
    Push,
    Wipe,
    Cover,
    Uncover,
    Split,
    Blinds,
    Dissolve,
    Zoom,
    Wheel,
    RandomBar,
    Strips,
    Fly,
    Random,

    // ── Modern / "Subtle" set ────────────────────────────────────────────────────
    /// <summary>p:morph — morph transition (PowerPoint 2016+)</summary>
    Morph,
    /// <summary>p:flash — flash white</summary>
    Flash,
    /// <summary>p:reveal — reveal from edge</summary>
    Reveal,

    // ── "Exciting" 3-D set ───────────────────────────────────────────────────────
    /// <summary>p:cube — 3-D cube rotation</summary>
    Cube,
    /// <summary>p:box — box in/out</summary>
    Box,
    /// <summary>p:rotate — rotate</summary>
    Rotate,
    /// <summary>p:flip — flip card</summary>
    Flip,
    /// <summary>p:gallery — gallery</summary>
    Gallery,
    /// <summary>p:conveyor — conveyor belt</summary>
    Conveyor,
    /// <summary>p:ferris — ferris wheel</summary>
    Ferris,
    /// <summary>p:flythrough — fly through</summary>
    Flythrough,
    /// <summary>p:switch — light switch</summary>
    Switch,
    /// <summary>p:orbit — orbit</summary>
    Orbit,
    /// <summary>p:doors — doors</summary>
    Doors,
    /// <summary>p:window — window</summary>
    Window,
    /// <summary>p:pan — pan</summary>
    Pan,
    /// <summary>p:honeycomb — honeycomb</summary>
    Honeycomb,
    /// <summary>p:comb — comb wipe</summary>
    Comb,
    /// <summary>p:glitter — glitter</summary>
    Glitter,
    /// <summary>p:vortex — vortex swirl</summary>
    Vortex,
    /// <summary>p:shred — shred</summary>
    Shred,
    /// <summary>p:wind — wind</summary>
    Wind,
    /// <summary>p:ripple — ripple</summary>
    Ripple,
    /// <summary>p:warp — warp</summary>
    Warp,
    /// <summary>p:fracture — fracture</summary>
    Fracture,
    /// <summary>p:crush — crush</summary>
    Crush,
    /// <summary>p:peelOff — peel off</summary>
    PeelOff,
    /// <summary>p:pageCurlDouble — page curl (double)</summary>
    PageCurlDouble,
    /// <summary>p:pageCurlSingle — page curl (single)</summary>
    PageCurlSingle,
    /// <summary>p:airplane — airplane</summary>
    Airplane,
    /// <summary>p:origami — origami</summary>
    Origami,
    /// <summary>p:prism — prism</summary>
    Prism,
    /// <summary>p:curtains — curtains</summary>
    Curtains,
    /// <summary>p:drape — drape</summary>
    Drape,
    /// <summary>p:prestige — prestige</summary>
    Prestige,
    /// <summary>p:wheelReverse — reverse wheel</summary>
    WheelReverse,

    // ── Catch-all ────────────────────────────────────────────────────────────────
    /// <summary>
    /// An unrecognized or extension transition element. RawXml carries the verbatim XML
    /// to guarantee lossless round-trip; slideshow falls back to Fade.
    /// </summary>
    Other,
}

/// <summary>
/// Direction modifier for transitions that accept a directional argument.
/// Maps to attributes like <c>dir</c> on child elements such as <c>p:push</c>, <c>p:wipe</c>, etc.
/// </summary>
public enum TransitionDirection
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
}
