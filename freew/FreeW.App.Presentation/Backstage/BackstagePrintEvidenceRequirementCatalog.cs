namespace FreeW.App.Presentation.Backstage;

public static class BackstagePrintEvidenceRequirementCatalog
{
    public const string WpfHostId = "wpf-fidelity-render";
    public const string AvaloniaHostId = "avalonia-page-layout-shot";
    public const string PrintPreviewScenarioId = "backstage-print-preview-fidelity";
    public const string PdfExportScenarioId = "backstage-pdf-export-fidelity";
    public const int MinimumExpectedOutputs = 2;

    public static IReadOnlyList<BackstagePrintEvidenceRequirement> Build(
        BackstagePrintEvidenceKind kind)
    {
        var scenarioId = kind switch
        {
            BackstagePrintEvidenceKind.PrintPreviewFidelity => PrintPreviewScenarioId,
            BackstagePrintEvidenceKind.PdfExportFidelity => PdfExportScenarioId,
            _ => null
        };

        return scenarioId is null
            ? []
            :
            [
                new(WpfHostId, scenarioId, MinimumExpectedOutputs),
                new(AvaloniaHostId, scenarioId, MinimumExpectedOutputs)
            ];
    }
}
