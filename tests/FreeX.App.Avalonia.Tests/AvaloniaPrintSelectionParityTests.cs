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
    public void PrintAndExportEntryPoints_UseTheSameSelectionGate()
    {
        var printSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Print.cs"));
        var backstageSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"));

        printSource.Should().Contain("var hasSelection = HasPrintSelection(_session.SelectedRange);");
        backstageSource.Should().Contain("var hasSelection = HasPrintSelection(_session.SelectedRange);");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
