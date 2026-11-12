namespace FreeW.Core.Model;

/// <summary>
/// Word 2010+ text effects on a run (Home &gt; Text Effects and Typography): the <c>w14:</c> run-property
/// elements that have no slot in the base <c>w:rPr</c> schema. Each member holds one effect element
/// exactly as it appears in WordprocessingML (its outer XML, e.g. <c>&lt;w14:glow w14:rad="63500"
/// xmlns:w14="…"&gt;…&lt;/w14:glow&gt;</c>), or null when the run does not carry that effect.
/// <para>
/// Keeping the source XML is what makes the effects lossless: Word's effect vocabulary (scheme colours
/// with colour transforms, gradient stops, bevels, 3-D scenes) is far wider than anything a renderer
/// draws, so a typed projection would silently narrow a document on save. Strings also give the record
/// value equality, which <see cref="RunFormatting"/> relies on to merge and compare runs.
/// </para>
/// </summary>
public sealed record RunTextEffects
{
    /// <summary>The WordprocessingML 2010 namespace every effect element belongs to.</summary>
    public const string Namespace = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary><c>w14:glow</c> — a coloured halo of a given radius.</summary>
    public string? Glow { get; init; }

    /// <summary><c>w14:shadow</c> — an outer shadow (blur, distance, direction, colour).</summary>
    public string? Shadow { get; init; }

    /// <summary><c>w14:reflection</c> — a mirrored, fading copy below the text.</summary>
    public string? Reflection { get; init; }

    /// <summary><c>w14:textOutline</c> — the glyph outline (width, dash, fill).</summary>
    public string? Outline { get; init; }

    /// <summary><c>w14:textFill</c> — a solid or gradient glyph fill.</summary>
    public string? Fill { get; init; }

    /// <summary><c>w14:scene3d</c> — camera and light rig for 3-D text.</summary>
    public string? Scene3D { get; init; }

    /// <summary><c>w14:props3d</c> — bevel, extrusion and material for 3-D text.</summary>
    public string? Props3D { get; init; }

    /// <summary><c>w14:cntxtAlts</c> — the OpenType contextual-alternates toggle.</summary>
    public string? ContextualAlternates { get; init; }

    /// <summary>True when no effect is carried, in which case the owning run should hold null instead.</summary>
    public bool IsEmpty =>
        Glow is null && Shadow is null && Reflection is null && Outline is null && Fill is null
        && Scene3D is null && Props3D is null && ContextualAlternates is null;

    /// <summary>
    /// Word's run-property inheritance for effects: each effect is inherited independently, so a
    /// directly applied effect replaces only the same effect from the style or document default.
    /// </summary>
    public static RunTextEffects? Merge(RunTextEffects? direct, RunTextEffects? inherited)
    {
        if (direct is null)
            return inherited;
        if (inherited is null)
            return direct;

        return new RunTextEffects
        {
            Glow = direct.Glow ?? inherited.Glow,
            Shadow = direct.Shadow ?? inherited.Shadow,
            Reflection = direct.Reflection ?? inherited.Reflection,
            Outline = direct.Outline ?? inherited.Outline,
            Fill = direct.Fill ?? inherited.Fill,
            Scene3D = direct.Scene3D ?? inherited.Scene3D,
            Props3D = direct.Props3D ?? inherited.Props3D,
            ContextualAlternates = direct.ContextualAlternates ?? inherited.ContextualAlternates,
        };
    }
}
