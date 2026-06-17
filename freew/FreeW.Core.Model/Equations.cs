namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item W1, basic OMML equations):
// An equation is modelled as an OPTIONAL INLINE RUN MARK (Run.Equation) rather than a new block type.
// This mirrors how every other inline feature (images, footnote/endnote references, content controls,
// fields) is already carried on Run, so equations flow through the existing run sequence, hyperlink/
// comment/revision wrapping, table cells, headers and footers with zero new plumbing — and they
// round-trip through docx as an inline m:oMath emitted in place of the run's w:t. An equation is a flat,
// ordered list of MathRun parts; each part is either plain math text (m:r/m:t), a superscript
// (base^exponent, m:sSup) or a fraction (numerator/denominator, m:f). That covers the linear forms the
// roadmap calls out ("E = mc^2") while staying well short of the full OMML schema.

/// <summary>
/// The kind of an OMML math fragment carried by a <see cref="MathRun"/>. <see cref="Text"/> is a plain
/// run of math text (m:r/m:t); <see cref="Superscript"/> is a base raised to an exponent (m:sSup);
/// <see cref="Fraction"/> is a numerator over a denominator (m:f).
/// </summary>
public enum MathRunKind
{
    Text,
    Superscript,
    Fraction
}

/// <summary>
/// One fragment of an <see cref="Equation"/>. A <see cref="MathRunKind.Text"/> part carries its content
/// in <see cref="Text"/>. A <see cref="MathRunKind.Superscript"/> part raises <see cref="Base"/> to
/// <see cref="Sup"/> (e.g. base "x", sup "2" → x²). A <see cref="MathRunKind.Fraction"/> part divides
/// <see cref="Numerator"/> by <see cref="Denominator"/>. Kept deliberately small and immutable so it
/// round-trips cleanly and so consumers can pattern-match on <see cref="Kind"/>.
/// </summary>
public sealed record MathRun
{
    /// <summary>The fragment kind (plain text, superscript, or fraction).</summary>
    public MathRunKind Kind { get; init; } = MathRunKind.Text;

    /// <summary>Plain math text (only meaningful when <see cref="Kind"/> is <see cref="MathRunKind.Text"/>).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The base of a superscript (only meaningful for <see cref="MathRunKind.Superscript"/>).</summary>
    public string Base { get; init; } = string.Empty;

    /// <summary>The exponent of a superscript (only meaningful for <see cref="MathRunKind.Superscript"/>).</summary>
    public string Sup { get; init; } = string.Empty;

    /// <summary>The numerator of a fraction (only meaningful for <see cref="MathRunKind.Fraction"/>).</summary>
    public string Numerator { get; init; } = string.Empty;

    /// <summary>The denominator of a fraction (only meaningful for <see cref="MathRunKind.Fraction"/>).</summary>
    public string Denominator { get; init; } = string.Empty;

    /// <summary>Creates a plain math-text fragment (m:r/m:t).</summary>
    public static MathRun PlainText(string text) => new() { Kind = MathRunKind.Text, Text = text };

    /// <summary>Creates a superscript fragment (m:sSup): <paramref name="@base"/> raised to <paramref name="sup"/>.</summary>
    public static MathRun Superscript(string @base, string sup) =>
        new() { Kind = MathRunKind.Superscript, Base = @base, Sup = sup };

    /// <summary>Creates a fraction fragment (m:f): <paramref name="numerator"/> over <paramref name="denominator"/>.</summary>
    public static MathRun Fraction(string numerator, string denominator) =>
        new() { Kind = MathRunKind.Fraction, Numerator = numerator, Denominator = denominator };

    /// <summary>
    /// A best-effort linear (plain-text) rendering of this fragment, used for the host run's fallback
    /// text: text → itself, superscript → <c>base^sup</c>, fraction → <c>num/den</c>.
    /// </summary>
    public string LinearText => Kind switch
    {
        MathRunKind.Superscript => $"{Base}^{Sup}",
        MathRunKind.Fraction => $"{Numerator}/{Denominator}",
        _ => Text
    };
}

/// <summary>
/// A basic inline mathematical equation: an ordered list of <see cref="MathRun"/> fragments that maps onto
/// an OMML <c>m:oMath</c>. Carried by a <see cref="Run"/> via <see cref="Run.Equation"/>. Stores only the
/// minimal OMML subset FreeW round-trips (plain text, superscript, fraction); richer structures degrade to
/// plain math text on read so nothing throws.
/// </summary>
public sealed class Equation
{
    /// <summary>The ordered math fragments making up the equation (left to right).</summary>
    public List<MathRun> Runs { get; } = [];

    public Equation() { }

    /// <summary>Creates an equation from an ordered set of fragments.</summary>
    public Equation(IEnumerable<MathRun> runs) => Runs.AddRange(runs);

    /// <summary>Convenience: a single-fragment plain-text equation (e.g. "x + 1").</summary>
    public static Equation FromText(string text) => new([MathRun.PlainText(text)]);

    /// <summary>A best-effort linear (plain-text) rendering of the whole equation (fragments concatenated).</summary>
    public string LinearText => string.Concat(Runs.Select(r => r.LinearText));
}
