using System.Windows;
using System.Windows.Media;
using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog : Window
{
    private static readonly IReadOnlyList<string> FontChoices = CreateFontChoices();

    public char SelectedChar { get; private set; }
    public string SelectedSymbol { get; private set; } = "";

    public readonly record struct SymbolCatalogEntry(string Symbol, string Name, string Subset, string CodeText)
    {
        public string AutomationName => CreateSymbolAutomationName(Symbol);
        public string SearchText => $"{Symbol} {Name} {Subset} U+{CodeText}";
        public string ToolTipText => $"{Name} (U+{CodeText})";
    }

    public readonly record struct SpecialCharacter(string Name, string Symbol, string Shortcut = "")
    {
        public string CodeText => SymbolPickerSelectionPlanner.FormatCodeText(Symbol);
        public string DisplaySymbol => Symbol switch
        {
            "\u00a0" => "NBSP",
            "\u00ad" => "SHY",
            _ => Symbol
        };
        public string AutomationName => UiText.Format("SymbolPicker_SpecialCharacterAutomationNameFormat", Name, CreateSymbolAutomationName(Symbol));
        public string SearchText => $"{Name} {Symbol} {DisplaySymbol} {Shortcut} U+{CodeText}";
    }

    public SymbolPickerDialog()
    {
        Title = UiText.Get("SymbolPicker_Symbol");
        Width = 840;
        Height = 620;
        MinWidth = 760;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;

        ApplySelection(SymbolPickerSelectionPlanner.CreateInitialSelection(GetSymbolsForSubset(SubsetChoices[0])));
        Content = CreateDialogContent();
    }

    private void ApplySelection(SymbolPickerSelection selection)
    {
        SelectedSymbol = selection.Symbol;
        SelectedChar = selection.SelectedChar;
    }

    public static IReadOnlyList<string> GetFontChoices() => FontChoices;

    private static IReadOnlyList<string> CreateFontChoices()
    {
        string[] preferredFonts =
        [
            "Segoe UI Symbol",
            "Segoe UI Emoji",
            "Segoe UI Historic",
            "Segoe UI",
            "Calibri",
            "Cambria Math",
            "Arial",
            "Times New Roman",
            "Courier New",
            "Consolas",
            "Symbol",
            "Wingdings",
            "Wingdings 2",
            "Wingdings 3",
            "Webdings"
        ];

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
