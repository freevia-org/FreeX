using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record CharacterColorChoice(string Label, string Hex);

public sealed record CharacterBorderPickerResult(bool Accepted, ParagraphBorder? Border);

public sealed record CharacterShadingPickerResult(bool Accepted, string? Hex);

public readonly record struct CharacterFormattingPickerLayout(
    double PanelMargin,
    double PaletteWidth,
    double SwatchSize,
    double SwatchMargin,
    double ClearTopMargin,
    double ClearHorizontalMargin,
    double ClearHorizontalPadding,
    string SwatchBorderHex);

/// <summary>
/// Shared palette and result semantics for WPF and Avalonia character border/shading pickers.
/// Cancellation is distinct from accepted No Border/No Color choices.
/// </summary>
public static class CharacterFormattingPickerPlanner
{
    public const string BorderTitle = "Character Border";
    public const string ShadingTitle = "Character Shading";
    public const string BorderPrompt = "Choose border colour:";
    public const string NoBorderLabel = "No Border";
    public const string NoColorLabel = "No Color";

    public static readonly CharacterFormattingPickerLayout Layout = new(
        PanelMargin: 8,
        PaletteWidth: 6 * 26,
        SwatchSize: 22,
        SwatchMargin: 2,
        ClearTopMargin: 6,
        ClearHorizontalMargin: 2,
        ClearHorizontalPadding: 8,
        SwatchBorderHex: "#808080");

    public static readonly IReadOnlyList<CharacterColorChoice> BorderPalette =
    [
        new("Black", "#000000"), new("Red", "#FF0000"), new("Blue", "#0070C0"),
        new("Green", "#00B050"), new("Gold", "#FFC000"), new("Purple", "#7030A0"),
        new("Gray", "#808080"), new("Dark Red", "#C00000"), new("Dark Blue", "#002060"),
        new("Dark Green", "#375623"), new("Brown", "#974706"), new("Dark Gray", "#3F3F3F"),
    ];

    public static readonly IReadOnlyList<CharacterColorChoice> ShadingPalette =
    [
        new("Yellow", "#FFFF00"), new("Green", "#92D050"), new("Cyan", "#00B0F0"),
        new("Gold", "#FFC000"), new("Red", "#FF0000"), new("Gray", "#D9D9D9"),
        new("Dark Gray", "#A6A6A6"), new("Light Yellow", "#FFF2CC"), new("Light Blue", "#DEEBF7"),
        new("Light Green", "#E2EFDA"), new("Light Orange", "#FCE4D6"), new("Light Gray", "#EDEDED"),
    ];

    public static CharacterBorderPickerResult SelectBorder(int index)
    {
        var choice = ChoiceAt(BorderPalette, index);
        return new CharacterBorderPickerResult(
            Accepted: true,
            new ParagraphBorder(choice.Hex, 0.5) { LineStyle = BorderLineStyle.Single });
    }

    public static CharacterBorderPickerResult SelectNoBorder() =>
        new(Accepted: true, Border: null);

    public static CharacterBorderPickerResult CancelBorder() =>
        new(Accepted: false, Border: null);

    public static CharacterShadingPickerResult SelectShading(int index) =>
        new(Accepted: true, Hex: ChoiceAt(ShadingPalette, index).Hex);

    public static CharacterShadingPickerResult SelectNoColor() =>
        new(Accepted: true, Hex: null);

    public static CharacterShadingPickerResult CancelShading() =>
        new(Accepted: false, Hex: null);

    private static CharacterColorChoice ChoiceAt(IReadOnlyList<CharacterColorChoice> choices, int index)
    {
        if (index < 0 || index >= choices.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return choices[index];
    }
}
