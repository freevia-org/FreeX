using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsMergedCellsAndObjectsWithoutAltText()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);

        sheet.MergedRegions.Add(new GridRange(a1, b2));
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 1),
            Kind = PictureKind.Image
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 1),
            Kind = DrawingShapeKind.Rectangle,
            AltText = "Process block"
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().Contain(i => i.Kind == AccessibilityIssueKind.MergedCells);
        issues.Should().Contain(i => i.Kind == AccessibilityIssueKind.MissingAltText);
        issues.Should().NotContain(i => i.Location.Contains("6,1", StringComparison.Ordinal));
    }
}
