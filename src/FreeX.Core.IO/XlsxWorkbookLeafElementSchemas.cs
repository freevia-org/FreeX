using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Declarative schema table for workbook.xml leaf elements.
/// Each entry encodes the allowed attributes and per-attribute normalization rules for one
/// element; the generic <see cref="XlsxWorkbookLeafElementNormalizer"/> drives them all.
///
/// <para>To add a new leaf element: append one row to <see cref="All"/> with
/// <c>LocalName</c>, <c>AllowedAttributes</c>, and any <c>AttributeRules</c> needed.
/// No new class or orchestrator wiring is required.</para>
/// </summary>
internal static class XlsxWorkbookLeafElementSchemas
{
    // ── shared value normalizers ──────────────────────────────────────────────────────────────

    private static readonly Regex CellRangePattern = new(
        @"^[A-Z]{1,3}[1-9][0-9]{0,6}(:[A-Z]{1,3}[1-9][0-9]{0,6})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string? NormalizeDouble(string? value)
    {
        var trimmed = value?.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
        {
            return null;
        }

        return XlsxNumberFormatting.ToXmlString(parsed);
    }

    private static string? NormalizeCellRange(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellRangePattern.IsMatch(trimmed) ? trimmed : null;
    }

    private static readonly HashSet<string> ValidCalculationModes = ["manual", "auto", "autoNoTable"];
    private static readonly HashSet<string> ValidReferenceModes = ["A1", "R1C1"];
    private static readonly HashSet<string> ValidTargetScreenSizes =
    [
        "544x376", "640x480", "720x512", "800x600", "1024x768",
        "1152x882", "1152x900", "1280x1024", "1600x1200", "1800x1440", "1920x1200"
    ];
    private static readonly HashSet<string> ValidShowObjectsValues = ["all", "placeholders", "none"];
    private static readonly HashSet<string> ValidUpdateLinksValues = ["userSet", "never", "always"];
    private static readonly HashSet<string> ValidSmartTagShowValues = ["all", "noIndicator", "none"];

    // ── schema table ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One row per workbook.xml leaf element. The generic normalizer applies these rules
    /// identically: strip disallowed attrs → apply per-attr value rules → remove child nodes.
    /// </summary>
    public static readonly IReadOnlyList<XlsxWorkbookLeafElementSchema> All =
    [
        // ── fileVersion ────────────────────────────────────────────────────────────────────
        // fileVersion schema.
        // Pass-through text only: all 5 attributes are kept verbatim if present; no value rules.
        new()
        {
            LocalName = "fileVersion",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "appName", "lastEdited", "lowestEdited", "rupBuild", "codeName"
            }
        },

        // ── fileRecoveryPr ─────────────────────────────────────────────────────────────────
        // fileRecoveryPr schema.
        // All four attributes are boolean-valued; unknown attrs and child nodes are removed.
        new()
        {
            LocalName = "fileRecoveryPr",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "autoRecover", "crashSave", "dataExtractLoad", "repairLoad"
            },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["autoRecover"]      = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["crashSave"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["dataExtractLoad"]  = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["repairLoad"]       = XlsxXmlNormalizationHelpers.NormalizeBoolean
            }
        },

        // ── fileSharing ────────────────────────────────────────────────────────────────────
        // fileSharing schema.
        new()
        {
            LocalName = "fileSharing",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "readOnlyRecommended", "userName", "reservationPassword",
                "algorithmName", "hashValue", "saltValue", "spinCount"
            },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["readOnlyRecommended"] = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["reservationPassword"] = XlsxXmlNormalizationHelpers.NormalizeLegacyPasswordHashOrNull,
                ["hashValue"]           = XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull,
                ["saltValue"]           = XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull,
                ["spinCount"]           = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull
            }
        },

        // ── workbookProtection ─────────────────────────────────────────────────────────────
        // workbookProtection schema.
        new()
        {
            LocalName = "workbookProtection",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "workbookPassword", "revisionsPassword",
                "lockStructure", "lockWindows", "lockRevision",
                "revisionsAlgorithmName", "revisionsHashValue", "revisionsSaltValue", "revisionsSpinCount",
                "workbookAlgorithmName", "workbookHashValue", "workbookSaltValue", "workbookSpinCount"
            },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["workbookPassword"]    = XlsxXmlNormalizationHelpers.NormalizeLegacyPasswordHashOrNull,
                ["revisionsPassword"]   = XlsxXmlNormalizationHelpers.NormalizeLegacyPasswordHashOrNull,
                ["lockStructure"]       = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["lockWindows"]         = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["lockRevision"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["revisionsHashValue"]  = XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull,
                ["revisionsSaltValue"]  = XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull,
                ["revisionsSpinCount"]  = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                ["workbookHashValue"]   = XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull,
                ["workbookSaltValue"]   = XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull,
                ["workbookSpinCount"]   = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull
            }
        },

        // ── workbookPr ─────────────────────────────────────────────────────────────────────
        // workbookPr schema.
        new()
        {
            LocalName = "workbookPr",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "date1904", "showObjects", "showBorderUnselectedTables", "filterPrivacy",
                "promptedSolutions", "showInkAnnotation", "backupFile", "saveExternalLinkValues",
                "updateLinks", "codeName", "hidePivotFieldList", "showPivotChartFilter",
                "allowRefreshQuery", "publishItems", "checkCompatibility", "autoCompressPictures",
                "refreshAllConnections", "defaultThemeVersion"
            },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["date1904"]                    = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["showBorderUnselectedTables"]  = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["filterPrivacy"]               = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["promptedSolutions"]           = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["showInkAnnotation"]           = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["backupFile"]                  = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["saveExternalLinkValues"]      = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["hidePivotFieldList"]          = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["showPivotChartFilter"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["allowRefreshQuery"]           = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["publishItems"]                = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["checkCompatibility"]          = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["autoCompressPictures"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["refreshAllConnections"]       = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["showObjects"]         = v => XlsxXmlNormalizationHelpers.NormalizeToken(v, ValidShowObjectsValues),
                ["updateLinks"]         = v => XlsxXmlNormalizationHelpers.NormalizeToken(v, ValidUpdateLinksValues),
                ["defaultThemeVersion"] = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                ["codeName"]            = XlsxXmlNormalizationHelpers.NormalizeOptionalText
            }
        },

        // ── calcPr ─────────────────────────────────────────────────────────────────────────
        // calcPr schema.
        new()
        {
            LocalName = "calcPr",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "calcId", "calcMode", "fullCalcOnLoad", "refMode", "iterate",
                "iterateCount", "iterateDelta", "fullPrecision", "calcCompleted",
                "calcOnSave", "concurrentCalc", "concurrentManualCount", "forceFullCalc"
            },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["fullCalcOnLoad"]       = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["iterate"]              = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["fullPrecision"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["calcCompleted"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["calcOnSave"]           = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["concurrentCalc"]       = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["forceFullCalc"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["calcId"]               = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                ["iterateCount"]         = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                ["concurrentManualCount"]= XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                ["calcMode"]             = v => XlsxXmlNormalizationHelpers.NormalizeToken(v, ValidCalculationModes),
                ["refMode"]              = v => XlsxXmlNormalizationHelpers.NormalizeToken(v, ValidReferenceModes),
                ["iterateDelta"]         = NormalizeDouble
            }
        },

        // ── webPublishing ──────────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookWebPublishingNormalizer.
        new()
        {
            LocalName = "webPublishing",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "css", "thicket", "longFileNames", "vml", "allowPng",
                "targetScreenSize", "dpi", "codePage", "characterSet"
            },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["css"]              = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["thicket"]         = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["longFileNames"]    = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["vml"]             = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["allowPng"]        = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["dpi"]             = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                ["codePage"]        = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull,
                ["targetScreenSize"]= v => XlsxXmlNormalizationHelpers.NormalizeToken(v, ValidTargetScreenSizes),
                ["characterSet"]    = XlsxXmlNormalizationHelpers.NormalizeOptionalText
            }
        },

        // ── smartTagPr ─────────────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement.
        new()
        {
            LocalName = "smartTagPr",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal)
            {
                "embed", "show"
            },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["embed"] = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["show"]  = v => XlsxXmlNormalizationHelpers.NormalizeToken(v, ValidSmartTagShowValues)
            }
        },

        // ── oleSize ────────────────────────────────────────────────────────────────────────
        // Migrated from XlsxWorkbookOleSizeNormalizer.
        // Uses RequiredAttributes so the element is removed when ref is absent or invalid.
        new()
        {
            LocalName = "oleSize",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "ref" },
            AttributeRules = new Dictionary<string, Func<string?, string?>>
            {
                ["ref"] = NormalizeCellRange
            },
            RequiredAttributes = new HashSet<string>(StringComparer.Ordinal) { "ref" }
        },
    ];

    /// <summary>
    /// Lookup table keyed by element local name for O(1) dispatch from the orchestrator.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, XlsxWorkbookLeafElementSchema> ByLocalName =
        All.ToDictionary(s => s.LocalName, StringComparer.Ordinal);
}
