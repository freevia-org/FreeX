using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Shared driver for worksheet normalizers that only inspect structural elements OUTSIDE
/// <c>sheetData</c> (dimension, sheetViews, merge cells, page setup, auto filters, hyperlinks,
/// conditional formats, ...).  On the cell-patch save path these normalizers historically loaded
/// every worksheet's full XML — including all cell rows — just to discover (in the overwhelmingly
/// common case) that the header is already canonical and nothing needs rewriting.  For a workbook
/// with hundreds of thousands of cells that meant tens of multi-hundred-megabyte
/// <see cref="XDocument"/> loads per save.
///
/// This helper instead parses each worksheet's header with the cell rows pruned away (the
/// <c>sheetData</c> subtree is skipped by the streaming reader and never materialized), runs the
/// normalizer against a clone of that pruned header, and only pays for a full parse + rewrite when
/// the clone actually changes.  Because the gated normalizers never read <c>sheetData</c>, the
/// pruned header yields the same change verdict as the full worksheet.  The pruned headers are
/// memoized per <see cref="ZipArchive"/> so the ~two dozen header normalizers that run during a
/// single save share one prune per worksheet rather than re-parsing independently.
/// </summary>
internal static class XlsxWorksheetHeaderNormalization
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly ConditionalWeakTable<ZipArchive, PrunedHeaderCache> Caches = new();

    /// <summary>
    /// Runs <paramref name="normalizeWorksheetRoot"/> over every worksheet in the package, using a
    /// pruned (sheetData-less) header to skip the expensive full parse whenever the header is already
    /// canonical.  <paramref name="normalizeWorksheetRoot"/> MUST only inspect elements outside
    /// <c>sheetData</c>; normalizers that touch cell rows (grid XML) cannot use this driver.
    /// </summary>
    public static void NormalizeWorksheets(ZipArchive archive, Func<XElement, bool> normalizeWorksheetRoot)
    {
        var cache = Caches.GetValue(archive, static _ => new PrunedHeaderCache());
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var prunedRoot = cache.GetOrLoad(worksheetEntry);
            if (prunedRoot is not null && !normalizeWorksheetRoot(new XElement(prunedRoot)))
            {
                // Header already canonical for this normalizer — skip the full per-cell parse.
                continue;
            }

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (normalizeWorksheetRoot(root))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
                // The stored bytes changed; drop the memoized header so later normalizers re-prune.
                cache.Invalidate(worksheetEntry.FullName);
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if any worksheet's pruned header satisfies
    /// <paramref name="predicate"/>.  Used to cheaply decide whether a rare worksheet feature (OLE
    /// controls, web-publish items, single-XML cells, ...) is present anywhere before paying for the
    /// feature's full normalization pass.  <paramref name="predicate"/> MUST only inspect elements
    /// outside <c>sheetData</c>.  Worksheets whose header cannot be pruned are treated as a match so
    /// the caller falls back to the full pass.
    /// </summary>
    public static bool AnyWorksheetHeaderMatches(ZipArchive archive, Func<XElement, bool> predicate)
    {
        var cache = Caches.GetValue(archive, static _ => new PrunedHeaderCache());
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var prunedRoot = cache.GetOrLoad(worksheetEntry);
            if (prunedRoot is null || predicate(prunedRoot))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Drops all memoized pruned headers for <paramref name="archive"/>.  Call this after any code
    /// path that rewrites worksheet headers WITHOUT going through this driver (for example the cell
    /// patch loop, which may also rewrite dimension / merge / hyperlink / sheet-view elements) so
    /// subsequent header normalizers re-prune from the current bytes.
    /// </summary>
    public static void InvalidateAll(ZipArchive archive)
    {
        if (Caches.TryGetValue(archive, out var cache))
            cache.Clear();
    }

    private sealed class PrunedHeaderCache
    {
        // null value = prune attempted and failed (don't retry); absent key = not yet attempted.
        private readonly Dictionary<string, XElement?> _byPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _attempted = new(StringComparer.OrdinalIgnoreCase);

        public XElement? GetOrLoad(ZipArchiveEntry worksheetEntry)
        {
            var path = worksheetEntry.FullName;
            if (_attempted.Contains(path))
                return _byPath.TryGetValue(path, out var cached) ? cached : null;

            var pruned = LoadWorksheetRootWithoutSheetData(worksheetEntry);
            _attempted.Add(path);
            _byPath[path] = pruned;
            return pruned;
        }

        public void Invalidate(string path)
        {
            _attempted.Remove(path);
            _byPath.Remove(path);
        }

        public void Clear()
        {
            _attempted.Clear();
            _byPath.Clear();
        }
    }

    /// <summary>
    /// Streams a worksheet part and returns its root with every child preserved EXCEPT the cell rows
    /// inside <c>sheetData</c>, which are skipped without materializing any <see cref="XElement"/>.
    /// Returns <see langword="null"/> if the part is not a worksheet or cannot be parsed, in which
    /// case callers fall back to a full parse.
    /// </summary>
    private static XElement? LoadWorksheetRootWithoutSheetData(ZipArchiveEntry worksheetEntry)
    {
        try
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "worksheet" ||
                !string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal))
            {
                return null;
            }

            var root = XmlReaderElementMaterializer.CreateShallowElement(reader);
            if (reader.IsEmptyElement)
                return root;

            var worksheetDepth = reader.Depth;
            var readNext = true;
            while (true)
            {
                if (readNext && !reader.Read())
                    break;
                readNext = true;

                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == worksheetDepth)
                    break;

                if (reader.NodeType != XmlNodeType.Element || reader.Depth != worksheetDepth + 1)
                    continue;

                if (reader.LocalName == "sheetData" &&
                    string.Equals(reader.NamespaceURI, WorksheetNs.NamespaceName, StringComparison.Ordinal))
                {
                    // Keep a shallow sheetData placeholder but skip its (potentially huge) row subtree.
                    root.Add(XmlReaderElementMaterializer.CreateShallowElement(reader));
                    reader.Skip();
                    readNext = false;
                    continue;
                }

                if (XNode.ReadFrom(reader) is XElement child)
                {
                    root.Add(child);
                    readNext = false;
                }
            }

            return root;
        }
        catch
        {
            return null;
        }
    }

}
