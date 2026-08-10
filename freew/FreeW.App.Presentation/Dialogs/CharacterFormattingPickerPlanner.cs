using FreeW.Core.Model;
using FreeW.App.Presentation.Ribbon;

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
        FreeWRibbonPaletteCatalog.CharacterBorders
            .Where(choice => choice.Hex is not null)
            .Select(ToPickerChoice)
            .ToArray();

    public static readonly IReadOnlyList<CharacterColorChoice> ShadingPalette =
        FreeWRibbonPaletteCatalog.CharacterShading
            .Where(choice => choice.Hex is not null)
            .Select(ToPickerChoice)
            .ToArray();

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

    private static CharacterColorChoice ToPickerChoice(FreeWRibbonPaletteChoice choice) =>
        new(choice.PickerLabel ?? choice.Label, choice.Hex!);
}
