using FreeX.App.Avalonia.Pivot;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Avalonia.Headless;

using System.Threading;
using System.Threading.Tasks;

namespace FreeX.App.Avalonia.Tests;

public sealed class PivotFieldListLinuxEvidenceTests
{
    private static readonly HeadlessUnitTestSession HeadlessSession =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

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

    [Fact]
    public async Task StartupLoadedPivotFixture_PreservesLiveSessionIdentityAndContext()
    {
        await HeadlessSession.Dispatch(() =>
        {
            var fixturePath = TestWorkspaceFileLocator.Find(
                "tests",
                "FreeX.App.Avalonia.Tests",
                "Fixtures",
                "FreeX_wave50_pivot_fields.xlsx");
            var startup = new StartupWorkbookLoader().Load([fixturePath]);
            startup.IsFallback.Should().BeFalse();
            startup.SourcePath.Should().Be(fixturePath);

            var session = new WorkbookSessionFactory().Create(
                startup,
                viewportHeight: 720,
                viewportWidth: 1120,
                includeObjects: true);
            var sheet = session.ActiveSheet;
            var pivot = sheet.PivotTables.Should().ContainSingle().Subject;
            var target = new CellAddress(sheet.Id, pivot.TargetRange.Start.Row, pivot.TargetRange.Start.Col);

            session.SelectCell(target);

            session.ActiveSheet.Id.Should().Be(sheet.Id);
            session.ActiveCell.Should().Be(target);
            PivotSourceContext.FindActivePivot(session.ActiveSheet, session.ActiveCell)
                .Should()
                .BeSameAs(pivot);

            var window = new MainWindow([], session);
            try
            {
                window.SelectClickedCell(target, global::Avalonia.Input.KeyModifiers.None);
                window.RibbonContextStateForTest.IsActive("pivot.active").Should().BeTrue();
                window.PivotFieldPaneVisibleForTest.Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }
}
