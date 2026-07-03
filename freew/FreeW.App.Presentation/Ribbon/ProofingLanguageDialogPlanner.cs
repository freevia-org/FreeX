namespace FreeW.App.Presentation.Ribbon;

public sealed record ProofingLanguageDialogChoice(string Tag, string DisplayText);

public sealed record ProofingLanguageDialogPlan(
    IReadOnlyList<ProofingLanguageDialogChoice> Choices,
    int SelectedIndex)
{
    public ProofingLanguageDialogChoice SelectedChoice =>
        Choices.Count == 0 ? throw new InvalidOperationException("Proofing language dialog has no choices.") : Choices[SelectedIndex];
}

public static class ProofingLanguageDialogPlanner
{
    public const string ClearLanguageLabel = "(None - clear language)";

    public static ProofingLanguageDialogPlan Build(string? currentTag)
    {
        var normalizedCurrent = ProofingLanguageCatalog.NormalizeTag(currentTag) ?? string.Empty;
        var choices = new List<ProofingLanguageDialogChoice>
        {
            new(string.Empty, ClearLanguageLabel),
        };

        choices.AddRange(ProofingLanguageCatalog.CommonLanguages.Select(choice =>
            new ProofingLanguageDialogChoice(choice.Tag, $"{choice.Label} ({choice.Tag})")));

        var selectedIndex = choices.FindIndex(choice =>
            string.Equals(choice.Tag, normalizedCurrent, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = 0;

        return new ProofingLanguageDialogPlan(choices, selectedIndex);
    }
}
