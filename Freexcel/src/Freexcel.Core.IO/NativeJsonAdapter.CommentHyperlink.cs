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

    private static (CellAddress Address, string Target)? TryLoadHyperlink(HyperlinkDto? hyperlinkDto, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(hyperlinkDto?.Address) || hyperlinkDto.Target is null)
            return null;

        try
        {
            var address = CellAddress.Parse(hyperlinkDto.Address, sheetId);
            return address.Sheet == sheetId
                ? (address, hyperlinkDto.Target)
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

    private static HyperlinkDto ToHyperlinkDto(KeyValuePair<CellAddress, string> pair) => new()
    {
        Address = pair.Key.ToA1(),
        Target = pair.Value
    };
}
