using System.IO;

using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaPrintSelectionParityTests
{
    [Fact]
    public void HasPrintSelection_RecognizesSingleCellSelection()
    {
        var workbook = new Workbook("Budget");
        var sheet = workbook.AddSheet("Sheet1");
        var singleCell = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));

        MainWindow.HasPrintSelection(singleCell).Should().BeTrue();
    }

    [Fact]
    public void HasPrintSelection_RejectsMissingSelection()
    {
        MainWindow.HasPrintSelection(null).Should().BeFalse();
    }

    [Fact]
    public void PrintAndBackstageExport_UseSharedSelectionPolicy()
    {
        var printSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Print.cs"));
        var backstageSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"));

        printSource.Should().Contain("var scopePlan = WorkbookExportScopePlanner.Build(");
        printSource.Should().Contain(
            "_session.Workbook, HasPrintSelection(_session.SelectedRange), WorkbookExportPrintSurface.MacOs);");
        var exportOptionsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ExportOptions.cs"));
        exportOptionsSource.Should().Contain("WorkbookExportInteractionPlanner.CreateCommandPlan(");
        exportOptionsSource.Should().Contain("_session.SelectedRange");
        backstageSource.Should().NotContain("HasPrintSelection(");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
