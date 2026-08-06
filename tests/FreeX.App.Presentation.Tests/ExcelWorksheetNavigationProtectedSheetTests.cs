using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class ExcelWorksheetNavigationProtectedSheetTests
{
    [Theory]
    [InlineData(ExcelWorksheetNavigationKey.Right, false, 1, 2, 1, 3)]
    [InlineData(ExcelWorksheetNavigationKey.Left, false, 1, 2, 1, 1)]
    [InlineData(ExcelWorksheetNavigationKey.Down, false, 2, 1, 3, 1)]
    [InlineData(ExcelWorksheetNavigationKey.Up, false, 2, 1, 1, 1)]
    [InlineData(ExcelWorksheetNavigationKey.Enter, false, 2, 1, 3, 1)]
    [InlineData(ExcelWorksheetNavigationKey.Enter, true, 2, 1, 1, 1)]
    [InlineData(ExcelWorksheetNavigationKey.Tab, false, 1, 2, 1, 3)]
    [InlineData(ExcelWorksheetNavigationKey.Tab, true, 1, 2, 1, 1)]
    public void ResolveProtectedSheetTarget_SkipsLockedCellsInNavigationDirection(
        ExcelWorksheetNavigationKey key,
        bool shiftHeld,
        int targetRow,
        int targetCol,
        int expectedRow,
        int expectedCol)
    {
        var (workbook, sheet, unlockedStyleId) = CreateProtectedWorkbook();
        var target = new CellAddress(sheet.Id, (uint)targetRow, (uint)targetCol);
        var expected = new CellAddress(sheet.Id, (uint)expectedRow, (uint)expectedCol);
        sheet.SetStyleOnly(expected.Row, expected.Col, unlockedStyleId);

        var resolved = ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
            workbook,
            sheet,
            target,
            key,
            shiftHeld);

        resolved.Should().Be(expected);
    }

    [Fact]
    public void ResolveProtectedSheetTarget_ReturnsNullAtSheetEdgeWhenNothingIsSelectable()
    {
        var (workbook, sheet, _) = CreateProtectedWorkbook();
        var target = new CellAddress(sheet.Id, 1, 1);

        var resolved = ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
            workbook,
            sheet,
            target,
            ExcelWorksheetNavigationKey.Left,
            shiftHeld: false);

        resolved.Should().BeNull();
    }

    [Fact]
    public void ResolveProtectedSheetTarget_PreservesTargetsOutsideSkipPolicy()
    {
        var (workbook, sheet, unlockedStyleId) = CreateProtectedWorkbook();
        var lockedTarget = new CellAddress(sheet.Id, 1, 2);

        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
                workbook,
                sheet,
                lockedTarget,
                ExcelWorksheetNavigationKey.Home,
                shiftHeld: false)
            .Should()
            .Be(lockedTarget);

        sheet.ProtectionPermissions.Add(SheetProtectionPermission.SelectLockedCells);
        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
                workbook,
                sheet,
                lockedTarget,
                ExcelWorksheetNavigationKey.Right,
                shiftHeld: false)
            .Should()
            .Be(lockedTarget);

        sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
        sheet.SetStyleOnly(lockedTarget.Row, lockedTarget.Col, unlockedStyleId);
        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
                workbook,
                sheet,
                lockedTarget,
                ExcelWorksheetNavigationKey.Right,
                shiftHeld: false)
            .Should()
            .Be(lockedTarget);

        sheet.IsProtected = false;
        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(
                workbook,
                sheet,
                new CellAddress(sheet.Id, 1, 3),
                ExcelWorksheetNavigationKey.Right,
                shiftHeld: false)
            .Should()
            .Be(new CellAddress(sheet.Id, 1, 3));
    }

    [Fact]
    public void FreeXRenderers_DelegateProtectedNavigationTargetsToPresentationPlanner()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var hostRoot = Path.Combine(repoRoot, "src", "FreeX.App.Host");
        var avaloniaRoot = Path.Combine(repoRoot, "src", "FreeX.App.Avalonia");
        var presentationSource = File.ReadAllText(Path.Combine(
            presentationRoot,
            "ExcelWorksheetNavigationPlanner.cs"));
        var adapterSource = File.ReadAllText(Path.Combine(hostRoot, "ExcelWorksheetNavigationPlanner.cs"));
        var wpfSource = File.ReadAllText(Path.Combine(hostRoot, "MainWindow.Selection.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(avaloniaRoot, "MainWindow.cs"));

        presentationSource.Should().Contain("public static CellAddress? ResolveProtectedSheetTarget(");
        adapterSource.Should().Contain(
            "FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(");

        foreach (var rendererSource in new[] { wpfSource, avaloniaSource })
        {
            rendererSource.Should().Contain("ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(");
            rendererSource.Should().NotContain("GetProtectedNavigationStep(");
            rendererSource.Should().NotContain("FindNextSelectableCellInDirection(");
        }
    }

    private static (Workbook Workbook, Sheet Sheet, StyleId UnlockedStyleId) CreateProtectedWorkbook()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
        return (workbook, sheet, unlockedStyleId);
    }
}
