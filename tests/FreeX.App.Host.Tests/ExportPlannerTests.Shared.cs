using System.Windows;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
{
    private static string ExportSummary(params string[] parts) =>
        UiText.Format("Export_OptionsSentence", string.Join(UiText.Get("Export_OptionsSeparator"), parts));

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalChildren<T>(child))
                yield return descendant;
        }
    }
}
