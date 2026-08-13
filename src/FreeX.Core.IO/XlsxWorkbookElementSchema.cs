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

    /// <summary>
    /// Attribute local names that must be present and valid after normalization.
    /// When any required attribute is absent (or was removed by its rule), the element itself
    /// is removed from the document. An empty set (the default) means the element is always kept.
    /// </summary>
    public IReadOnlySet<string> RequiredAttributes { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Generic single-pass normalizer driven by <see cref="XlsxWorkbookLeafElementSchema"/>.
/// </summary>
internal static class XlsxWorkbookLeafElementNormalizer
{
    /// <summary>
    /// Normalizes a known workbook leaf element using the schema registered for its local name.
    /// </summary>
    public static bool Normalize(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!XlsxWorkbookLeafElementSchemas.ByLocalName.TryGetValue(
                element.Name.LocalName,
                out var schema))
        {
            throw new InvalidOperationException(
                $"No workbook leaf-element schema is registered for '{element.Name.LocalName}'.");
        }

        return Normalize(element, schema);
    }

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

    /// <summary>
    /// Returns <c>true</c> when the element should be removed from its parent because one
    /// or more <see cref="XlsxWorkbookLeafElementSchema.RequiredAttributes"/> are absent or
    /// were nulled out by their attribute rule. Only meaningful when
    /// <see cref="XlsxWorkbookLeafElementSchema.RequiredAttributes"/> is non-empty.
    /// </summary>
    public static bool ShouldRemove(XElement element, XlsxWorkbookLeafElementSchema schema)
    {
        if (schema.RequiredAttributes.Count == 0)
            return false;

        foreach (var required in schema.RequiredAttributes)
        {
            if (element.Attribute(required) is null)
                return true;
        }

        return false;
    }
}
