namespace FreeP.Core.Model;

// ── OMML math model (Theme 21) ────────────────────────────────────────────────────────────────

/// <summary>
/// Payload for an OMML math equation embedded inside a paragraph run.
///
/// In OOXML a math equation in a slide text body appears as either:
///   - An <c>a14:m</c> child of <c>a:p</c>, containing the <c>m:oMath</c> element directly, or
///   - An <c>mc:AlternateContent</c> with the <c>m:oMathPara</c> / <c>m:oMath</c> in
///     <c>mc:Choice</c> and a simple text run in <c>mc:Fallback</c>.
///
/// FreeP preserves the raw OMML XML verbatim for byte-faithful round-trip so that PowerPoint
/// can still display and edit the equation.  The <c>Run.Text</c> on the owning run carries the
/// flattened fallback plain text (all <c>m:t</c> values concatenated) which is used for
/// rendering in FreeP's compositor.
///
/// A full OMML math-layout engine is OUT OF SCOPE (deferred); the fallback plain text is the
/// visual floor.
/// </summary>
public sealed class MathRunInfo
{
    /// <summary>
    /// The raw XML of the math element as it should be re-emitted on write.
    ///
    /// For the <c>a14:m</c> form: the outer <c>a14:m</c> element including its namespace
    /// declarations and the nested <c>m:oMath</c> child, serialized to a string.
    ///
    /// For the <c>mc:AlternateContent</c> form: the full <c>mc:AlternateContent</c>
    /// element (mc:Choice + mc:Fallback), serialized to a string.
    ///
    /// Re-emitted verbatim by the writer inside the <c>a:p</c> element.
    /// </summary>
    public string RawXml { get; set; } = string.Empty;

    /// <summary>
    /// True when the original paragraph used the <c>mc:AlternateContent</c> wrapper form.
    /// False for the compact <c>a14:m</c> form (both are valid per the spec).
    /// Stored so the writer can re-emit the correct wrapper structure.
    /// </summary>
    public bool IsAlternateContent { get; set; }

    /// <summary>
    /// Authored <c>m:mathPr</c> from the containing <c>a:graphicData</c>, when
    /// the package places the equation in that wrapper. This is below package
    /// defaults and below any <c>m:mathPr</c> carried by <see cref="RawXml"/>.
    /// </summary>
    public OmmlMathProperties? ContainingProperties { get; set; }
}
