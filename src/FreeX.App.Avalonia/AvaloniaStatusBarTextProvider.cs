using Free.Shared.AppServices;

namespace FreeX.App.Avalonia;

/// <summary>
/// Backs the neutral <see cref="IStatusBarTextProvider"/> for the Avalonia port. The Avalonia shell has
/// no <c>UiText</c> resource system yet, so it supplies the same English label/format strings the WPF
/// host resolves from its <c>StatusBar_*</c> resources, keeping the shared
/// <see cref="StatusBarDisplayModelBuilder"/> output identical across shells.
/// </summary>
internal sealed class AvaloniaStatusBarTextProvider : IStatusBarTextProvider
{
    public static readonly AvaloniaStatusBarTextProvider Instance = new();

    private AvaloniaStatusBarTextProvider()
    {
    }

    public string GetReadoutFormat(StatusBarReadoutKind kind) =>
        kind switch
        {
            StatusBarReadoutKind.Average => "Average: {0}",
            StatusBarReadoutKind.Count => "Count: {0}",
            StatusBarReadoutKind.NumericalCount => "Numerical Count: {0}",
            StatusBarReadoutKind.Sum => "Sum: {0}",
            StatusBarReadoutKind.Minimum => "Min: {0}",
            StatusBarReadoutKind.Maximum => "Max: {0}",
            _ => "Count: {0}"
        };

    public string GetReadoutLabel(StatusBarReadoutKind kind) =>
        kind switch
        {
            StatusBarReadoutKind.Average => "Average",
            StatusBarReadoutKind.Count => "Count",
            StatusBarReadoutKind.NumericalCount => "Numerical Count",
            StatusBarReadoutKind.Sum => "Sum",
            StatusBarReadoutKind.Minimum => "Minimum",
            StatusBarReadoutKind.Maximum => "Maximum",
            _ => "Count"
        };
}
