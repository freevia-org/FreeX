using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    private static string ReadPivotWorkflowSource()
    {
        return DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "\n",
            "PivotFieldGroupingDialog.cs",
            "PivotTableDataSourceDialog.cs",
            "PivotChartTypeDialog.cs",
            "PivotDialogLayout.cs",
            "PivotChartOptionsDialog.cs",
            "PivotSlicerTimelineDialogs.cs",
            "PivotCalculatedDialogs.cs",
            "PivotStyleCatalog.cs",
            "PivotStyleGalleryDialog.cs",
            "PivotTableOptionsDialog.cs",
            "PivotTableOptionsDialog.Result.cs");
    }

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
        => DialogSourceTestSupport.ReadClassSource(fileName, startMarker, endMarker);

    private static T GetPrivateField<T>(object instance, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(instance, name);

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
