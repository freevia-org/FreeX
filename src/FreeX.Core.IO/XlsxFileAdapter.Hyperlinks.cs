using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static HyperlinkTargetKind GetHyperlinkTargetKind(XLHyperlink hyperlink, string target)
    {
        if (!string.IsNullOrWhiteSpace(hyperlink.InternalAddress))
            return HyperlinkTargetKind.PlaceInThisDocument;

        return target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? HyperlinkTargetKind.EmailAddress
            : HyperlinkTargetKind.ExistingFileOrWebPage;
    }

    private static string? NormalizeInternalHyperlinkAddress(string? address, string ownSheetName)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        var bangIndex = address.IndexOf('!');
        if (bangIndex < 0)
            return address;

        var sheetPart = address[..bangIndex];
        if (bangIndex > 2 && address[0] == '\'' && address[bangIndex - 1] == '\'')
        {
            // O27: Excel escapes an embedded apostrophe in a quoted sheet name by doubling it
            // (e.g. 'Bob''s Sheet'!A1 for a sheet literally named "Bob's Sheet"). Unescape ''->'
            // after stripping the surrounding quotes, or the bookmark ends up with the literal
            // "''" still in it and downstream sheet-name equality checks (WPF's
            // TryNavigateToWorkbookReference, WorkbookReferenceNavigator.UnquoteSheetName) fail
            // to match the real sheet name.
            var sheetName = address[1..(bangIndex - 1)].Replace("''", "'", StringComparison.Ordinal);
            address = sheetName + address[bangIndex..];
            bangIndex = sheetName.Length;
            sheetPart = sheetName;
        }

        // R38-io-hyperlink-2-1: ClosedXML's XLHyperlink.InternalAddress *getter* unconditionally
        // prepends "<CurrentSheet>!" to a bang-less internal address the moment it is read (both
        // when we read it here on load, and again when ClosedXML's own writer reads it to
        // serialize the "location" attribute). A hyperlink that targets a workbook-scoped DEFINED
        // NAME is stored bang-less (Excel writes e.g. location="MyDefinedName", never sheet
        // qualified), so reading that raw property turns it into "Sheet1!MyDefinedName" -- a
        // fabricated sheet-qualified reference that silently changes the hyperlink's target
        // instead of jumping to the name. Detect this by checking whether the part after the
        // bang actually parses as a cell/range reference; a defined name never can (Excel
        // forbids naming a defined name like a cell address), so if it doesn't parse, the bang
        // was bogus -- strip it and hand back the bare name instead of resolving/rewriting it
        // into a cell reference.
        //
        // R39-meta-1: that fabrication only ever prepends the hyperlink's OWN containing sheet
        // (ClosedXML's XLHyperlink.InternalAddress prepends Container.WorksheetName), so a
        // fabricated address always reads "OwnSheet!Name". A GENUINE sheet-qualified reference to
        // a sheet-scoped local defined name on a DIFFERENT sheet (e.g. a hyperlink on Sheet1
        // pointing at "Sheet2!LocalRegion") is legitimate and must keep its sheet qualifier --
        // only strip when the sheet part equals the hyperlink's own sheet, matching the
        // fabrication case.
        var reference = address[(bangIndex + 1)..];
        if (LooksLikeCellOrRangeReference(reference))
            return address;

        return string.Equals(sheetPart, ownSheetName, StringComparison.OrdinalIgnoreCase) ? reference : address;
    }

    private static bool LooksLikeCellOrRangeReference(string reference)
    {
        if (reference.Length == 0)
            return false;

        var sheet = SheetId.New();
        var parts = reference.Split(':');
        if (parts.Length == 1)
            return CellAddress.TryParse(parts[0], sheet, out _);

        if (parts.Length != 2)
            return false;

        if (CellAddress.TryParse(parts[0], sheet, out _) && CellAddress.TryParse(parts[1], sheet, out _))
            return true;

        // Whole-column (A:A) / whole-row (3:3) refs are valid range forms CellAddress.TryParse
        // can't represent on its own.
        return IsWholeColumnOrRowReference(parts[0], parts[1]);
    }

    private static bool IsWholeColumnOrRowReference(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return false;

        if (left.All(char.IsAsciiLetter) && right.All(char.IsAsciiLetter))
            return true;

        return left.All(char.IsAsciiDigit) && right.All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Inverse of <see cref="NormalizeInternalHyperlinkAddress"/>: re-quotes the sheet-name
    /// portion of an internal hyperlink address (as stored on the model, always unquoted with
    /// embedded apostrophes un-escaped) so it round-trips back to a valid Excel reference when
    /// written verbatim into the "location" attribute on the fast PATCH-save path. The full-save
    /// path goes through ClosedXML, which re-quotes on its own; this helper exists so the PATCH
    /// path (XlsxCellPatchBaseline.ApplyHyperlinkChanges) matches that behavior instead of
    /// writing an invalid unquoted sheet reference (e.g. "My Sheet!A10" instead of
    /// 'My Sheet'!A10).
    /// </summary>
    internal static string? QuoteInternalHyperlinkAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        var bangIndex = address.IndexOf('!');
        if (bangIndex <= 0)
            return address;

        // Already quoted (mirrors the guard in NormalizeInternalHyperlinkAddress) -- leave as-is
        // so we never double-quote an address that was never normalized in the first place.
        if (bangIndex > 2 && address[0] == '\'' && address[bangIndex - 1] == '\'')
            return address;

        var sheetName = address[..bangIndex];
        var rest = address[bangIndex..];
        return SheetNameFormatter.QuoteIfNeeded(sheetName) + rest;
    }

    private static XLHyperlink CreateXlsxHyperlink(string target, HyperlinkMetadata? metadata)
    {
        metadata ??= new HyperlinkMetadata();
        var linkTarget = metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument &&
                         !string.IsNullOrWhiteSpace(metadata.Bookmark)
            ? metadata.Bookmark
            : target;

        // A "#…" prefix marks an in-document target (some adapters, e.g. SpreadsheetML, carry it that
        // way). Strip it for the internal address and treat the link as internal even when the model's
        // LinkType was not set, so we never hand ClosedXML a null external URI for it.
        var isInternal = metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument ||
                         linkTarget.StartsWith("#", StringComparison.Ordinal);
        if (linkTarget.StartsWith("#", StringComparison.Ordinal))
            linkTarget = linkTarget[1..];

        var hyperlink = new XLHyperlink(linkTarget);

        if (isInternal)
        {
            hyperlink.IsExternal = false;
            hyperlink.InternalAddress = linkTarget;
        }
        else
        {
            // External link: absolute URIs (http://, file:///, mailto:…) and relative file paths
            // (docs/report.pdf, ../other.xlsx) are both valid external hyperlink targets in Excel.
            // UriKind.RelativeOrAbsolute accepts both forms; ClosedXML emits a proper relationship
            // entry for any non-null ExternalAddress, whether the Uri is absolute or relative.
            hyperlink.IsExternal = true;
            hyperlink.ExternalAddress = new Uri(NormalizeExternalHyperlinkTarget(linkTarget), UriKind.RelativeOrAbsolute);
        }

        if (!string.IsNullOrWhiteSpace(metadata.ScreenTip))
            hyperlink.Tooltip = metadata.ScreenTip;

        return hyperlink;
    }

    // RFC 3986 unreserved characters plus reserved (gen-delims + sub-delims) plus '%' itself, so
    // an already percent-encoded triplet (e.g. "%20") is left alone rather than re-escaped into
    // "%2520". Backslash ('\') is also included even though it is not a valid RFC 3986 URI
    // character: it is the path separator in Windows local file paths (C:\Reports\Q1.xlsx) and
    // UNC paths (\\server\share\Q1.xlsx), both valid "Insert Hyperlink > Existing File" targets
    // in Excel. Percent-encoding it (as the rest of this escaping logic otherwise would) turns
    // a drive-letter path into a string Uri can no longer recognise as a rooted local path
    // ("A Dos path must be rooted" UriFormatException, silently dropping the hyperlink -- see
    // R84-io-hyperlink-defined-name-5-1) and turns a UNC path into a bogus relative Uri holding
    // literal "%5C%5C…" text instead of a working link. Leaving backslash unescaped lets Uri
    // parse a drive-letter path correctly on its own; UNC paths still need the additional
    // file://host/share rewrite in <see cref="NormalizeExternalHyperlinkTarget"/> below.
    private const string SafeExternalHyperlinkUriCharacters = "-._~:/?#[]@!$&'()*+,;=%\\";

    /// <summary>
    /// R84-io-hyperlink-defined-name-5-1: rewrites a raw external hyperlink target that uses UNC
    /// path syntax (\\server\share\file.xlsx) into the equivalent file://server/share/file.xlsx
    /// URI form before it reaches <see cref="Uri"/>. A raw UNC path cannot be parsed by
    /// <see cref="Uri"/> (even with <see cref="UriKind.Absolute"/>) on this runtime, and percent-
    /// encoding its backslashes (the only alternative once backslash is excluded from escaping)
    /// produces a Uri that parses successfully but as a bogus *relative* Uri holding the literal
    /// percent-escaped text -- never resolving back to the intended UNC path. The file://host/share
    /// form is the standard, working representation of a UNC path that both <see cref="Uri"/> and
    /// Windows/Excel resolve back to \\host\share\path. Drive-letter paths (C:\Reports\Q1.xlsx)
    /// need no such rewrite -- <see cref="Uri"/> parses them correctly as long as the backslash is
    /// left un-escaped (see <see cref="SafeExternalHyperlinkUriCharacters"/>).
    /// </summary>
    private static string NormalizeExternalHyperlinkTarget(string target)
    {
        var escaped = EscapeExternalHyperlinkTarget(target);
        if (escaped.Length > 2 && escaped[0] == '\\' && escaped[1] == '\\')
            return "file:" + escaped.Replace('\\', '/');

        return escaped;
    }

    /// <summary>
    /// R38-io-hyperlink-2-3: percent-encode characters that are not valid literally inside a URI
    /// (most commonly a space) before handing the external target to <see cref="Uri"/>. ClosedXML
    /// writes the hyperlink relationship's Target verbatim from the Uri it was given, so an
    /// un-escaped space (or other reserved/unsafe character) in the model's target string ends up
    /// written raw into the .rels part -- an invalid Target per the OPC/URI rules real Excel
    /// always honours (Excel itself percent-encodes such characters when it writes a hyperlink
    /// Target). Reserved/unreserved RFC 3986 characters, and anything already part of a
    /// percent-encoded escape, are left untouched so a plain "http://…" URL -- or a target that
    /// was already escaped -- round-trips unchanged.
    /// </summary>
    private static string EscapeExternalHyperlinkTarget(string target)
    {
        var needsEscaping = false;
        foreach (var c in target)
        {
            if (!IsSafeExternalHyperlinkUriChar(c))
            {
                needsEscaping = true;
                break;
            }
        }

        if (!needsEscaping)
            return target;

        var builder = new StringBuilder(target.Length + 8);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in target.EnumerateRunes())
        {
            if (rune.Value <= 0x7F && IsSafeExternalHyperlinkUriChar((char)rune.Value))
            {
                builder.Append((char)rune.Value);
                continue;
            }

            var byteCount = rune.EncodeToUtf8(bytes);
            foreach (var b in bytes[..byteCount])
                builder.Append('%').Append(b.ToString("X2"));
        }

        return builder.ToString();
    }

    private static bool IsSafeExternalHyperlinkUriChar(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
        SafeExternalHyperlinkUriCharacters.IndexOf(c, StringComparison.Ordinal) >= 0;

    /// <summary>
    /// True when <paramref name="sheet"/> has at least one internal (PlaceInThisDocument) hyperlink
    /// whose <see cref="HyperlinkMetadata.Bookmark"/> is bang-less -- i.e. targets a defined name
    /// rather than a sheet-qualified cell/range reference. Gates
    /// <see cref="FixFabricatedDefinedNameHyperlinkLocations"/>, the FULL (ClosedXML) save-path
    /// counterpart of R38-io-hyperlink-2-1's load-time fix, so the post-processing pass only opens a
    /// worksheet XML edit session when there is actually a hyperlink of the shape ClosedXML's
    /// XLHyperlink.InternalAddress getter is known to fabricate a sheet prefix onto.
    /// </summary>
    internal static bool HasBareInternalHyperlinkBookmarks(Sheet sheet)
    {
        foreach (var metadata in sheet.HyperlinkMetadata.Values)
        {
            if (metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument &&
                !string.IsNullOrWhiteSpace(metadata.Bookmark) &&
                !metadata.Bookmark.Contains('!'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// R55-io-hyperlink-round-trip-5-1: on the FULL (ClosedXML) save path, ClosedXML's own
    /// XLHyperlink.InternalAddress *getter* fabricates a "&lt;CurrentSheet&gt;!" prefix onto a
    /// bang-less internal hyperlink address the instant it serializes the "location" attribute --
    /// the exact same corruption <see cref="NormalizeInternalHyperlinkAddress"/> already strips at
    /// LOAD time (see its R38-io-hyperlink-2-1 comment), but re-introduced on every FULL save because
    /// <see cref="CreateXlsxHyperlink"/> can only hand ClosedXML the bare bookmark -- it has no way to
    /// stop ClosedXML's writer from re-fabricating the prefix when it reads that property back.
    /// The PATCH-save path never hits this: it bypasses ClosedXML entirely and writes the model's
    /// Bookmark verbatim (via <see cref="QuoteInternalHyperlinkAddress"/>).
    /// Post-process the saved worksheet XML here, reusing the exact same detection logic
    /// <see cref="NormalizeInternalHyperlinkAddress"/> applies at load, so a workbook-scoped
    /// defined-name hyperlink round-trips through a full save unchanged.
    /// </summary>
    internal static void FixFabricatedDefinedNameHyperlinkLocations(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);

        foreach (var sheet in workbook.Sheets)
        {
            if (!HasBareInternalHyperlinkBookmarks(sheet))
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var hyperlinksElement = edit.Root.Element(worksheetNs + "hyperlinks");
            if (hyperlinksElement is null)
                continue;

            var changed = false;
            foreach (var hyperlinkElement in hyperlinksElement.Elements(worksheetNs + "hyperlink"))
            {
                var locationAttribute = hyperlinkElement.Attribute("location");
                if (locationAttribute is null || string.IsNullOrWhiteSpace(locationAttribute.Value))
                    continue;

                var normalized = NormalizeInternalHyperlinkAddress(locationAttribute.Value, sheet.Name);
                if (!string.Equals(normalized, locationAttribute.Value, StringComparison.Ordinal))
                {
                    locationAttribute.Value = normalized!;
                    changed = true;
                }
            }

            if (changed)
                session.MarkDirty(edit);
        }
    }

    /// <summary>
    /// True when <paramref name="sheet"/> has at least one EXTERNAL hyperlink (LinkType other than
    /// PlaceInThisDocument) whose <see cref="HyperlinkMetadata.Bookmark"/> ("location" sub-address --
    /// Excel's "Existing File &gt; Bookmark..." feature) is non-empty. Gates
    /// <see cref="FixExternalHyperlinkBookmarkLocations"/>.
    /// </summary>
    internal static bool HasExternalHyperlinkBookmarks(Sheet sheet)
    {
        foreach (var metadata in sheet.HyperlinkMetadata.Values)
        {
            if (metadata.LinkType != HyperlinkTargetKind.PlaceInThisDocument &&
                !string.IsNullOrWhiteSpace(metadata.Bookmark))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// R96-io-hyperlink-external-bookmark: backfills the "location" attribute onto an EXTERNAL
    /// hyperlink's saved &lt;hyperlink&gt; element on a FULL (ClosedXML) save. ClosedXML's
    /// XLHyperlink writer branches exclusively on IsExternal (decompiled from
    /// ClosedXML.Excel.IO.WorksheetPartWriter): when IsExternal is true it emits ONLY an r:id
    /// relationship, never "location"; when false it emits ONLY "location", never r:id. The
    /// InternalAddress/ExternalAddress property setters are likewise mutually exclusive -- each one
    /// flips IsExternal as a side effect, so whichever is assigned LAST silently discards the other
    /// -- meaning CreateXlsxHyperlink has no way to hand ClosedXML both an r:id AND a "location" for
    /// the same element, however it orders the assignments. (The suggested fix of simply also
    /// setting XLHyperlink.InternalAddress for an external link with a Bookmark was verified against
    /// the real ClosedXML 0.105.0 assembly to silently DROP the r:id/external relationship instead --
    /// it does not work.) Post-processing is therefore the only way to emit both: look up each
    /// affected hyperlink's freshly-regenerated &lt;hyperlink&gt; element by its CURRENT cell address
    /// straight from the live model, rather than by string-matching a pre-edit source XML snapshot's
    /// "ref" (the approach XlsxWorksheetMetadataPreserver.MergeWorksheetHyperlinkMetadata uses, which
    /// misses the moment the anchor cell moves to a new address via a row/column insert or delete,
    /// or a cut-and-paste move) -- both the model's Sheet.HyperlinkMetadata key and the regenerated
    /// worksheet's "ref" always reflect the SAME current address, since ClosedXML writes each
    /// hyperlink's <c>ref</c> from the current IXLCell it was attached to (see
    /// XlsxFileAdapter.Save.cs, which sets one hyperlink per model cell address). This closes the gap
    /// unconditionally, independent of whether the anchor cell ever moved.
    /// </summary>
    internal static void FixExternalHyperlinkBookmarkLocations(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);

        foreach (var sheet in workbook.Sheets)
        {
            if (!HasExternalHyperlinkBookmarks(sheet))
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var hyperlinksElement = edit.Root.Element(worksheetNs + "hyperlinks");
            if (hyperlinksElement is null)
                continue;

            var hyperlinksByReference = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var hyperlinkElement in hyperlinksElement.Elements(worksheetNs + "hyperlink"))
            {
                var reference = hyperlinkElement.Attribute("ref")?.Value;
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    // Preserve the document-order winner used by the former FirstOrDefault lookup.
                    hyperlinksByReference.TryAdd(reference, hyperlinkElement);
                }
            }

            var changed = false;
            foreach (var (address, metadata) in sheet.HyperlinkMetadata)
            {
                if (metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument ||
                    string.IsNullOrWhiteSpace(metadata.Bookmark))
                {
                    continue;
                }

                var reference = address.ToA1();
                if (!hyperlinksByReference.TryGetValue(reference, out var hyperlinkElement))
                    continue;

                // Defensive: never overwrite a "location" a future ClosedXML version might already
                // have written for this element.
                if (hyperlinkElement.Attribute("location") is not null)
                    continue;

                hyperlinkElement.SetAttributeValue("location", metadata.Bookmark);
                changed = true;
            }

            if (changed)
                session.MarkDirty(edit);
        }
    }
}
