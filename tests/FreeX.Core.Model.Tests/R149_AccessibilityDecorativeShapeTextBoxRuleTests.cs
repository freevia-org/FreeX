using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R149-app-accessibility-checker-decorative-shapes: Excel's "Mark as decorative" flag
/// (<see cref="PictureModel.IsDecorative"/>) was only ever modeled/consumed for pictures --
/// <see cref="DrawingShapeModel"/> and <see cref="TextBoxModel"/> had no such property, and
/// <see cref="AccessibilityCheckerService.FindIssues"/> called its shape/text-box alt-text check
/// with no isDecorative argument at all, so a shape or text box the user explicitly marked
/// decorative in Excel (with no alt text/title/name) was always flagged Missing Alt Text -- a
/// guaranteed false positive versus real Excel's own Accessibility Checker (mirrors the picture
/// fix in R90_AccessibilityDecorativePictureRuleTests).
/// </summary>
public sealed class R149_AccessibilityDecorativeShapeTextBoxRuleTests
{
    [Fact]
    public void FindIssues_DoesNotFlagShapeMarkedDecorative_EvenWithNoAltTextTitleOrName()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            IsDecorative = true
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().NotContain(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
    }

    [Fact]
    public void FindIssues_DoesNotFlagTextBoxMarkedDecorative_EvenWithNoAltTextTitleOrName()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            IsDecorative = true
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().NotContain(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
    }

    [Fact]
    public void FindIssues_StillFlagsOrdinaryShapeAndTextBoxWithNoAltText_WhenNotMarkedDecorative()
    {
        // No-regression sibling: an ordinary (non-decorative) shape/text box with no accessible
        // text must still be flagged -- the fix must not exempt every shape/text box, only ones
        // explicitly marked decorative.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            IsDecorative = false
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            IsDecorative = false
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().Contain(i => i.Kind == AccessibilityIssueKind.MissingAltText && i.Location == "B2");
        issues.Should().Contain(i => i.Kind == AccessibilityIssueKind.MissingAltText && i.Location == "C3");
    }
}
