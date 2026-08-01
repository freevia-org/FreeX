using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class R90_CrossWorkbookFormulaPointModeTests
{
    [Fact]
    public void Resolver_RoutesSelectionToLiveOwnerAndKeepsSourceChromeOnSourceWindow()
    {
        var owner = new FakeFormulaPointWindow("Owner.xlsx", active: true);
        var source = new FakeFormulaPointWindow("Source.xlsx", active: false);
        var range = Range(SheetId.New(), 2, 2, 4, 3);
        var selection = new FormulaPointModeSelection(
            source.DocumentId,
            source.WorkbookName,
            "Input Data",
            range);

        FormulaPointModeWorkbookResolver.TryRouteSelection(
                [owner, source],
                source,
                selection)
            .Should()
            .BeTrue();

        owner.Selections.Should().ContainSingle().Which.Should().Be(selection);
        source.LastSourceSelection.Should().Be(range);
    }

    [Fact]
    public void Resolver_RoutesCommitAndEscapeToTheOwnerAcrossWorkbookIdentities()
    {
        var owner = new FakeFormulaPointWindow("Owner.xlsx", active: true);
        var source = new FakeFormulaPointWindow("Source.xlsx", active: false);

        FormulaPointModeWorkbookResolver.TryRouteCommit([owner, source], source).Should().BeTrue();
        FormulaPointModeWorkbookResolver.TryRouteCancel([owner, source], source).Should().BeTrue();
        owner.CommitCount.Should().Be(1);
        owner.CancelCount.Should().Be(1);
    }

    [Fact]
    public void Planner_PreservesExternalWorkbookQualifierForReplacementAndAppend()
    {
        var sourceSheet = SheetId.New();
        var first = Range(sourceSheet, 2, 2, 2, 2);
        var second = Range(sourceSheet, 4, 3, 4, 3);

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(", 5, 0, null, null, first, FormulaCell, false, out var firstEdit,
                "Input Data", selectedWorkbookName: "Source.xlsx")
            .Should().BeTrue();
        firstEdit.TextEdit.Text.Should().Be("=SUM('[Source.xlsx]Input Data'!B2");

        FormulaRangeEntryPlanner.TryAppendDisjointRangeSelection(
                firstEdit.TextEdit.Text,
                firstEdit.ReferenceStart,
                firstEdit.ReferenceLength,
                second,
                FormulaCell,
                false,
                out var appendEdit,
                "Input Data",
                selectedWorkbookName: "Source.xlsx")
            .Should().BeTrue();
        appendEdit.TextEdit.Text.Should().Be(
            "=SUM('[Source.xlsx]Input Data'!B2,'[Source.xlsx]Input Data'!C4");
    }

    [Fact]
    public void F4_CyclesReferenceWithoutDroppingExternalWorkbookQualifier()
    {
        var formula = "=SUM('[Source.xlsx]Input Data'!$B$2)";

        ExcelTextEditorPlanner.TryCycleFormulaReference(formula, formula.IndexOf("$B$2", StringComparison.Ordinal) + 2, out var edit)
            .Should().BeTrue();

        edit.Text.Should().Be("=SUM('[Source.xlsx]Input Data'!B$2)");
    }

    private static CellAddress FormulaCell => new(SheetId.New(), 10, 5);

    private static GridRange Range(SheetId sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));

    private sealed class FakeFormulaPointWindow(string name, bool active) : IFormulaPointModeWorkbookWindow
    {
        public WorkbookId DocumentId { get; } = new(Guid.NewGuid());
        public string WorkbookName { get; } = name;
        public bool HasActiveFormulaPointMode => active;
        public List<FormulaPointModeSelection> Selections { get; } = [];
        public GridRange? LastSourceSelection { get; private set; }
        public int CommitCount { get; private set; }
        public int CancelCount { get; private set; }

        public bool AcceptFormulaPointModeSelection(
            FormulaPointModeSelection selection,
            bool append,
            bool extendSelection)
        {
            Selections.Add(selection);
            return true;
        }

        public void ShowFormulaPointModeSourceSelection(GridRange range) => LastSourceSelection = range;
        public bool CommitOwnedFormulaPointModeEdit() { CommitCount++; return true; }
        public bool CancelOwnedFormulaPointModeEdit() { CancelCount++; return true; }
    }
}
