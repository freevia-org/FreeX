namespace Free.Shared.AppServices;

/// <summary>
/// English status-bar label/format strings for shells that do not have a localized resource adapter.
/// WPF keeps using its UiText-backed provider; Avalonia uses this shared fallback.
/// </summary>
public sealed class EnglishStatusBarTextProvider : IStatusBarTextProvider
{
    public static readonly EnglishStatusBarTextProvider Instance = new();

    private EnglishStatusBarTextProvider()
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
