namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    private static string ExportSummary(params string[] parts) =>
        UiText.Format("Export_OptionsSentence", string.Join(UiText.Get("Export_OptionsSeparator"), parts));
}
