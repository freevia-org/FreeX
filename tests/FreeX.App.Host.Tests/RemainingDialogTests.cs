using FluentAssertions;
using System.IO;
using System.Reflection;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    private static char GetAccessKey(string label)
    {
        var marker = label.IndexOf('_', StringComparison.Ordinal);
        marker.Should().BeGreaterThanOrEqualTo(0);
        marker.Should().BeLessThan(label.Length - 1);
        return char.ToUpperInvariant(label[marker + 1]);
    }

    private static string ReadRemainingDialogSources()
    {
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RemainingDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PageBreakDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ForecastSheetDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SheetNameDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "UnhideSheetDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "FillSeriesStepDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ZoomDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SparklineDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SpellCheckDialog.cs")));
    }

    private static string ReadStatusDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "GoalSeekStatusDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WorkbookStatisticsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "StatusDialogKeyboardFocus.cs")));

    private static string ReadPrintPreviewDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.Layout.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewDialog.Helpers.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewSettingsPanelFactory.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PrintPreviewToolbarPlanner.cs")));

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName));
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        if (end < 0)
            end = source.Length;
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string ReadObjectDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "HyperlinkDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextEntryDialogs.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ThreadedCommentDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ObjectSizingDialogs.cs")));

    private static T GetField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

}
