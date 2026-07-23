namespace FreeW.App.Presentation.Dialogs;

public sealed record CellShadingColorChoice(string Label, string Hex);

public sealed record CellShadingDialogResult(bool Accepted, string? Hex);

/// <summary>
/// Shared palette and result semantics for the WPF and Avalonia table-cell shading pickers.
/// Cancellation is distinct from an accepted <c>No Color</c> choice, which intentionally carries
/// a null hex value and clears the selected cell fill.
/// </summary>
public static class CellShadingDialogPlanner
{
    public const string Title = "Cell Shading";
    public const string NoColorLabel = "No Color";

    public static readonly IReadOnlyList<CellShadingColorChoice> Palette =
    [
        new("Yellow", "#FFFF00"),
        new("Green", "#92D050"),
        new("Cyan", "#00B0F0"),
        new("Gold", "#FFC000"),
        new("Red", "#FF0000"),
        new("Gray", "#D9D9D9"),
        new("Dark Gray", "#A6A6A6"),
        new("Light Yellow", "#FFF2CC"),
        new("Light Blue", "#DEEBF7"),
        new("Light Green", "#E2EFDA"),
        new("Light Orange", "#FCE4D6"),
        new("Light Gray", "#EDEDED"),
    ];

    public static CellShadingDialogResult SelectPaletteColor(int index)
    {
        if (index < 0 || index >= Palette.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        return new CellShadingDialogResult(Accepted: true, Palette[index].Hex);
    }

    public static CellShadingDialogResult SelectNoColor() =>
        new(Accepted: true, Hex: null);

    public static CellShadingDialogResult Cancel() =>
        new(Accepted: false, Hex: null);
}
