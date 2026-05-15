using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class BorderShortcutService
{
    private static readonly CellBorder OutlineBorder = new(BorderStyle.Thin, CellColor.Black);
    private static readonly CellBorder NoBorder = new(BorderStyle.None);

    public static StyleDiff GetOutlineBorderDiff(GridRange range, CellAddress address) => new(
        BorderTop: address.Row == range.Start.Row ? OutlineBorder : null,
        BorderRight: address.Col == range.End.Col ? OutlineBorder : null,
        BorderBottom: address.Row == range.End.Row ? OutlineBorder : null,
        BorderLeft: address.Col == range.Start.Col ? OutlineBorder : null);

    public static StyleDiff GetClearBorderDiff() => new(
        BorderTop: NoBorder,
        BorderRight: NoBorder,
        BorderBottom: NoBorder,
        BorderLeft: NoBorder);
}
