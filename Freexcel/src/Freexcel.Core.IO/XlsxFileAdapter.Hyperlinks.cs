using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
        var hyperlink = new XLHyperlink(linkTarget);

        if (metadata.LinkType == HyperlinkTargetKind.PlaceInThisDocument)
        {
            hyperlink.IsExternal = false;
            hyperlink.InternalAddress = linkTarget;
        }
        else
        {
            hyperlink.IsExternal = true;
            if (Uri.TryCreate(linkTarget, UriKind.Absolute, out var uri))
                hyperlink.ExternalAddress = uri;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ScreenTip))
            hyperlink.Tooltip = metadata.ScreenTip;

        return hyperlink;
    }
}
