using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
            "PivotStyleGalleryDialog.cs",
            "PivotTableOptionsDialog.cs");
    }

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
        => DialogSourceTestSupport.ReadClassSource(fileName, startMarker, endMarker);

}
