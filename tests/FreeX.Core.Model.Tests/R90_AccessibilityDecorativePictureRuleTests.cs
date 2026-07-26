using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-app-accessibility-checker-5-2: FreeX had no "mark as decorative" concept anywhere in the
/// model, so a picture the user intentionally marked decorative (Excel's Alt Text pane "Mark as
/// decorative" checkbox) with no alt text/title/name was always flagged as Missing Alt Text -- a
/// guaranteed false positive versus real Excel's own Accessibility Checker. Drives the real product
/// entry point, <see cref="AccessibilityCheckerService.FindIssues"/>.
/// </summary>
public sealed class R90_AccessibilityDecorativePictureRuleTests
{
    [Fact]
    public void FindIssues_DoesNotFlagPictureMarkedDecorative_EvenWithNoAltTextTitleOrName()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            IsDecorative = true
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().NotContain(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
    }

    [Fact]
    public void FindIssues_StillFlagsOrdinaryPictureWithNoAltText_WhenNotMarkedDecorative()
    {
        // No-regression sibling: an ordinary (non-decorative) picture with no accessible text must
        // still be flagged -- the fix must not exempt every picture, only ones explicitly marked
        // decorative.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            IsDecorative = false
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook);

        issues.Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.MissingAltText);
    }
}
