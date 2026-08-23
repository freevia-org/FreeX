namespace FreeX.Core.Model;

/// <summary>Resolves competing cell borders using Excel's visual prominence order.</summary>
public static class BorderStylePrecedence
{
    private static readonly BorderStyle[] RankedStyles =
    [
        BorderStyle.Double,
        BorderStyle.Thick,
        BorderStyle.Medium,
        BorderStyle.MediumDashDotDot,
        BorderStyle.MediumDashDot,
        BorderStyle.MediumDashed,
        BorderStyle.SlantDashDot,
        BorderStyle.Thin,
        BorderStyle.DashDotDot,
        BorderStyle.DashDot,
        BorderStyle.Dashed,
        BorderStyle.Dotted,
        BorderStyle.Hair,
        BorderStyle.None,
    ];

    public static CellBorder ResolveWinner(CellBorder first, CellBorder second)
    {
        if (first.Style == BorderStyle.None)
            return second;
        if (second.Style == BorderStyle.None)
            return first;

        return GetRank(first.Style) <= GetRank(second.Style) ? first : second;
    }

    public static int GetRank(BorderStyle style)
    {
        var index = Array.IndexOf(RankedStyles, style);
        return index < 0 ? RankedStyles.Length : index;
    }
}
