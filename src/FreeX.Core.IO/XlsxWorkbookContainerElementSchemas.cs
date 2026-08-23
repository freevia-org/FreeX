using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Declarative schema table for workbook.xml container elements.
/// Each entry encodes the allowed attributes, child element normalization rules, and
/// dedup/pruning policies; the generic
/// <see cref="XlsxWorkbookContainerElementNormalizer"/> drives them all.
///
/// <para>Elements handled here (migrated from dedicated normalizer classes):
/// <c>definedNames</c>, <c>externalReferences</c>, <c>functionGroups</c>,
/// <c>webPublishObjects</c>, <c>pivotCaches</c>.</para>
///
/// <para>Residual (too complex for the table):
/// <c>bookViews</c>, <c>customWorkbookViews</c>, <c>extLst</c>.</para>
/// </summary>
internal static class XlsxWorkbookContainerElementSchemas
{
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XName RelIdName = RelNs + "id";

    // ── shared value normalizers ──────────────────────────────────────────────────────────────

    private static string? NormalizeRelationshipId(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeNonNegativeInt(string? value)
    {
        var trimmed = value?.Trim();
        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    // ── schema table ─────────────────────────────────────────────────────────────────────────

    /// <summary>One row per workbook.xml container element.</summary>
    public static readonly IReadOnlyList<XlsxWorkbookContainerElementSchema> All =
    [
        // ── definedNames ───────────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookDefinedNameNormalizer.
        new()
        {
            LocalName = "definedNames",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal),
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "definedName",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
                {
                    "name",
                    "comment",
                    "customMenu",
                    "description",
                    "help",
                    "statusBar",
                    "localSheetId",
                    "hidden",
                    "function",
                    "vbProcedure",
                    "xlm",
                    "functionGroupId",
                    "shortcutKey",
                    "publishToServer",
                    "workbookParameter"
                },
                RequiredAttributes = new HashSet<string>(StringComparer.Ordinal) { "name" },
                AttributeRules = new Dictionary<string, Func<string?, string?>>
                {
                    ["name"]             = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["comment"]          = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["customMenu"]       = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["description"]      = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["help"]             = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["statusBar"]        = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["shortcutKey"]      = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["hidden"]           = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                    ["function"]         = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                    ["vbProcedure"]      = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                    ["xlm"]              = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                    ["publishToServer"]  = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                    ["workbookParameter"]= XlsxXmlNormalizationHelpers.NormalizeBoolean,
                    ["localSheetId"]     = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                    ["functionGroupId"]  = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull
                },
                RemoveAllChildNodes = false,          // definedName has text content (the formula)
                RemoveChildElementsOnly = true        // but child elements (if any) should be removed
            },
            RemoveSelfIfEmpty = true
        },

        // ── externalReferences ─────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookExternalReferencesNormalizer.
        new()
        {
            LocalName = "externalReferences",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal),
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "externalReference",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal),
                AllowedNamespacedAttributes = [RelIdName],
                RequiredNamespacedAttributes = [RelIdName],
                NamespacedAttributeRules = new Dictionary<XName, Func<string?, string?>>
                {
                    [RelIdName] = NormalizeRelationshipId
                }
            },
            DedupKeySelectors = [el => el.Attribute(RelIdName)?.Value?.Trim()],
            RemoveSelfIfEmpty = true
        },

        // ── functionGroups ─────────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookFunctionGroupsNormalizer.
        // Note: the container stays even when empty (no RemoveSelfIfEmpty).
        new()
        {
            LocalName = "functionGroups",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "builtInGroupCount" },
            ContainerAttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["builtInGroupCount"] = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull
            },
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "functionGroup",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "name" },
                RequiredAttributes = new HashSet<string>(StringComparer.Ordinal) { "name" },
                AttributeRules = new Dictionary<string, Func<string?, string?>>
                {
                    ["name"] = XlsxXmlNormalizationHelpers.NormalizeOptionalText
                }
            },
            RemoveSelfIfEmpty = false
        },

        // ── webPublishObjects ──────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookWebPublishObjectsNormalizer.
        // Uses PostProcess to recompute the count attribute.
        new()
        {
            LocalName = "webPublishObjects",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "count" },
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "webPublishObject",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
                {
                    "id",
                    "divId",
                    "sourceObject",
                    "destinationFile",
                    "title",
                    "autoRepublish"
                },
                RequiredAttributes = new HashSet<string>(StringComparer.Ordinal)
                {
                    "id",
                    "divId",
                    "destinationFile",
                    "sourceObject"
                },
                AttributeRules = new Dictionary<string, Func<string?, string?>>
                {
                    ["id"]              = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                    ["divId"]           = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["sourceObject"]    = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["destinationFile"] = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["title"]           = XlsxXmlNormalizationHelpers.NormalizeOptionalText,
                    ["autoRepublish"]   = XlsxXmlNormalizationHelpers.NormalizeBoolean
                }
            },
            RemoveSelfIfEmpty = true,
            PostProcess = static (container, workbookNs) =>
            {
                var childName = workbookNs + "webPublishObject";
                var count = container.Elements(childName).Count().ToString(CultureInfo.InvariantCulture);
                XlsxXmlNormalizationHelpers.SetAttributeIfChanged(container, "count", count);
            }
        },

        // ── pivotCaches ────────────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookPivotCachesNormalizer.
        // Both cacheId and r:id must be unique; DedupKeySelectors fires independently per key.
        new()
        {
            LocalName = "pivotCaches",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal),
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "pivotCache",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "cacheId" },
                AllowedNamespacedAttributes = [RelIdName],
                RequiredAttributes = new HashSet<string>(StringComparer.Ordinal) { "cacheId" },
                RequiredNamespacedAttributes = [RelIdName],
                AttributeRules = new Dictionary<string, Func<string?, string?>>
                {
                    ["cacheId"] = NormalizeNonNegativeInt
                },
                NamespacedAttributeRules = new Dictionary<XName, Func<string?, string?>>
                {
                    [RelIdName] = NormalizeRelationshipId
                }
            },
            // Two independent dedup sets: cacheId and r:id are each unique.
            DedupKeySelectors =
            [
                el => el.Attribute("cacheId")?.Value?.Trim(),
                el => el.Attribute(RelIdName)?.Value?.Trim()
            ],
            RemoveSelfIfEmpty = true
        },
    ];

    /// <summary>Lookup keyed by container element local name for O(1) dispatch.</summary>
    public static readonly IReadOnlyDictionary<string, XlsxWorkbookContainerElementSchema> ByLocalName =
        All.ToDictionary(s => s.LocalName, StringComparer.Ordinal);
}
