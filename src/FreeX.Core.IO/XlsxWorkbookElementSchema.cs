using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Declarative attribute-normalization schema for a leaf workbook.xml element — one that has no
/// meaningful child elements (only text content and attributes) and whose full sanitization can be
/// expressed as: remove disallowed attributes + normalize each allowed attribute via a per-attribute
/// rule.
///
/// <para><b>How to add a new workbook-level leaf element:</b> add one
/// <c>XlsxWorkbookLeafElementSchema</c> entry to
/// <see cref="XlsxWorkbookLeafElementSchemas.All"/> with:
/// <list type="number">
///   <item><description>The element's local name.</description></item>
///   <item><description>All allowed (no-namespace) attribute local names in
///   <c>AllowedAttributes</c>.</description></item>
///   <item><description>One <c>AttributeRule</c> per attribute that needs value
///   validation/canonicalization. Omit an attribute from <c>AttributeRules</c> to treat it as
///   pass-through-text (the attribute is kept verbatim as long as its name is in
///   <c>AllowedAttributes</c>).</description></item>
/// </list>
/// The generic normalizer in <see cref="XlsxWorkbookLeafElementNormalizer"/> then drives
/// a single-pass normalize on the element.</para>
///
/// <para><b>Not suitable for</b> elements that have child elements requiring their own
/// normalization (e.g. <c>bookViews</c>, <c>definedNames</c>, <c>functionGroups</c>) —
/// those remain as dedicated normalizer classes.</para>
/// </summary>
internal sealed class XlsxWorkbookLeafElementSchema
{
    /// <summary>Local name of the workbook.xml child element covered by this schema row.</summary>
    public required string LocalName { get; init; }

    /// <summary>
    /// The set of allowed no-namespace attribute local names. Any attribute not in this set
    /// (and not a namespace declaration) is removed.
    /// </summary>
    public required IReadOnlySet<string> AllowedAttributes { get; init; }

    /// <summary>
    /// Per-attribute normalization rules. Keys are attribute local names (must be a subset of
    /// <see cref="AllowedAttributes"/>). The value function receives the raw attribute value
    /// (or <c>null</c> if absent) and returns the canonical value, or <c>null</c> to remove
    /// the attribute. Attributes in <see cref="AllowedAttributes"/> but absent from
    /// <see cref="AttributeRules"/> are kept verbatim.
    /// </summary>
    public IReadOnlyDictionary<string, Func<string?, string?>> AttributeRules { get; init; }
        = new Dictionary<string, Func<string?, string?>>();

    /// <summary>
    /// When <c>true</c>, all child nodes (text nodes, child elements, etc.) are removed
    /// during normalization. Default is <c>true</c> because workbook.xml leaf elements are
    /// schema-empty content types.
    /// </summary>
    public bool RemoveAllChildNodes { get; init; } = true;
}

/// <summary>
/// Generic single-pass normalizer driven by <see cref="XlsxWorkbookLeafElementSchema"/>.
/// </summary>
internal static class XlsxWorkbookLeafElementNormalizer
{
    /// <summary>
    /// Normalizes <paramref name="element"/> according to <paramref name="schema"/>:
    /// removes disallowed attributes, applies per-attribute value rules, optionally removes
    /// child nodes. Returns <c>true</c> if any change was made.
    /// </summary>
    public static bool Normalize(XElement element, XlsxWorkbookLeafElementSchema schema)
    {
        var changed = false;

        if (schema.RemoveAllChildNodes)
            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(element);

        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(element, schema.AllowedAttributes);

        foreach (var (attributeName, rule) in schema.AttributeRules)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(element, attributeName, rule);

        return changed;
    }
}
