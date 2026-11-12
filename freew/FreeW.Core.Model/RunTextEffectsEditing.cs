namespace FreeW.Core.Model;

/// <summary>
/// Typed get/set over a run's <see cref="RunTextEffects"/>. Every setter returns a new value and
/// collapses an effect-free result to null, which is how <see cref="RunFormatting.TextEffects"/>
/// represents "no effects"; effects the caller does not touch (including 3-D and any the typed layer
/// cannot interpret) are carried through verbatim.
/// </summary>
public static class RunTextEffectsEditing
{
    public static TextGlowEffect? GetGlow(this RunTextEffects? effects) => RunTextEffectsCodec.ReadGlow(effects?.Glow);

    public static TextShadowEffect? GetShadow(this RunTextEffects? effects) => RunTextEffectsCodec.ReadShadow(effects?.Shadow);

    public static TextReflectionEffect? GetReflection(this RunTextEffects? effects) =>
        RunTextEffectsCodec.ReadReflection(effects?.Reflection);

    public static TextOutlineEffect? GetOutline(this RunTextEffects? effects) => RunTextEffectsCodec.ReadOutline(effects?.Outline);

    public static TextFillEffect? GetFill(this RunTextEffects? effects) => RunTextEffectsCodec.ReadFill(effects?.Fill);

    public static RunTextEffects? WithGlow(this RunTextEffects? effects, TextGlowEffect? glow) =>
        Normalize((effects ?? new RunTextEffects()) with { Glow = glow is null ? null : RunTextEffectsCodec.WriteGlow(glow) });

    public static RunTextEffects? WithShadow(this RunTextEffects? effects, TextShadowEffect? shadow) =>
        Normalize((effects ?? new RunTextEffects()) with { Shadow = shadow is null ? null : RunTextEffectsCodec.WriteShadow(shadow) });

    public static RunTextEffects? WithReflection(this RunTextEffects? effects, TextReflectionEffect? reflection) =>
        Normalize((effects ?? new RunTextEffects()) with
        {
            Reflection = reflection is null ? null : RunTextEffectsCodec.WriteReflection(reflection),
        });

    public static RunTextEffects? WithOutline(this RunTextEffects? effects, TextOutlineEffect? outline) =>
        Normalize((effects ?? new RunTextEffects()) with { Outline = outline is null ? null : RunTextEffectsCodec.WriteOutline(outline) });

    public static RunTextEffects? WithFill(this RunTextEffects? effects, TextFillEffect? fill) =>
        Normalize((effects ?? new RunTextEffects()) with { Fill = fill is null ? null : RunTextEffectsCodec.WriteFill(fill) });

    /// <summary>
    /// Word's "Clear Text Effects": removes glow, shadow, reflection, outline, fill and the 3-D pair.
    /// The OpenType contextual-alternates toggle is typography, not an effect, and is kept.
    /// </summary>
    public static RunTextEffects? ClearVisualEffects(this RunTextEffects? effects) =>
        effects is null
            ? null
            : Normalize(new RunTextEffects { ContextualAlternates = effects.ContextualAlternates });

    /// <summary>True when the run carries any visual effect (everything except contextual alternates).</summary>
    public static bool HasVisualEffects(this RunTextEffects? effects) =>
        effects is not null
        && (effects.Glow is not null || effects.Shadow is not null || effects.Reflection is not null
            || effects.Outline is not null || effects.Fill is not null
            || effects.Scene3D is not null || effects.Props3D is not null);

    private static RunTextEffects? Normalize(RunTextEffects effects) => effects.IsEmpty ? null : effects;
}
