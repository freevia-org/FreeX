using FluentAssertions;

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
        return DialogSourceTestSupport.ReadHostSources(
            "RemainingDialogs.cs",
            "PageBreakDialog.cs",
            "ForecastSheetDialog.cs",
            "SheetNameDialog.cs",
            "UnhideSheetDialog.cs",
            "FillSeriesStepDialog.cs",
            "ZoomDialog.cs",
            "SparklineDialog.cs",
            "SpellCheckDialog.cs");
    }

    private static string ReadStatusDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "GoalSeekStatusDialog.cs",
            "WorkbookStatisticsDialog.cs",
            "AccessibilityCheckerDialog.cs",
            "StatusDialogKeyboardFocus.cs");

    private static string ReadPrintPreviewDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "PrintPreviewDialog.cs",
            "PrintPreviewDialog.Layout.cs",
            "PrintPreviewDialog.Helpers.cs",
            "PrintPreviewSettingsPanelFactory.cs",
            "PrintPreviewToolbarPlanner.cs");

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
        => DialogSourceTestSupport.ReadClassSource(fileName, startMarker, endMarker);

    private static string ReadObjectDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "HyperlinkDialog.cs",
            "TextEntryDialogs.cs",
            "ThreadedCommentDialog.cs",
            "ObjectSizingDialogs.cs");

    private static T GetField<T>(object instance, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(instance, name);

}
