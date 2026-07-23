using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Reads the VML drawing for a worksheet and returns the 1-based (row, col) addresses of note
/// shapes that are shown/pinned open ("Show Comment" in Excel).
/// </summary>
/// <remarks>
/// R76-io-vml-legacy-4-3: a note's shown/pinned state has TWO signals in the VML — the
/// ClientData <c>&lt;x:Visible/&gt;</c> element, and the shape's own CSS
/// <c>style="...;visibility:visible|hidden"</c> property. Real Excel (and FreeX's own writer,
/// see <c>XlsxLegacyCommentPreserver.ApplyVisibleFlag</c>) treats the CSS style as the
/// authoritative paint state and keeps both signals in sync. When a file has the two signals
/// disagree (e.g. hand-edited, or produced by a different writer), the style must win — otherwise
/// FreeX models the note as shown/hidden opposite to what Excel actually paints, and then bakes
/// that wrong state back in on resave. <c>&lt;x:Visible/&gt;</c> is honored only as a legacy
/// fallback when the shape has no <c>visibility</c> style property at all.
/// </remarks>
internal static class XlsxWorksheetCommentVisibilityReader
{
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";

    /// <summary>
    /// Returns the 1-based (row, col) addresses of notes whose VML ClientData contains
    /// <c>&lt;x:Visible/&gt;</c>. Returns an empty list if there is no VML or none are pinned.
    /// </summary>
    public static IReadOnlyList<(uint Row, uint Col)> Read(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        try
        {
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            // Resolve the VML drawing relationship.
            var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
            var relsEntry = archive.GetEntry(relsPath);
            if (relsEntry is null)
                return [];

            XDocument relsXml;
            try
            {
                relsXml = OpcXml.LoadXml(relsEntry);
            }
            catch
            {
                return [];
            }

            var vmlRelId = worksheetXml.Root
                ?.Element(worksheetNs + "legacyDrawing")
                ?.Attribute("{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id")
                ?.Value;
            if (string.IsNullOrWhiteSpace(vmlRelId))
                return [];

            var vmlTarget = relsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(rel =>
                    string.Equals(rel.Attribute("Id")?.Value, vmlRelId, StringComparison.Ordinal) &&
                    string.Equals(rel.Attribute("Type")?.Value, VmlDrawingRelationshipType, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(vmlTarget))
                return [];

            var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, vmlTarget);
            var vmlEntry = archive.GetEntry(vmlPath);
            if (vmlEntry is null)
                return [];

            XDocument vml;
            try
            {
                vml = OpcXml.LoadXml(vmlEntry);
            }
            catch
            {
                return [];
            }

            if (vml.Root is null)
                return [];

            List<(uint Row, uint Col)>? result = null;
            foreach (var shape in vml.Root.Elements(VmlNs + "shape"))
            {
                var clientData = shape.Elements(ExcelVmlNs + "ClientData")
                    .FirstOrDefault(cd => string.Equals(
                        cd.Attribute("ObjectType")?.Value, "Note",
                        StringComparison.OrdinalIgnoreCase));
                if (clientData is null)
                    continue;

                // The CSS visibility style is authoritative (matches the writer's precedence);
                // <x:Visible/> is honored only as a legacy fallback when the style is absent.
                var styleVisible = TryGetStyleVisibility(shape.Attribute("style")?.Value);
                var hasVisible = clientData.Element(ExcelVmlNs + "Visible") is not null;
                var isShown = styleVisible ?? hasVisible;
                if (!isShown)
                    continue;

                var rowText = clientData.Element(ExcelVmlNs + "Row")?.Value;
                var colText = clientData.Element(ExcelVmlNs + "Column")?.Value;
                if (!uint.TryParse(rowText, out var row0) || !uint.TryParse(colText, out var col0))
                    continue;

                // VML uses 0-based; CellAddress uses 1-based.
                result ??= [];
                result.Add((row0 + 1, col0 + 1));
            }

            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Parses the shape's <c>style</c> attribute for an explicit <c>visibility:</c> CSS property
    /// (mirroring <c>XlsxLegacyCommentPreserver.ApplyVisibilityStyle</c>'s format). Returns
    /// <see langword="true"/> for <c>visible</c>, <see langword="false"/> for <c>hidden</c>, or
    /// <see langword="null"/> when the shape has no <c>style</c> attribute or no
    /// <c>visibility</c> property within it (i.e. the style signal is absent).
    /// </summary>
    private static bool? TryGetStyleVisibility(string? styleValue)
    {
        if (string.IsNullOrEmpty(styleValue))
            return null;

        foreach (var property in styleValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIndex = property.IndexOf(':');
            if (colonIndex < 0 ||
                !string.Equals(property[..colonIndex].Trim(), "visibility", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property[(colonIndex + 1)..].Trim();
            if (string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase))
                return false;
            if (string.Equals(value, "visible", StringComparison.OrdinalIgnoreCase))
                return true;

            return null;
        }

        return null;
    }
}
