using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Ribbon;

public sealed record ProofingLanguageDialogChoice(string Tag, string DisplayText);

public sealed record ProofingLanguageDialogText(
    string Title,
    string LanguageLabel,
    string Instruction,
    string ClearLanguageLabel,
    string OkLabel,
    string CancelLabel);

public sealed record ProofingLanguageDialogPlan(
    IReadOnlyList<ProofingLanguageDialogChoice> Choices,
    int SelectedIndex,
    ProofingLanguageDialogText Text)
{
    public ProofingLanguageDialogChoice SelectedChoice =>
        Choices.Count == 0 ? throw new InvalidOperationException("Proofing language dialog has no choices.") : Choices[SelectedIndex];
}

public static class ProofingLanguageDialogPlanner
{
    public const string ClearLanguageLabel = "(None - clear language)";

    private static readonly ResourceTextDescriptor[] DialogTexts =
    [
        new("ProofingLanguage_Dialog_Title", "Set Proofing Language"),
        new("ProofingLanguage_Language_Label", "Language:"),
        new("ProofingLanguage_Instruction", "Select the proofing language for the selected text:"),
        new("ProofingLanguage_Clear_Label", ClearLanguageLabel),
        new("Common_Ok", "OK"),
        new("Common_Cancel", "Cancel"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        DialogTexts.Select(text => text.ResourceKey).ToArray();

    public static ProofingLanguageDialogText ResolveText(Func<string, string?>? getText = null) =>
        new(
            DialogTexts[0].Resolve(getText),
            DialogTexts[1].Resolve(getText),
            DialogTexts[2].Resolve(getText),
            DialogTexts[3].Resolve(getText),
            DialogTexts[4].Resolve(getText),
            DialogTexts[5].Resolve(getText));

    public static ProofingLanguageDialogPlan Build(
        string? currentTag,
        Func<string, string?>? getText = null)
    {
        var text = ResolveText(getText);
        var normalizedCurrent = ProofingLanguageCatalog.NormalizeTag(currentTag) ?? string.Empty;
        var choices = new List<ProofingLanguageDialogChoice>
        {
            new(string.Empty, text.ClearLanguageLabel),
        };

        choices.AddRange(ProofingLanguageCatalog.CommonLanguages.Select(choice =>
            new ProofingLanguageDialogChoice(choice.Tag, $"{choice.Label} ({choice.Tag})")));

        var selectedIndex = choices.FindIndex(choice =>
            string.Equals(choice.Tag, normalizedCurrent, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = 0;

        return new ProofingLanguageDialogPlan(choices, selectedIndex, text);
    }
}
