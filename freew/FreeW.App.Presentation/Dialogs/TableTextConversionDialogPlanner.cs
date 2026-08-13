using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Dialogs;

public sealed record TableTextDelimiterChoice(string Label, char Delimiter);

public sealed record TableTextConversionDialogText(
    string TextToTableTitle,
    string TableToTextTitle,
    string PromptLabel,
    IReadOnlyList<TableTextDelimiterChoice> Choices);

public static class TableTextConversionDialogPlanner
{
    public const string PromptLabel = "Separate cells at:";

    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("TableConversion_TextToTable_Title", "Convert Text to Table"),
        new("TableConversion_TableToText_Title", "Convert Table to Text"),
        new("TableConversion_Prompt_Label", PromptLabel),
        new("TableConversion_Tab_Label", "Tab"),
        new("TableConversion_Comma_Label", "Comma  ,"),
        new("TableConversion_Semicolon_Label", "Semicolon  ;"),
    ];

    public static IReadOnlyList<TableTextDelimiterChoice> Choices { get; } =
    [
        new("Tab", '\t'),
        new("Comma  ,", ','),
        new("Semicolon  ;", ';'),
    ];

    public static int DefaultChoiceIndex => 0;

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static TableTextConversionDialogText ResolveText(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            [
                new(Texts[3].Resolve(getText), '\t'),
                new(Texts[4].Resolve(getText), ','),
                new(Texts[5].Resolve(getText), ';'),
            ]);

    public static char? DelimiterAt(int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < Choices.Count
            ? Choices[selectedIndex].Delimiter
            : null;
}
