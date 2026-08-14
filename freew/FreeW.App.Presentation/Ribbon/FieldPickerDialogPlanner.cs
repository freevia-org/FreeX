using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record FieldPickerChoice(string Category, string Label, string Instruction);

public sealed record DocumentPropertyFieldCommandPlan(
    string CommandId,
    string LegacyCommandId,
    string Label,
    string KeyTip,
    RunFieldKind Kind);

public static class FieldPickerDialogPlanner
{
    public const string Title = "Insert Field";
    public const string Prompt = "Choose a field to insert:";

    public static readonly IReadOnlyList<FieldPickerChoice> Choices =
    [
        new("Date and Time", "Date (DATE)", @" DATE \@ ""M/d/yyyy"" "),
        new("Date and Time", "Time (TIME)", @" TIME \@ ""h:mm am/pm"" "),
        new("Date and Time", "Print Date (PRINTDATE)", @" PRINTDATE \@ ""M/d/yyyy h:mm am/pm"" "),
        new("Document Information", "Author (AUTHOR)", " AUTHOR "),
        new("Document Information", "File Name (FILENAME)", " FILENAME "),
        new("Document Information", "Title (TITLE)", " TITLE "),
        new("Document Information", "Subject (SUBJECT)", " SUBJECT "),
        new("Document Information", "Keywords (KEYWORDS)", " KEYWORDS "),
        new("Document Information", "Comments (COMMENTS)", " COMMENTS "),
        new("Document Information", "Template (TEMPLATE)", " TEMPLATE "),
        new("Document Information", "Revision Number (REVNUM)", " REVNUM "),
        new("Document Information", "Edit Time (EDITTIME)", " EDITTIME "),
        new("Equations and Formulas", "Formula (=)", @" =2*(3+4) \# ""0.00"" "),
        new("Numbering", "Page Number (PAGE)", " PAGE "),
        new("Numbering", "Number of Pages (NUMPAGES)", " NUMPAGES "),
        new("Numbering", "Section Number (SECTION)", " SECTION "),
        new("Numbering", "Number of Section Pages (SECTIONPAGES)", " SECTIONPAGES "),
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
        new("freew.docprop-title", "freew.quick-parts.title", "Document Property: Title", "T", RunFieldKind.Title),
        new("freew.docprop-subject", "freew.quick-parts.subject", "Document Property: Subject", "S", RunFieldKind.Subject),
        new("freew.docprop-author", "freew.quick-parts.author", "Document Property: Author", "A", RunFieldKind.Author),
        new("freew.docprop-keywords", "freew.quick-parts.keywords", "Document Property: Keywords", "K", RunFieldKind.Keywords),
        new("freew.docprop-comments", "freew.quick-parts.comments", "Document Property: Comments", "C", RunFieldKind.DocComments),
    ];

    public static void RegisterCommands(
        IRibbonCommandRegistry registry,
        Action<RunFieldKind> insertField)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(insertField);

        foreach (var plan in CommandPlans)
        {
            var command = new ActionRibbonCommand(() => insertField(plan.Kind));
            registry.Register(plan.CommandId, command);
            registry.Register(plan.LegacyCommandId, command);
        }
    }
}
