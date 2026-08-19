using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R149-app-accessibility-checker-decorative-shapes: a shape's or text box's "Mark as decorative"
/// flag (<see cref="DrawingShapeModel.IsDecorative"/>/<see cref="TextBoxModel.IsDecorative"/>) must
/// round-trip through a real .xlsx save/load, mirroring
/// <see cref="R90_AccessibilityDecorativePictureRoundTripTests"/> for pictures -- otherwise simply
/// opening and resaving a workbook containing a decorative shape/text box in FreeX permanently loses
/// the marking. Drives the real product entry point, <see cref="XlsxFileAdapter.Save"/>/
/// <see cref="XlsxFileAdapter.Load"/>.
/// </summary>
public sealed class R149_AccessibilityDecorativeShapeTextBoxRoundTripTests
{
    [Fact]
    public void SaveThenLoad_PreservesDecorativeFlag_ForShape_AndAccessibilityCheckerStaysExempt()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DecorativeShapeRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 96,
            Height = 64,
            IsDecorative = true
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedShape = reloaded.GetSheet("Sheet1")!.DrawingShapes
            .Should().ContainSingle("the shape must survive the round-trip").Subject;
        reloadedShape.IsDecorative.Should().BeTrue(
            "the 'Mark as decorative' extension must round-trip through save/load");

        var issues = AccessibilityCheckerService.FindIssues(reloaded);
        issues.Should().NotContain(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
    }

    [Fact]
    public void SaveThenLoad_PreservesDecorativeFlag_ForTextBox_AndAccessibilityCheckerStaysExempt()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DecorativeTextBoxRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Width = 96,
            Height = 64,
            IsDecorative = true
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedTextBox = reloaded.GetSheet("Sheet1")!.TextBoxes
            .Should().ContainSingle("the text box must survive the round-trip").Subject;
        reloadedTextBox.IsDecorative.Should().BeTrue(
            "the 'Mark as decorative' extension must round-trip through save/load");

        var issues = AccessibilityCheckerService.FindIssues(reloaded);
        issues.Should().NotContain(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
    }

    [Fact]
    public void SaveThenLoad_LeavesOrdinaryShapeAndTextBoxFlagged_WhenNotMarkedDecorative()
    {
        // No-regression sibling: an ordinary (non-decorative) shape/text box with no explicit alt
        // text/title must still round-trip as non-decorative and still be flagged after reload --
        // the fix must not make every shape/text box exempt.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("OrdinaryShapeTextBoxRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 96,
            Height = 64,
            IsDecorative = false
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Width = 96,
            Height = 64,
            IsDecorative = false
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        reloadedSheet.DrawingShapes.Should().ContainSingle().Subject.IsDecorative.Should().BeFalse();
        reloadedSheet.TextBoxes.Should().ContainSingle().Subject.IsDecorative.Should().BeFalse();

        var issues = AccessibilityCheckerService.FindIssues(reloaded);
        issues.Should().Contain(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
        issues.Count(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText)
            .Should().Be(2);
    }
}
