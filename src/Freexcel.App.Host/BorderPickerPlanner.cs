using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum BorderPickerChoice
{
    All,
    Outline,
    Inside,
    Top,
    Right,
    Bottom,
    Left,
    None
}

public static class BorderPickerPlanner
{
    public static StyleDiff Plan(
        BorderPickerChoice choice,
        GridRange range,
        CellAddress address,
        CellColor color,
        BorderStyle style = BorderStyle.Thin)
    {
        var border = new CellBorder(style, color);
        var noBorder = new CellBorder(BorderStyle.None, color);

        return choice switch
        {
            BorderPickerChoice.All => new StyleDiff(
                BorderTop: border,
                BorderRight: border,
                BorderBottom: border,
                BorderLeft: border),
            BorderPickerChoice.Outline => new StyleDiff(
                BorderTop: address.Row == range.Start.Row ? border : null,
                BorderRight: address.Col == range.End.Col ? border : null,
                BorderBottom: address.Row == range.End.Row ? border : null,
                BorderLeft: address.Col == range.Start.Col ? border : null),
            BorderPickerChoice.Inside => new StyleDiff(
                BorderTop: address.Row > range.Start.Row ? border : null,
                BorderRight: address.Col < range.End.Col ? border : null,
                BorderBottom: address.Row < range.End.Row ? border : null,
                BorderLeft: address.Col > range.Start.Col ? border : null),
            BorderPickerChoice.Top => new StyleDiff(BorderTop: border),
            BorderPickerChoice.Right => new StyleDiff(BorderRight: border),
            BorderPickerChoice.Bottom => new StyleDiff(BorderBottom: border),
            BorderPickerChoice.Left => new StyleDiff(BorderLeft: border),
            BorderPickerChoice.None => new StyleDiff(
                BorderTop: noBorder,
                BorderRight: noBorder,
                BorderBottom: noBorder,
                BorderLeft: noBorder),
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null)
        };
    }
}
