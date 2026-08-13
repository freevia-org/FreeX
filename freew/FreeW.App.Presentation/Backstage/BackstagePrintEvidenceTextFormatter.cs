namespace FreeW.App.Presentation.Backstage;

public static class BackstagePrintEvidenceTextFormatter
{
    public static string Format(BackstagePrintEvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var scenarios = row.FixtureScenarioIds.Count == 0
            ? BackstageViewTextResources.NoEvidenceFixtureScenario
            : string.Join(", ", row.FixtureScenarioIds);
        var requirements = row.Requirements.Count == 0
            ? BackstageViewTextResources.NoEvidenceRequirement
            : string.Join(", ", row.Requirements.Select(FormatRequirement));

        return $"{KindLabel(row.Kind)} - {StatusLabel(row.Status)}\n{row.Description}\n" +
            $"{BackstageViewTextResources.EvidenceScenariosLabel}: {scenarios}\n" +
            $"{BackstageViewTextResources.EvidenceRequirementsLabel}: {requirements}";
    }

    public static string KindLabel(BackstagePrintEvidenceKind kind) => kind switch
    {
        BackstagePrintEvidenceKind.PrintPreviewFidelity => BackstageViewTextResources.PrintPreviewEvidenceLabel,
        BackstagePrintEvidenceKind.PdfExportFidelity => BackstageViewTextResources.PdfExportEvidenceLabel,
        BackstagePrintEvidenceKind.NativePrint => BackstageViewTextResources.NativePrintEvidenceLabel,
        _ => kind.ToString()
    };

    public static string StatusLabel(BackstagePrintEvidenceStatus status) => status switch
    {
        BackstagePrintEvidenceStatus.FixtureReady => BackstageViewTextResources.FixtureReadyEvidenceStatus,
        BackstagePrintEvidenceStatus.HostBacked => BackstageViewTextResources.HostBackedEvidenceStatus,
        BackstagePrintEvidenceStatus.Deferred => BackstageViewTextResources.DeferredEvidenceStatus,
        _ => status.ToString()
    };

    private static string FormatRequirement(BackstagePrintEvidenceRequirement requirement) =>
        $"{requirement.HostId}/{requirement.ScenarioId} >= {requirement.MinimumExpectedOutputs}";
}
