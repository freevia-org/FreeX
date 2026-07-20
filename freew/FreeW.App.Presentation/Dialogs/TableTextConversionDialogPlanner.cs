namespace FreeW.App.Presentation.Dialogs;

public sealed record TableTextDelimiterChoice(string Label, char Delimiter);

public static class TableTextConversionDialogPlanner
{
    public const string PromptLabel = "Separate cells at:";

    public static IReadOnlyList<TableTextDelimiterChoice> Choices { get; } =
    [
        new("Tab", '\t'),
        new("Comma  ,", ','),
        new("Semicolon  ;", ';'),
    ];

    public static int DefaultChoiceIndex => 0;

    public static char? DelimiterAt(int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < Choices.Count
            ? Choices[selectedIndex].Delimiter
            : null;
}
