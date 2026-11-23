using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeX.Core.IO;

/// <summary>
/// Repairs legacy VML drawing parts (<c>xl/drawings/*.vml</c>) that are not well-formed XML so ClosedXML's
/// strict comment loader accepts them.
/// </summary>
/// <remarks>
/// Excel writes form-control (button) captions into VML as HTML-ish markup -- <c>&lt;br&gt;</c> with no closing
/// tag -- and tolerates it on read, but ClosedXML parses the VML with a strict XML reader and throws
/// <c>ArgumentException("Could not load comments file")</c>, costing the user the whole workbook. This repair is
/// applied ONLY to the ClosedXML-only parse copy of the package; the pristine source package (which FreeX reads
/// comments and form controls from, and restores on save) is never modified.
/// </remarks>
internal static class XlsxClosedXmlVmlRepair
{
    private static readonly Regex VoidHtmlTag = new(
        @"<(?<name>br|hr|img|input|meta|link|wbr)(?<attrs>(?:\s+[^<>]*?)?)\s*(?<!/)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NamedHtmlEntity = new(
        @"&(?<name>nbsp|copy|reg|trade|ndash|mdash|hellip|bull);",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns a NEW writable package stream in which every non-well-formed VML part has had void HTML tags
    /// self-closed and common HTML entities converted to numeric references. Parts that are already well-formed
    /// (or still malformed after repair) are left byte-identical.
    /// </summary>
    internal static MemoryStream CreateRepairedCopy(MemoryStream sourcePackage)
    {
        sourcePackage.Position = 0;
        var working = new MemoryStream();
        sourcePackage.CopyTo(working);
        sourcePackage.Position = 0;
        working.Position = 0;

        using (var archive = new ZipArchive(working, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (var entry in archive.Entries.ToList())
            {
                if (!entry.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
                    !entry.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    text = reader.ReadToEnd();

                if (IsWellFormed(text))
                    continue;

                var repaired = Repair(text);
                if (ReferenceEquals(repaired, text) || !IsWellFormed(repaired))
                    continue;

                var name = entry.FullName;
                var lastWrite = entry.LastWriteTime;
                entry.Delete();
                var replacement = archive.CreateEntry(name, CompressionLevel.Optimal);
                replacement.LastWriteTime = lastWrite;
                using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
                writer.Write(repaired);
            }
        }

        working.Position = 0;
        return working;
    }

    internal static string Repair(string vml)
    {
        var repaired = VoidHtmlTag.Replace(vml, match => $"<{match.Groups["name"].Value}{match.Groups["attrs"].Value}/>");
        repaired = NamedHtmlEntity.Replace(repaired, match => match.Groups["name"].Value switch
        {
            "nbsp" => "&#160;",
            "copy" => "&#169;",
            "reg" => "&#174;",
            "trade" => "&#8482;",
            "ndash" => "&#8211;",
            "mdash" => "&#8212;",
            "hellip" => "&#8230;",
            _ => "&#8226;",
        });
        return string.Equals(repaired, vml, StringComparison.Ordinal) ? vml : repaired;
    }

    private static bool IsWellFormed(string xml)
    {
        try
        {
            var settings = SecureXmlReaderSettings.Create();
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read())
            {
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
