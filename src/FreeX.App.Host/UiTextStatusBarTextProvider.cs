using Free.Shared.AppServices;

namespace FreeX.App.Host;

/// <summary>
/// Backs the neutral <see cref="IStatusBarTextProvider"/> with the host's <c>UiText</c> resources,
/// mapping each readout kind to its localized format and label resource keys. This is the WPF host's
/// adapter into the shared <see cref="StatusBarDisplayModelBuilder"/>.
/// </summary>
internal sealed class UiTextStatusBarTextProvider : IStatusBarTextProvider
{
    public static readonly UiTextStatusBarTextProvider Instance = new();

    private UiTextStatusBarTextProvider()
    {
    }

    public string GetReadoutFormat(StatusBarReadoutKind kind) =>
        UiText.Get(kind switch
        {
            StatusBarReadoutKind.Average => "StatusBar_AverageFormat",
            StatusBarReadoutKind.Count => "StatusBar_CountFormat",
            StatusBarReadoutKind.NumericalCount => "StatusBar_NumericalCountFormat",
            StatusBarReadoutKind.Sum => "StatusBar_SumFormat",
            StatusBarReadoutKind.Minimum => "StatusBar_MinFormat",
            StatusBarReadoutKind.Maximum => "StatusBar_MaxFormat",
            _ => "StatusBar_CountFormat"
        });

    public string GetReadoutLabel(StatusBarReadoutKind kind) =>
        UiText.Get(kind switch
        {
            StatusBarReadoutKind.Average => "StatusBar_Average",
            StatusBarReadoutKind.Count => "StatusBar_Count",
            StatusBarReadoutKind.NumericalCount => "StatusBar_NumericalCount",
            StatusBarReadoutKind.Sum => "StatusBar_Sum",
            StatusBarReadoutKind.Minimum => "StatusBar_Minimum",
            StatusBarReadoutKind.Maximum => "StatusBar_Maximum",
            _ => "StatusBar_Count"
        });
}
