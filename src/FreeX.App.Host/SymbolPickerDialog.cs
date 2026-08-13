using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog : Window
{
    private static readonly IReadOnlyList<string> FontChoices = CreateFontChoices();
    private static readonly IReadOnlyList<string> SubsetChoices = SymbolPickerCatalogPlanner.GetSubsetNames();

    public char SelectedChar { get; private set; }
    public string SelectedSymbol { get; private set; } = "";

    public SymbolPickerDialog()
    {
        Title = UiText.Get("SymbolPicker_Symbol");
        Width = SymbolPickerCatalogPlanner.DialogWidth;
        Height = SymbolPickerCatalogPlanner.DialogHeight;
        MinWidth = 760;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;

        ApplySelection(SymbolPickerCatalogPlanner.CreateDefaultSelection());
        Content = CreateDialogContent();
    }

    private void ApplySelection(SymbolPickerSelectionPlan selection)
    {
        SelectedSymbol = selection.Symbol;
        SelectedChar = selection.SelectedChar;
    }

    private static IReadOnlyList<string> CreateFontChoices()
    {
        var preferredFonts = SymbolPickerCatalogPlanner.GetPreferredFontChoices();
        var installedFonts = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableFonts = preferredFonts
            .Where(installedFonts.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return availableFonts.Length >= 4
            ? availableFonts
            : preferredFonts;
    }
}
