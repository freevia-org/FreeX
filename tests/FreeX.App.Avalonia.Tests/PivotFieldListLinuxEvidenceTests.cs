using FreeX.App.Avalonia.Pivot;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class PivotFieldListLinuxEvidenceTests
{
    [Fact]
    public void WorksheetSelectionRouteRefreshesThePivotFieldPane()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.PivotTabs.cs");
        var contextualRefreshStart = source.IndexOf(
            "private void RefreshPivotContextualTab()",
            StringComparison.Ordinal);

        contextualRefreshStart.Should().BeGreaterThanOrEqualTo(0);
        source[contextualRefreshStart..].IndexOf("RefreshPivotFieldPane();", StringComparison.Ordinal)
            .Should()
            .BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void PivotFixture_LoadsWithAnActiveTargetCell()
    {
        var fixturePath = TestWorkspaceFileLocator.Find(
            "tests",
            "FreeX.App.Avalonia.Tests",
            "Fixtures",
            "FreeX_wave50_pivot_fields.xlsx");

        using var stream = File.OpenRead(fixturePath);
        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.GetSheetAt(0);
        var pivot = sheet.PivotTables.Should().ContainSingle().Subject;

        pivot.TargetRange.Start.ToA1().Should().Be("E1");
        pivot.TargetRange.End.ToA1().Should().Be("F3");
        PivotSourceContext.FindActivePivot(sheet, new CellAddress(sheet.Id, 1, 5))
            .Should()
            .BeSameAs(pivot);
    }
}
