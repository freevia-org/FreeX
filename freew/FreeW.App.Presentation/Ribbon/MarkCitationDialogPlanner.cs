using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record MarkCitationCategoryChoice(CitationCategory Category, string Label)
{
    public override string ToString() => Label;
}

public sealed record MarkCitationDialogState(
    CitationCategory Category,
    string LongCitation,
    string ShortCitation);

public sealed record MarkCitationValidation(string Message);

public static class MarkCitationDialogPlanner
{
    public const string AutomationId = "MarkCitationDialog";
    public const string CategoryAutomationId = "MarkCitationCategoryComboBox";
    public const string LongCitationAutomationId = "MarkCitationLongTextBox";
    public const string ShortCitationAutomationId = "MarkCitationShortTextBox";
    public const string StatusAutomationId = "MarkCitationValidationText";
    public const double DialogWidth = 380;
    public const double ContentHorizontalMargin = 16;
    public const double ContentTopMargin = 16;
    public const double LabelBottomMargin = 4;
    public const double FieldBottomMargin = 10;
    public const double StatusBottomMargin = 8;
    public const double ActionRowTopMargin = 10;
    public const double ActionRowBottomMargin = 16;
    public const string Title = "Mark Citation";
    public const string CategoryLabel = "Category:";
    public const string LongCitationLabel = "Selected text (long citation):";
    public const string ShortCitationLabel = "Short citation (optional):";
    public const string MarkButtonLabel = "Mark";
    public const string CancelButtonLabel = "Cancel";
    public const string MissingLongCitationMessage = "Enter the long citation before marking.";

    public static IReadOnlyList<MarkCitationCategoryChoice> BuildCategoryChoices() =>
        Enum.GetValues<CitationCategory>()
            .Select(category => new MarkCitationCategoryChoice(category, TableOfAuthorities.CategoryHeading(category)))
            .ToList();

    public static MarkCitationDialogState BuildInitialState(string? seedLongCitation) =>
        new(CitationCategory.Cases, (seedLongCitation ?? string.Empty).Trim(), string.Empty);

    public static int SelectCategoryIndex(
        IReadOnlyList<MarkCitationCategoryChoice> choices,
        CitationCategory category)
    {
        ArgumentNullException.ThrowIfNull(choices);

        for (var i = 0; i < choices.Count; i++)
            if (choices[i].Category == category)
                return i;

        return 0;
    }

    public static bool TryBuildCitation(
        MarkCitationDialogState state,
        out Citation? citation,
        out MarkCitationValidation? validation)
    {
        var longCitation = state.LongCitation.Trim();
        if (longCitation.Length == 0)
        {
            citation = null;
            validation = new MarkCitationValidation(MissingLongCitationMessage);
            return false;
        }

        citation = new Citation(longCitation, state.Category, state.ShortCitation.Trim());
        validation = null;
        return true;
    }
}
