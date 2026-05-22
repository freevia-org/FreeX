using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static (CellAddress Address, string Text)? TryLoadComment(CommentDto? commentDto, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(commentDto?.Address) || commentDto.Text is null)
            return null;

        try
        {
            var address = CellAddress.Parse(commentDto.Address, sheetId);
            return address.Sheet == sheetId
                ? (address, commentDto.Text)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (CellAddress Address, string Target, HyperlinkMetadata Metadata)? TryLoadHyperlink(HyperlinkDto? hyperlinkDto, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(hyperlinkDto?.Address) || hyperlinkDto.Target is null)
            return null;

        try
        {
            var address = CellAddress.Parse(hyperlinkDto.Address, sheetId);
            return address.Sheet == sheetId
                ? (address, hyperlinkDto.Target, ToHyperlinkMetadata(hyperlinkDto))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static CommentDto ToCommentDto(KeyValuePair<CellAddress, string> pair) => new()
    {
        Address = pair.Key.ToA1(),
        Text = pair.Value
    };

    private static HyperlinkDto ToHyperlinkDto(Sheet sheet, KeyValuePair<CellAddress, string> pair)
    {
        sheet.HyperlinkMetadata.TryGetValue(pair.Key, out var metadata);
        metadata ??= new HyperlinkMetadata();
        return new HyperlinkDto
        {
            Address = pair.Key.ToA1(),
            Target = pair.Value,
            LinkType = metadata.LinkType,
            ScreenTip = string.IsNullOrWhiteSpace(metadata.ScreenTip) ? null : metadata.ScreenTip,
            Bookmark = string.IsNullOrWhiteSpace(metadata.Bookmark) ? null : metadata.Bookmark
        };
    }

    private static HyperlinkMetadata ToHyperlinkMetadata(HyperlinkDto dto) =>
        new(
            dto.LinkType is { } linkType && Enum.IsDefined(linkType)
                ? linkType
                : HyperlinkTargetKind.ExistingFileOrWebPage,
            (dto.ScreenTip ?? "").Trim(),
            (dto.Bookmark ?? "").Trim());
}
