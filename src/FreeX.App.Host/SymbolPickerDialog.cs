using System.Windows;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog : Window
{
    private static readonly string[] FontChoices = ["Segoe UI Symbol", "Calibri", "Arial", "Times New Roman"];

    public char SelectedChar { get; private set; }
    public string SelectedSymbol { get; private set; } = "";

    public readonly record struct SpecialCharacter(string Name, string Symbol);

    public SymbolPickerDialog()
    {
        Title = "Symbol";
        Width = 620;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        ApplySelection(SymbolPickerSelectionPlanner.CreateInitialSelection(GetSymbolsForSubset(SubsetChoices[0])));
        Content = CreateDialogContent();
    }

    private void ApplySelection(SymbolPickerSelection selection)
    {
        SelectedSymbol = selection.Symbol;
        SelectedChar = selection.SelectedChar;
    }
}
