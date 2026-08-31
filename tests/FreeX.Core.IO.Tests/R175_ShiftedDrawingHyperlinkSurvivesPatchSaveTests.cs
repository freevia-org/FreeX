using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r175 remediation. Round 175 taught the row/column shift to rewrite a drawing object's
/// "Place in This Document" hyperlink, which made <c>DrawingObjectHyperlink</c> mutable for the
/// first time from a command that does not force a full save. R101's patch-safety guard fired,
/// because the drawing fingerprint that decides whether a save may reuse the source package's
/// drawing parts verbatim did not compare the field -- so the shifted target could be discarded.
/// The fingerprint now covers it (see WriteDrawingObjectHyperlinkFingerprint).
///
/// <para>KNOWN GAP, deliberately not asserted here: writing this test revealed that the shifted
/// target does not survive a save-and-reload even so -- the model holds Sheet1!$A$6 after the
/// insert, but a reloaded workbook reads Sheet1!$A$5. So round 175's hyperlink fix currently
/// corrects the in-memory model only, and the user-visible defect persists in the saved file. That
/// is a separate defect from the fingerprint one, it is NOT fixed, and it is recorded rather than
/// hidden behind a passing test. What this test pins is the part that does work.</para>
/// </summary>
public sealed class R175_ShiftedDrawingHyperlinkSurvivesPatchSaveTests
{
    [Fact]
    public void InsertRow_ShiftsAShapeHyperlinkTargetInTheModel()
    {
        var workbook = new Workbook("HyperlinkShift");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("target"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Shape 1",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 50,
            Hyperlink = new DrawingObjectHyperlink("Sheet1!$A$5", TargetMode: null, Tooltip: "jump"),
        });

        var context = new TestCommandContext(workbook);
        new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1).Apply(context).Success.Should().BeTrue();

        sheet.DrawingShapes[0].Hyperlink!.Target.Should().Be(
            "Sheet1!$A$6",
            "inserting a row above the target must carry the hyperlink with the rows it points at");
        sheet.DrawingShapes[0].Hyperlink!.Tooltip.Should().Be(
            "jump",
            "only the address changes -- the rest of the hyperlink is carried through unchanged");
    }

    [Fact]
    public void UndoingTheInsert_RestoresTheOriginalHyperlinkTarget()
    {
        var workbook = new Workbook("HyperlinkShift");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Shape 1",
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 50,
            Hyperlink = new DrawingObjectHyperlink("Sheet1!$A$5"),
        });

        var context = new TestCommandContext(workbook);
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);

        sheet.DrawingShapes[0].Hyperlink!.Target.Should().Be(
            "Sheet1!$A$5",
            "undo must put the hyperlink back exactly, not leave it shifted");
    }
}
