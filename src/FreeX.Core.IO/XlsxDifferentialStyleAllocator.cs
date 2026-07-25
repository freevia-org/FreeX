using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// R89-io-autofilter-color-dxf-1-1: shared workbook-level &lt;dxfs&gt; access used by both
/// <see cref="XlsxAdvancedConditionalFormatWriter"/> (conditional-format differential styles) and
/// <see cref="XlsxAutoFilterColorFilterDxfWriter"/> (AutoFilter "Filter by Cell/Font Colour" dxfs),
/// so a numFmtId allocated by one writer can never collide with the other, and both writers append
/// into the SAME &lt;dxfs&gt; element (whichever writer runs first in the save pipeline sees, and
/// appends after, whatever the other already wrote). The per-writer decision of whether/how to
/// de-duplicate against existing entries stays with each writer -- this only owns the low-level
/// element/index bookkeeping that both need to agree on.
/// </summary>
internal static class XlsxDifferentialStyleAllocator
{
    public static XElement GetOrCreateDxfsElement(XElement root, XNamespace workbookNs)
    {
        var dxfs = root.Element(workbookNs + "dxfs");
        if (dxfs is null)
        {
            dxfs = new XElement(workbookNs + "dxfs");
            root.Add(dxfs);
        }

        return dxfs;
    }

    /// <summary>
    /// Allocate numFmtIds above the maximum existing custom format id to avoid collision with
    /// workbook numFmts (ids >= 164) and other dxf numFmts already in the file. The OOXML spec
    /// reserves 0-163 for built-ins; custom formats start at 164.
    /// </summary>
    public static int ComputeNextCustomNumFmtId(XElement root, XElement dxfs, XNamespace workbookNs)
    {
        const int MinCustomNumFmtId = 164;
        var maxExistingNumFmtId = root
            .Element(workbookNs + "numFmts")?
            .Elements(workbookNs + "numFmt")
            .Select(element => int.TryParse(element.Attribute("numFmtId")?.Value, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() ?? 0;
        var maxDxfNumFmtId = dxfs
            .Elements(workbookNs + "dxf")
            .SelectMany(dxf => dxf.Elements(workbookNs + "numFmt"))
            .Select(element => int.TryParse(element.Attribute("numFmtId")?.Value, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(MinCustomNumFmtId, Math.Max(maxExistingNumFmtId, maxDxfNumFmtId) + 1);
    }

    /// <summary>
    /// Returns the index of an existing &lt;dxf&gt; structurally identical to <paramref name="dxfXml"/>
    /// if one is already present in <paramref name="dxfs"/> (so two AutoFilter colour filters that
    /// pick the same colour -- or a colour filter that happens to match an existing conditional-format
    /// dxf -- share one dxf entry/index instead of the table accreting a duplicate on every save);
    /// otherwise appends <paramref name="dxfXml"/> as a new entry and returns its new index.
    /// </summary>
    public static int AllocateOrReuse(XElement dxfs, XElement dxfXml, XNamespace workbookNs)
    {
        var index = 0;
        foreach (var existing in dxfs.Elements(workbookNs + "dxf"))
        {
            if (XNode.DeepEquals(existing, dxfXml))
                return index;

            index++;
        }

        dxfs.Add(dxfXml);
        dxfs.SetAttributeValue("count", dxfs.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        return index;
    }
}
