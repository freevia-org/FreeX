using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FieldPickerChoice(string Category, string Label, string Instruction);

public sealed record DocumentPropertyFieldCommandPlan(string CommandId, RunFieldKind Kind);

public static class FieldPickerDialogPlanner
{
    public const string Title = "Insert Field";
    public const string Prompt = "Choose a field to insert:";

    public static readonly IReadOnlyList<FieldPickerChoice> Choices =
    [
        new("Date and Time", "Date (DATE)", @" DATE \@ ""M/d/yyyy"" "),
        new("Date and Time", "Time (TIME)", @" TIME \@ ""h:mm am/pm"" "),
        new("Document Information", "Author (AUTHOR)", " AUTHOR "),
        new("Document Information", "File Name (FILENAME)", " FILENAME "),
        new("Document Information", "Title (TITLE)", " TITLE "),
        new("Document Information", "Subject (SUBJECT)", " SUBJECT "),
        new("Document Information", "Keywords (KEYWORDS)", " KEYWORDS "),
        new("Document Information", "Comments (COMMENTS)", " COMMENTS "),
        new("Document Information", "Template (TEMPLATE)", " TEMPLATE "),
        new("Document Information", "Revision Number (REVNUM)", " REVNUM "),
        new("Numbering", "Page Number (PAGE)", " PAGE "),
        new("Numbering", "Number of Pages (NUMPAGES)", " NUMPAGES "),
        new("References", "StyleRef - heading style ref (STYLEREF)", " STYLEREF 1 "),
        new("References", "Sequence number (SEQ Figure)", " SEQ Figure \\* ARABIC "),
    ];

    public static IReadOnlyList<string> Categories =>
        Choices.Select(choice => choice.Category).Distinct().ToList();

    public static IReadOnlyList<FieldPickerChoice> ChoicesForCategory(string? category) =>
        Choices.Where(choice => choice.Category == category).ToList();

    public static bool TryGetInstruction(string? category, string? label, out string instruction)
    {
        var choice = Choices.FirstOrDefault(choice =>
            choice.Category == category && choice.Label == label);

        if (choice is null)
        {
            instruction = string.Empty;
            return false;
        }

        instruction = choice.Instruction;
        return true;
    }
}

public static class DocumentPropertyFieldPlanner
{
    public static readonly IReadOnlyList<DocumentPropertyFieldCommandPlan> CommandPlans =
    [
        new("freew.docprop-title", RunFieldKind.Title),
        new("freew.docprop-subject", RunFieldKind.Subject),
        new("freew.docprop-author", RunFieldKind.Author),
        new("freew.docprop-keywords", RunFieldKind.Keywords),
        new("freew.docprop-comments", RunFieldKind.DocComments),
    ];
}
