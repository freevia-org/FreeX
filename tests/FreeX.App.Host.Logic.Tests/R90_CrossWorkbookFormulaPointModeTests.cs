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

        owner.EditSelections.Should().ContainSingle().Which.Should().Be(
            new FormulaPointModeEditSelection(
                "Input Data",
                range,
                "Source.xlsx",
                FormulaPointModeSelectionMode.Replace,
                ExtendSelection: false));
        source.LastSourceSelection.Should().Be(range);
    }

    [Fact]
    public void Resolver_RoutesCommitAndEscapeToTheOwnerAcrossWorkbookIdentities()
    {
        var owner = new FakeFormulaPointWindow("Owner.xlsx", active: true);
        var source = new FakeFormulaPointWindow("Source.xlsx", active: false);

        FormulaPointModeWorkbookResolver.TryRouteCommand(
            [owner, source],
            source,
            FormulaPointModeCommand.Commit).Should().BeTrue();
        FormulaPointModeWorkbookResolver.TryRouteCommand(
            [owner, source],
            source,
            FormulaPointModeCommand.Cancel).Should().BeTrue();
        owner.CommitCount.Should().Be(1);
        owner.CancelCount.Should().Be(1);
    }

    [Fact]
    public void Resolver_DoesNotConsumeF4WhenNoOtherPointModeOwnerExists()
    {
        var source = new FakeFormulaPointWindow("Source.xlsx", active: false);

        FormulaPointModeWorkbookResolver.TryRouteCommand(
                [source],
                source,
                FormulaPointModeCommand.CycleReference)
            .Should()
            .BeFalse();
        source.CycleCount.Should().Be(0);
    }

    [Fact]
    public void Resolver_PreparesExternalAppendSelectionForTheEditOwner()
    {
        var owner = new FakeFormulaPointWindow("Owner.xlsx", active: true);
        var source = new FakeFormulaPointWindow("Source.xlsx", active: false);
        var range = Range(SheetId.New(), 2, 2, 4, 3);

        FormulaPointModeWorkbookResolver.TryRouteSelection(
                [owner, source],
                source,
                new FormulaPointModeSelection(
                    source.DocumentId,
                    source.WorkbookName,
                    "Input Data",
                    range),
                append: true,
                extendSelection: true)
            .Should()
            .BeTrue();

        owner.EditSelections.Should().ContainSingle().Which.Should().Be(
            new FormulaPointModeEditSelection(
                "Input Data",
                range,
                "Source.xlsx",
                FormulaPointModeSelectionMode.Append,
                ExtendSelection: true));
    }

    [Fact]
    public void Resolver_OmitsWorkbookQualifierForSelectionOwnedByTheSameWorkbook()
    {
        var owner = new FakeFormulaPointWindow("Owner.xlsx", active: true);
        var range = Range(SheetId.New(), 2, 2, 4, 3);

        FormulaPointModeWorkbookResolver.TryRouteSelection(
                [owner],
                owner,
                new FormulaPointModeSelection(
                    owner.DocumentId,
                    owner.WorkbookName,
                    "Input Data",
                    range))
            .Should()
            .BeTrue();

        owner.EditSelections.Should().ContainSingle().Which.Should().Be(
            new FormulaPointModeEditSelection(
                "Input Data",
                range,
                null,
                FormulaPointModeSelectionMode.Replace,
                ExtendSelection: false));
    }

    [Fact]
    public void Resolver_CreatesSourceSelectionFromTheOwningWorkbookAndRange()
    {
        var workbook = new Workbook("Source.xlsx");
        var sheet = workbook.AddSheet("Input Data");
        var range = Range(sheet.Id, 2, 2, 4, 3);

        FormulaPointModeWorkbookResolver.TryCreateSelection(workbook, range, out var selection)
            .Should()
            .BeTrue();

        selection.Should().Be(new FormulaPointModeSelection(
            workbook.Id,
            "Source.xlsx",
            "Input Data",
            range));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void Session_RequiresEditorPointModeAndEditCellForActiveOwnership(
        bool hasEditor,
        bool pointMode,
        bool hasEditCell,
        bool expected)
    {
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(pointMode);

        session.IsPointModeActive(hasEditor, hasEditCell)
            .Should()
            .Be(expected);
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
        public List<FormulaPointModeEditSelection> EditSelections { get; } = [];
        public GridRange? LastSourceSelection { get; private set; }
        public int CommitCount { get; private set; }
        public int CancelCount { get; private set; }
        public int CycleCount { get; private set; }

        public bool AcceptFormulaPointModeSelection(FormulaPointModeEditSelection selection)
        {
            EditSelections.Add(selection);
            return true;
        }

        public void ShowFormulaPointModeSourceSelection(GridRange range) => LastSourceSelection = range;
        public bool CommitOwnedFormulaPointModeEdit() { CommitCount++; return true; }
        public bool CancelOwnedFormulaPointModeEdit() { CancelCount++; return true; }
        public bool CycleOwnedFormulaPointModeReference() { CycleCount++; return true; }
    }
}
