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
            return address[1..(bangIndex - 1)] + address[bangIndex..];

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
        else if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var uri))
        {
            hyperlink.IsExternal = true;
            hyperlink.ExternalAddress = uri;
        }
        else
        {
            // Not an absolute URI and not flagged internal: ClosedXML would crash building an external
            // relationship with a null URI. Fall back to an internal link so the cell keeps a usable
            // target (the visible text/value is unchanged) instead of aborting the whole save.
            hyperlink.IsExternal = false;
            hyperlink.InternalAddress = linkTarget;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ScreenTip))
            hyperlink.Tooltip = metadata.ScreenTip;

        return hyperlink;
    }
}
