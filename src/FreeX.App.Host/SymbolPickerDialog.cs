using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog : Window
{
    private static readonly IReadOnlyList<string> FontChoices = CreateFontChoices();

    public char SelectedChar { get; private set; }
    public string SelectedSymbol { get; private set; } = "";

    public readonly record struct SymbolCatalogEntry(string Symbol, string Name, string Subset, string CodeText)
    {
        internal static SymbolCatalogEntry FromPresentation(SymbolPickerCatalogEntry entry) =>
            new(entry.Symbol, entry.Name, entry.Subset, entry.CodeText);

        public string AutomationName => CreateSymbolAutomationName(Symbol);
        public string SearchText => $"{Symbol} {Name} {Subset} U+{CodeText}";
        public string ToolTipText => $"{Name} (U+{CodeText})";
    }

    public readonly record struct SpecialCharacter(string Name, string Symbol, string Shortcut = "")
    {
        internal static SpecialCharacter FromPresentation(SymbolPickerSpecialCharacter special) =>
            new(special.Name, special.Symbol, special.Shortcut);

        public string CodeText => SymbolPickerCatalogPlanner.FormatCodeText(Symbol);
        public string DisplaySymbol => SymbolPickerCatalogPlanner.CreateDisplaySymbol(Symbol);
        public string AutomationName => UiText.Format("SymbolPicker_SpecialCharacterAutomationNameFormat", Name, CreateSymbolAutomationName(Symbol));
        public string SearchText => $"{Name} {Symbol} {DisplaySymbol} {Shortcut} U+{CodeText}";
    }

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

    public static IReadOnlyList<string> GetFontChoices() => FontChoices;

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
