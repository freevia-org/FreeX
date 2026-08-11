using FluentAssertions;
using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class PivotDesignCommandSourceTests
{

    [Fact]
    public void PivotDesignHandlers_RouteThroughExpectedOptionsDialogStyleGalleryAndCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotDesignCommands.cs");

        source.Should().Contain("private void PivotGrandTotalsBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void PivotSubtotalsBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void PivotReportLayoutBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ShowPivotTableOptionsDialog();");
        source.Should().Contain("private void PivotStyleGalleryBtn_Click(object sender, RoutedEventArgs e)");
        source.Should().Contain("ShowPivotStyleGalleryDialog();");
        source.Should().Contain("new PivotTableOptionsDialog(pivotTable, cache)");
        source.Should().Contain("new PivotStyleGalleryDialog(pivotTable.StyleName)");
        source.Should().Contain("PivotApplication.PlanDesignOptions(");
        source.Should().Contain("PivotApplication.PlanDialogOptions(");
        source.Should().Contain("!pivotTable.BlankLineAfterItems");
        source.Should().Contain("!pivotTable.ShowRowHeaders");
        source.Should().Contain("!pivotTable.ShowColumnHeaders");
        source.Should().Contain("!pivotTable.ShowRowStripes");
        source.Should().Contain("!pivotTable.ShowColumnStripes");
        source.Should().Contain("StyleName = dialog.Result.StyleName");
        source.Should().Contain("ApplyPivotApplicationPlan(");
        source.Should().NotContain("new ConfigurePivotTableOptionsCommand(");
    }

    private static string ReadPivotTableDesignTabXaml()
    {
        var xaml = ReadMainWindowXaml();
        var tabNameIndex = xaml.IndexOf("x:Name=\"PivotTableDesignTab\"", StringComparison.Ordinal);
        tabNameIndex.Should().BeGreaterThanOrEqualTo(0, "the PivotTable Design contextual tab should be present");

        var start = xaml.LastIndexOf("<TabItem", tabNameIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the PivotTable Design contextual tab should have a TabItem start");

        var end = xaml.IndexOf("<TabItem Header=\"{local:Loc Key=MainWindow_Header_Help}\"", tabNameIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThan(tabNameIndex, "the PivotTable Design contextual tab should end before the Help tab");

        return xaml[start..end];
    }
}
