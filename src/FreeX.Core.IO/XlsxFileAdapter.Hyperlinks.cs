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

    private static string? NormalizeInternalHyperlinkAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        var bangIndex = address.IndexOf('!');
        if (bangIndex > 2 && address[0] == '\'' && address[bangIndex - 1] == '\'')
        {
            // O27: Excel escapes an embedded apostrophe in a quoted sheet name by doubling it
            // (e.g. 'Bob''s Sheet'!A1 for a sheet literally named "Bob's Sheet"). Unescape ''->'
            // after stripping the surrounding quotes, or the bookmark ends up with the literal
            // "''" still in it and downstream sheet-name equality checks (WPF's
            // TryNavigateToWorkbookReference, WorkbookReferenceNavigator.UnquoteSheetName) fail
            // to match the real sheet name.
            var sheetName = address[1..(bangIndex - 1)].Replace("''", "'", StringComparison.Ordinal);
            return sheetName + address[bangIndex..];
        }

        return address;
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
            hyperlink.ExternalAddress = new Uri(linkTarget, UriKind.RelativeOrAbsolute);
        }

        if (!string.IsNullOrWhiteSpace(metadata.ScreenTip))
            hyperlink.Tooltip = metadata.ScreenTip;

        return hyperlink;
    }
}
