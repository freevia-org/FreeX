using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    // ── NativeXmlPreserveBag test helpers ────────────────────────────────────

    private static string? BagAttr(NativeXmlPreserveBag? bag, string key, string attrName)
    {
        if (bag is null) return null;
        var xml = bag.Get(key);
        if (xml is null) return null;
        try { return XElement.Parse(xml).Attribute(attrName)?.Value; } catch { return null; }
    }

    private static IReadOnlyList<string> BagChildren(NativeXmlPreserveBag? bag, string key)
    {
        if (bag is null) return [];
        var xml = bag.Get(key);
        if (xml is null) return [];
        try
        {
            return XElement.Parse(xml).Elements()
                .Select(e => e.ToString(SaveOptions.DisableFormatting))
                .ToList();
        }
        catch { return []; }
    }
}
