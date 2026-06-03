using System.IO;
using System.Reflection;
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
        return string.Join(
            "\n",
            new[]
            {
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
                "PivotTableOptionsDialog.Result.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName));
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = string.IsNullOrEmpty(endMarker)
            ? source.Length
            : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
            end = source.Length;
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

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
