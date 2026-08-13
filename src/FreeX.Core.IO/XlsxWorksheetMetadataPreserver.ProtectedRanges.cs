using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    // Worksheet protected range native metadata preservation.
    private static bool MergeWorksheetProtectedRanges(
        XElement sourceProtectedRanges,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledSqrefs)
    {
        var targetProtectedRanges = targetRoot.Element(workbookNs + "protectedRanges");

        var changed = false;
        var targetBySqref = targetProtectedRanges is null
            ? new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase)
            : targetProtectedRanges
                .Elements(workbookNs + "protectedRange")
                .Select(element => (Element: element, Key: CanonicalSupportedProtectedRangeSqref(element)))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .GroupBy(pair => pair.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Element,
                    StringComparer.OrdinalIgnoreCase);

        foreach (var sourceRange in sourceProtectedRanges.Elements(workbookNs + "protectedRange"))
        {
            var sourceSqref = CanonicalSupportedProtectedRangeSqref(sourceRange);
            if (!string.IsNullOrWhiteSpace(sourceSqref))
            {
                // A fully-parseable sqref (single- or multi-area) that is entirely represented in
                // the model has already been re-emitted (one modeled AllowEditRange per area) by
                // XlsxAllowEditRangeMapper.Save, so it must not also be copied verbatim below (that
                // would duplicate it). A single-area sqref additionally has a 1:1 matching target
                // element whose native-only attributes (custom name, extra attributes/children) can
                // be merged back onto it.
                if (!IsFullyModeledSqref(sourceSqref, modeledSqrefs))
                    continue;

                var areas = sourceSqref.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (areas.Length > 1)
                {
                    // A multi-area sqref (e.g. "B2:B10 D2:D10") is represented by several separate
                    // rebuilt target elements (one per area, each auto-named "FreeXAllowEditRange{n}"
                    // by XlsxAllowEditRangeMapper.BuildProtectedRangeElement) with no single element
                    // to merge full native metadata onto. Still restore the original multi-area NAME
                    // (e.g. a VBA-visible "PayrollCells") onto every rebuilt area-element belonging to
                    // this source range, matched by per-area sqref, so it survives resave instead of
                    // being silently replaced by the auto-generated placeholder name.
                    var sourceName = sourceRange.Attribute("name")?.Value;
                    if (!string.IsNullOrWhiteSpace(sourceName))
                    {
                        foreach (var area in areas)
                        {
                            if (targetBySqref.TryGetValue(area, out var targetArea) &&
                                !string.Equals(targetArea.Attribute("name")?.Value, sourceName, StringComparison.Ordinal))
                            {
                                targetArea.SetAttributeValue("name", sourceName);
                                changed = true;
                            }
                        }
                    }

                    continue;
                }

                if (targetBySqref.TryGetValue(sourceSqref, out var targetRange) &&
                    MergeProtectedRangeMetadata(sourceRange, targetRange))
                {
                    changed = true;
                }

                continue;
            }

            if (targetProtectedRanges is null)
            {
                targetProtectedRanges = new XElement(workbookNs + "protectedRanges");
                targetRoot.Add(targetProtectedRanges);
                changed = true;
            }

            if (!HasEquivalentProtectedRange(targetProtectedRanges, sourceRange, workbookNs))
            {
                targetProtectedRanges.Add(new XElement(sourceRange));
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Canonicalizes a <c>&lt;protectedRange&gt;</c> element's <c>sqref</c> into a form comparable
    /// against <see cref="XlsxAllowEditRangeMapper.GetModeledReferences"/>'s per-area strings. A
    /// single-area sqref canonicalizes to that area's <see cref="GridRange"/> string (unchanged
    /// behavior). A multi-area sqref (space-separated areas, e.g. "B2:B10 D2:D10") canonicalizes to
    /// each area's GridRange string, sorted and re-joined by single spaces, so equivalent multi-area
    /// sqrefs compare equal regardless of the original area order/spacing. Returns null when any
    /// area fails to parse (an unsupported/malformed sqref, which is preserved verbatim instead).
    /// </summary>
    private static string? CanonicalSupportedProtectedRangeSqref(XElement protectedRange)
    {
        var sqref = protectedRange.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(sqref))
            return null;

        var tokens = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return null;

        var sheet = SheetId.New();
        var canonicalAreas = new string[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!XlsxSqrefParser.TryParseRangeToken(tokens[index], sheet, out var range))
                return null;

            canonicalAreas[index] = range.ToString();
        }

        if (canonicalAreas.Length == 1)
            return canonicalAreas[0];

        Array.Sort(canonicalAreas, StringComparer.OrdinalIgnoreCase);
        return string.Join(' ', canonicalAreas);
    }

    /// <summary>
    /// True when every area of <paramref name="canonicalSqref"/> (as produced by
    /// <see cref="CanonicalSupportedProtectedRangeSqref"/>, so single areas have no internal spaces)
    /// is present in <paramref name="modeledSqrefs"/> — i.e. the whole sqref, single- or multi-area,
    /// is already represented in the model and must not be duplicated as a native-only passthrough.
    /// </summary>
    private static bool IsFullyModeledSqref(string canonicalSqref, IReadOnlySet<string> modeledSqrefs)
    {
        foreach (var area in canonicalSqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!modeledSqrefs.Contains(area))
                return false;
        }

        return true;
    }

    // "sqref" is the join key (handled separately) and the per-range password quartet
    // (legacy "password" or modern "algorithmName"/"hashValue"/"saltValue"/"spinCount") is fully
    // modeled via Sheet.AllowEditRangePasswords: XlsxAllowEditRangeMapper.Save has already rebuilt
    // targetRange's password attributes from the CURRENT model state (including a password having
    // been cleared or changed). None of it may be blindly copied back from the stale pre-edit
    // source element -- that would resurrect a removed/changed range password's old verifier, the
    // same bug class ModeledSheetProtectionAttributes guards against for sheet-level protection
    // (see XlsxWorksheetMetadataPreserver.MergeHelpers.cs).
    private static readonly HashSet<string> ModeledProtectedRangePasswordAttributes = new(StringComparer.Ordinal)
    {
        "sqref",
        "password",
        "algorithmName",
        "hashValue",
        "saltValue",
        "spinCount",
    };

    private static bool MergeProtectedRangeMetadata(XElement sourceRange, XElement targetRange)
    {
        var changed = false;
        foreach (var sourceAttribute in sourceRange.Attributes())
        {
            if (ModeledProtectedRangePasswordAttributes.Contains(sourceAttribute.Name.LocalName))
                continue;

            if (targetRange.Attribute(sourceAttribute.Name)?.Value == sourceAttribute.Value)
                continue;

            targetRange.SetAttributeValue(sourceAttribute.Name, sourceAttribute.Value);
            changed = true;
        }

        if (MergeMissingNativeChildren(sourceRange, targetRange, _ => true))
        {
            changed = true;
        }

        return changed;
    }

    private static bool HasEquivalentProtectedRange(
        XElement targetProtectedRanges,
        XElement sourceRange,
        XNamespace workbookNs)
    {
        var sourceSqref = sourceRange.Attribute("sqref")?.Value;
        var sourceName = sourceRange.Attribute("name")?.Value;
        var sourceAreas = NormalizeSqrefAreaSet(sourceSqref);
        return targetProtectedRanges
            .Elements(workbookNs + "protectedRange")
            .Any(targetRange =>
                (sourceAreas is not null &&
                 sourceAreas.SetEquals(NormalizeSqrefAreaSet(targetRange.Attribute("sqref")?.Value) ?? [])) ||
                (sourceAreas is null &&
                 !string.IsNullOrWhiteSpace(sourceName) &&
                 string.Equals(targetRange.Attribute("name")?.Value, sourceName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Splits a <c>sqref</c> attribute's raw (unparsed) space-separated area tokens into a
    /// case-insensitive set, for order/spacing-independent equivalence checks against another
    /// sqref. Unlike <see cref="CanonicalSupportedProtectedRangeSqref"/> this does not require the
    /// areas to parse as valid <see cref="GridRange"/>s — it is a purely textual comparison used
    /// only to detect duplicate native-only ranges being re-added. Returns null for a blank sqref.
    /// </summary>
    private static HashSet<string>? NormalizeSqrefAreaSet(string? sqref) =>
        string.IsNullOrWhiteSpace(sqref)
            ? null
            : sqref
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

}
