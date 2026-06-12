using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Declarative schema for a workbook.xml container element — one whose children are a
/// homogeneous collection of a single child element type (e.g. <c>definedNames</c> /
/// <c>definedName</c>, <c>externalReferences</c> / <c>externalReference</c>).
///
/// <para>The generic driver <see cref="XlsxWorkbookContainerElementNormalizer"/> applies:
/// strip unknown container attributes → iterate children → normalize each child (strip attrs +
/// apply value rules) → prune children that fail required-attribute or validator checks →
/// dedup by key selectors → optionally remove the container when empty → call PostProcess.</para>
/// </summary>
internal sealed class XlsxWorkbookContainerElementSchema
{
    /// <summary>Local name of the container element (e.g. <c>"definedNames"</c>).</summary>
    public required string LocalName { get; init; }

    /// <summary>
    /// Allowed no-namespace attribute local names on the container itself.
    /// Any attribute not in this set is removed.
    /// </summary>
    public IReadOnlySet<string> AllowedAttributes { get; init; }
        = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Per-attribute normalization rules for the container element itself
    /// (keys are attribute local names in <see cref="AllowedAttributes"/>).
    /// </summary>
    public IReadOnlyDictionary<string, Func<string?, string?>> ContainerAttributeRules { get; init; }
        = new Dictionary<string, Func<string?, string?>>();

    /// <summary>Schema that governs each child element.</summary>
    public required XlsxWorkbookChildElementSchema ChildSchema { get; init; }

    /// <summary>
    /// Key selectors for dedup; the child is pruned when any selector produces a value that
    /// was already seen in that selector's own seen-set. <c>null</c>-returning selectors are
    /// ignored (the child is not deduped for that key). An empty list means no dedup.
    /// </summary>
    public IReadOnlyList<Func<XElement, string?>> DedupKeySelectors { get; init; } = [];

    /// <summary>
    /// When <c>true</c>, the container element is removed from the document after normalization
    /// if it has no remaining child elements. Default is <c>true</c>.
    /// </summary>
    public bool RemoveSelfIfEmpty { get; init; } = true;

    /// <summary>
    /// Optional post-processing step called after all children have been processed.
    /// Receives the (possibly modified) container element and the workbook namespace.
    /// </summary>
    public Action<XElement, XNamespace>? PostProcess { get; init; }
}

/// <summary>
/// Schema describing one child element type inside a container element.
/// </summary>
internal sealed class XlsxWorkbookChildElementSchema
{
    /// <summary>Local name of the child element (e.g. <c>"definedName"</c>).</summary>
    public required string LocalName { get; init; }

    /// <summary>
    /// Allowed no-namespace attribute local names on the child element.
    /// Any attribute not in this set (and not a known namespaced attribute) is removed.
    /// </summary>
    public IReadOnlySet<string> AllowedAttributes { get; init; }
        = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Additional allowed attributes identified by their full <see cref="XName"/>
    /// (i.e. namespaced attributes such as <c>r:id</c>).
    /// </summary>
    public IReadOnlyList<XName> AllowedNamespacedAttributes { get; init; } = [];

    /// <summary>
    /// Attribute local names that must be present (and non-null after their rule fires) for
    /// the child to be kept. If any required attribute is absent, the child is pruned.
    /// </summary>
    public IReadOnlySet<string> RequiredAttributes { get; init; }
        = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Required namespaced attributes (full <see cref="XName"/>). If any is absent, the child
    /// is pruned.
    /// </summary>
    public IReadOnlyList<XName> RequiredNamespacedAttributes { get; init; } = [];

    /// <summary>
    /// Per no-namespace attribute normalization rules.
    /// Keys are attribute local names (must be a subset of <see cref="AllowedAttributes"/>).
    /// A rule returning <c>null</c> removes the attribute.
    /// </summary>
    public IReadOnlyDictionary<string, Func<string?, string?>> AttributeRules { get; init; }
        = new Dictionary<string, Func<string?, string?>>();

    /// <summary>
    /// Per namespaced attribute normalization rules, keyed by full <see cref="XName"/>.
    /// A rule returning <c>null</c> removes the attribute.
    /// </summary>
    public IReadOnlyDictionary<XName, Func<string?, string?>> NamespacedAttributeRules { get; init; }
        = new Dictionary<XName, Func<string?, string?>>();

    /// <summary>
    /// When <c>true</c> (the default), all child nodes (text, elements, etc.) of the child
    /// element are removed.
    /// </summary>
    public bool RemoveAllChildNodes { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, only child <em>elements</em> are removed (text content is preserved).
    /// Ignored when <see cref="RemoveAllChildNodes"/> is <c>true</c>.
    /// </summary>
    public bool RemoveChildElementsOnly { get; init; } = false;

    /// <summary>
    /// Optional extra validator called after attribute normalization. The child is pruned when
    /// this returns <c>false</c>.
    /// </summary>
    public Func<XElement, bool>? ChildValidator { get; init; }
}

/// <summary>
/// Generic driver for <see cref="XlsxWorkbookContainerElementSchema"/>.
/// Applies: strip container attrs → process children → dedup → remove-if-empty → post-process.
/// </summary>
internal static class XlsxWorkbookContainerElementNormalizer
{
    /// <summary>
    /// Normalizes a single container element in-place. Returns <c>true</c> if any change was
    /// made. Does NOT remove the container from its parent — that decision (and the
    /// <see cref="XlsxWorkbookContainerElementSchema.RemoveSelfIfEmpty"/> check) is handled by
    /// <see cref="NormalizeWorkbookRoot"/>.
    /// </summary>
    public static bool Normalize(
        XElement container,
        XlsxWorkbookContainerElementSchema schema,
        XNamespace workbookNs)
    {
        var changed = false;

        // Strip unknown container attributes.
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(container, schema.AllowedAttributes);

        // Apply per-attribute rules on the container itself (e.g. builtInGroupCount, count).
        foreach (var (attrName, rule) in schema.ContainerAttributeRules)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(container, attrName, rule);

        var childSchema = schema.ChildSchema;
        var expectedChildName = workbookNs + childSchema.LocalName;

        // Remove children that are not the expected child element.
        foreach (var unexpected in container.Elements()
                     .Where(e => e.Name != expectedChildName)
                     .ToList())
        {
            unexpected.Remove();
            changed = true;
        }

        // Build dedup state: one HashSet per DedupKeySelector.
        var dedupSets = schema.DedupKeySelectors.Count > 0
            ? schema.DedupKeySelectors.Select(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)).ToList()
            : null;

        // Process each child.
        foreach (var child in container.Elements(expectedChildName).ToList())
        {
            changed |= NormalizeChild(child, childSchema);

            // Check required no-namespace attributes.
            var pruned = false;
            foreach (var req in childSchema.RequiredAttributes)
            {
                if (child.Attribute(req) is null)
                {
                    child.Remove();
                    changed = true;
                    pruned = true;
                    break;
                }
            }

            if (!pruned)
            {
                // Check required namespaced attributes.
                foreach (var req in childSchema.RequiredNamespacedAttributes)
                {
                    if (child.Attribute(req) is null)
                    {
                        child.Remove();
                        changed = true;
                        pruned = true;
                        break;
                    }
                }
            }

            if (!pruned && childSchema.ChildValidator is not null && !childSchema.ChildValidator(child))
            {
                child.Remove();
                changed = true;
                pruned = true;
            }

            if (!pruned && dedupSets is not null)
            {
                // Prune if any dedup key was already seen.
                for (var i = 0; i < schema.DedupKeySelectors.Count; i++)
                {
                    var key = schema.DedupKeySelectors[i](child);
                    if (key is not null && !dedupSets[i].Add(key))
                    {
                        child.Remove();
                        changed = true;
                        break;
                    }
                }
            }
        }

        schema.PostProcess?.Invoke(container, workbookNs);

        return changed;
    }

    /// <summary>
    /// Iterates all occurrences of the container element in the workbook root, normalizes each
    /// one, removes containers that are empty (when
    /// <see cref="XlsxWorkbookContainerElementSchema.RemoveSelfIfEmpty"/> is set), and deduplicates
    /// duplicate container instances (only the first is kept). Returns <c>true</c> if any change
    /// was made.
    /// </summary>
    public static bool NormalizeWorkbookRoot(
        XElement workbookRoot,
        XlsxWorkbookContainerElementSchema schema,
        XNamespace workbookNs)
    {
        var changed = false;
        var keptContainer = false;

        foreach (var container in workbookRoot.Elements(workbookNs + schema.LocalName).ToList())
        {
            if (keptContainer)
            {
                // Duplicate container — remove.
                container.Remove();
                changed = true;
                continue;
            }

            changed |= Normalize(container, schema, workbookNs);

            if (schema.RemoveSelfIfEmpty && !container.Elements().Any())
            {
                container.Remove();
                changed = true;
                continue;
            }

            keptContainer = true;
        }

        return changed;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static bool NormalizeChild(XElement child, XlsxWorkbookChildElementSchema schema)
    {
        var changed = false;

        if (schema.RemoveAllChildNodes)
            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(child);
        else if (schema.RemoveChildElementsOnly)
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(child);

        // Strip unknown no-namespace attributes (keep known namespaced attributes).
        foreach (var attr in child.Attributes().ToList())
        {
            if (attr.IsNamespaceDeclaration)
                continue;

            if (attr.Name.Namespace == XNamespace.None)
            {
                if (!schema.AllowedAttributes.Contains(attr.Name.LocalName))
                {
                    attr.Remove();
                    changed = true;
                }
            }
            else
            {
                // Namespaced attribute: keep only if in AllowedNamespacedAttributes.
                var isAllowed = false;
                foreach (var allowed in schema.AllowedNamespacedAttributes)
                {
                    if (attr.Name == allowed)
                    {
                        isAllowed = true;
                        break;
                    }
                }

                if (!isAllowed)
                {
                    attr.Remove();
                    changed = true;
                }
            }
        }

        // Apply no-namespace attribute rules.
        foreach (var (name, rule) in schema.AttributeRules)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(child, name, rule);

        // Apply namespaced attribute rules.
        foreach (var (xname, rule) in schema.NamespacedAttributeRules)
        {
            var attr = child.Attribute(xname);
            var raw = attr?.Value;
            var normalized = rule(raw);
            if (string.Equals(raw, normalized, StringComparison.Ordinal))
                continue;

            changed = true;
            if (normalized is null)
                attr?.Remove();
            else
                child.SetAttributeValue(xname, normalized);
        }

        return changed;
    }
}
