using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum BorderEdge
{
    Top,
    Right,
    Bottom,
    Left
}

public static class BorderShortcutService
{
    private static readonly CellBorder NoBorder = new(BorderStyle.None);

    public static StyleDiff GetAllBorderDiff()
    {
        var border = CreateBlackBorder(BorderStyle.Thin);
        return new StyleDiff(BorderTop: border, BorderRight: border, BorderBottom: border, BorderLeft: border);
    }

    public static StyleDiff GetSingleBorderDiff(BorderEdge edge, BorderStyle style)
    {
        var border = CreateBlackBorder(style);
        return edge switch
        {
            BorderEdge.Top => new StyleDiff(BorderTop: border),
            BorderEdge.Right => new StyleDiff(BorderRight: border),
            BorderEdge.Bottom => new StyleDiff(BorderBottom: border),
            BorderEdge.Left => new StyleDiff(BorderLeft: border),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null)
        };
    }

    public static StyleDiff GetOutlineBorderDiff(GridRange range, CellAddress address) =>
        GetOutlineBorderDiff(range, address, BorderStyle.Thin);

    public static StyleDiff GetOutlineBorderDiff(GridRange range, CellAddress address, BorderStyle style)
    {
        var border = CreateBlackBorder(style);
        return new StyleDiff(
            BorderTop: address.Row == range.Start.Row ? border : null,
            BorderRight: address.Col == range.End.Col ? border : null,
            BorderBottom: address.Row == range.End.Row ? border : null,
            BorderLeft: address.Col == range.Start.Col ? border : null);
    }

    public static StyleDiff GetTopAndBottomBorderDiff(GridRange range, CellAddress address, BorderStyle bottomStyle)
    {
        var topBorder = CreateBlackBorder(BorderStyle.Thin);
        var bottomBorder = CreateBlackBorder(bottomStyle);
        return new StyleDiff(
            BorderTop: address.Row == range.Start.Row ? topBorder : null,
            BorderBottom: address.Row == range.End.Row ? bottomBorder : null);
    }

    public static StyleDiff GetClearBorderDiff() => new(
        BorderTop: NoBorder,
        BorderRight: NoBorder,
        BorderBottom: NoBorder,
        BorderLeft: NoBorder);

    public static bool HasBorderChanges(StyleDiff diff) =>
        diff.BorderTop is not null ||
        diff.BorderRight is not null ||
        diff.BorderBottom is not null ||
        diff.BorderLeft is not null;

    private static CellBorder CreateBlackBorder(BorderStyle style) => new(style, CellColor.Black);
}
