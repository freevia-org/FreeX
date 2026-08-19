using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class DefinedNamesSessionTests
{
    [Fact]
    public void ScopeChoices_KeepWorkbookSentinelDistinctFromSheetNamedWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Workbook");
        var session = new DefinedNamesSession(workbook, sheet.Id);

        var choices = session.ScopeChoices;

        choices.Should().HaveCount(2);
        choices.Select(scope => scope.Label).Should().Equal("Workbook", "Workbook");
        choices[0].IsWorkbook.Should().BeTrue();
        choices[1].SheetId.Should().Be(sheet.Id);
        choices[0].HasSameIdentity(choices[1]).Should().BeFalse();
    }

    [Fact]
    public void BuildRows_ProjectsAllStorageKindsWithRealScopeAndComputedPreviews()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));
        workbook.DefineNamedRange("GlobalCell", Cell(sheet, 1, 1));
        workbook.DefineNamedRange("LocalRange", Range(sheet, 2, 1, 3, 1), new NamedRangeMetadata("Sheet1", "local"), sheet.Id);
        workbook.NamedFormulas["TaxRate"] = "1+38";
        workbook.DefineNamedFormula("LocalFormula", "2+3", sheet.Id);

        var rows = new DefinedNamesSession(workbook, sheet.Id).BuildRows();

        rows.Single(row => row.Name == "GlobalCell").Should().Match<DefinedNameRow>(row =>
            row.Scope.IsWorkbook && row.RefersTo == "Sheet1!A1:A1" && row.Value == "42");
        rows.Single(row => row.Name == "LocalRange").Should().Match<DefinedNameRow>(row =>
            row.Scope.SheetId == sheet.Id && row.Value == "{100;200}" && row.Comment == "local");
        rows.Single(row => row.Name == "TaxRate").Should().Match<DefinedNameRow>(row =>
            row.RefersTo == "=1+38" && row.Value == "39" && row.Kind == DefinedNameKind.Formula);
        rows.Single(row => row.Name == "LocalFormula").Value.Should().Be("5");
    }

    [Fact]
    public void BuildRows_ShowsValuePreviewForNamedSingleCellSpillMember()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, new NumberValue(10));
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(10) },
            { new NumberValue(20) }
        }));
        // Row 2 is a spill member (owned by the anchor's array formula), not a real cell.
        workbook.DefineNamedRange("SpillMember", Cell(sheet, 2, 1));

        var rows = new DefinedNamesSession(workbook, sheet.Id).BuildRows();

        rows.Single(row => row.Name == "SpillMember").Value.Should().Be("20");
    }

    [Fact]
    public void BuildRows_ShowsValuePreviewForNamedRangeCoveringSpillMembers()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(anchor, new NumberValue(10));
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(10) },
            { new NumberValue(20) },
            { new NumberValue(30) }
        }));
        // Named range covers the anchor plus both spill members.
        workbook.DefineNamedRange("SpillRange", Range(sheet, 1, 1, 3, 1));

        var rows = new DefinedNamesSession(workbook, sheet.Id).BuildRows();

        rows.Single(row => row.Name == "SpillRange").Value.Should().Be("{10;20;30}");
    }

    [Fact]
    public void ProjectRows_FiltersWorkbookAndSameLabelWorksheetByIdentity()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Workbook");
        workbook.DefineNamedRange("Global", Cell(sheet, 1, 1));
        workbook.DefineNamedRange("Local", Cell(sheet, 2, 1), NamedRangeMetadata.WorkbookScope, sheet.Id);
        var session = new DefinedNamesSession(workbook, sheet.Id);

        session.ProjectRows(DefinedNameFilter.Workbook).Select(row => row.Name).Should().Equal("Global");
        session.ProjectRows(DefinedNameFilter.Worksheet).Select(row => row.Name).Should().Equal("Local");
    }

    [Fact]
    public void ValidateDraft_HandlesStructureRefersToAndScopeAwareDuplicates()
    {
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        workbook.DefineNamedRange("Rate", Cell(sheet1, 1, 1), NamedRangeMetadata.WorkbookScope, sheet1.Id);
        var session = new DefinedNamesSession(workbook, sheet1.Id);

        session.ValidateDraft(new DefinedNameDraft("Rate", session.GetScope(sheet1.Id), "=1"))
            .Name.Error.Should().Be(DefinedNameError.Duplicate);
        session.ValidateDraft(new DefinedNameDraft("Rate", session.GetScope(sheet2.Id), "=1"))
            .IsValid.Should().BeTrue();
        session.ValidateDraft(new DefinedNameDraft("A1", DefinedNameScope.Workbook, "=1"))
            .Name.Error.Should().Be(DefinedNameError.LooksLikeReference);
        session.ValidateDraft(new DefinedNameDraft("Rate2", DefinedNameScope.Workbook, ""))
            .RefersTo.Error.Should().Be(RefersToError.Blank);
    }

    [Fact]
    public void ValidateDraft_ExcludesOriginalOnlyWhenScopeIdentityIsUnchanged()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Workbook");
        workbook.DefineNamedRange("Rate", Cell(sheet, 1, 1));
        workbook.DefineNamedRange("Rate", Cell(sheet, 2, 1), NamedRangeMetadata.WorkbookScope, sheet.Id);
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var local = session.BuildRows().Single(row => row.Name == "Rate" && !row.Scope.IsWorkbook);

        session.ValidateDraft(new DefinedNameDraft("Rate", local.Scope, "=2"), local.Identity)
            .IsValid.Should().BeTrue();
        session.ValidateDraft(new DefinedNameDraft("Rate", DefinedNameScope.Workbook, "=2"), local.Identity)
            .Name.Error.Should().Be(DefinedNameError.Duplicate);
    }

    [Fact]
    public void PlanSave_BuildsRangeAndFormulaCommandsForExactScope()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var scope = session.GetScope(sheet.Id);

        var rangePlan = session.PlanSave(new DefinedNameDraft("Sales", scope, "Sheet1!A1:A2", "rows"));
        var formulaPlan = session.PlanSave(new DefinedNameDraft(
            "Rate",
            DefinedNameScope.Workbook,
            "=1+2",
            "Standard rate"));

        rangePlan.Command.Should().BeOfType<DefineNamedRangeCommand>();
        formulaPlan.Command.Should().BeOfType<DefineNamedFormulaCommand>();
        Run(workbook, rangePlan.Command!).Success.Should().BeTrue();
        Run(workbook, formulaPlan.Command!).Success.Should().BeTrue();
        workbook.ScopedNamedRanges[("Sales", sheet.Id)].Should().Be(Range(sheet, 1, 1, 2, 1));
        workbook.NamedFormulas["Rate"].Should().Be("1+2");
        workbook.NamedRangeMetadataByName["Rate"].Comment.Should().Be("Standard rate");
        session.BuildRows().Single(row => row.Name == "Rate").Comment.Should().Be("Standard rate");
    }

    [Fact]
    public void PlanSave_PersistsAndProjectsSheetScopedFormulaComment()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var plan = session.PlanSave(new DefinedNameDraft(
            "LocalRate",
            session.GetScope(sheet.Id),
            "=0.08",
            "Local sales tax"));

        Run(workbook, plan.Command!).Success.Should().BeTrue();

        workbook.ScopedNamedFormulas[("LocalRate", sheet.Id)].Should().Be("0.08");
        workbook.TryGetScopedNamedRangeMetadata("LocalRate", sheet.Id, out var metadata).Should().BeTrue();
        metadata.Comment.Should().Be("Local sales tax");
        session.BuildRows().Single(row => row.Name == "LocalRate").Comment.Should().Be("Local sales tax");
    }

    [Fact]
    public void PlanSave_KindChangeIsAtomicAndUndoRestoresOriginalDefinition()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("Rate", Cell(sheet, 1, 1));
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var original = session.BuildRows().Single().Identity;
        var plan = session.PlanSave(new DefinedNameDraft("Rate", DefinedNameScope.Workbook, "=1+2"), original);

        plan.Command.Should().BeOfType<CompositeWorkbookCommand>();
        Run(workbook, plan.Command!).Success.Should().BeTrue();
        workbook.NamedRanges.Should().NotContainKey("Rate");
        workbook.NamedFormulas["Rate"].Should().Be("1+2");

        plan.Command!.Revert(new TestContext(workbook));
        workbook.NamedRanges["Rate"].Should().Be(Cell(sheet, 1, 1));
        workbook.NamedFormulas.Should().NotContainKey("Rate");
    }

    [Fact]
    public void DeleteCommand_UsesRowIdentityForSheetNamedWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Workbook");
        workbook.DefineNamedRange("Rate", Cell(sheet, 1, 1));
        workbook.DefineNamedRange("Rate", Cell(sheet, 2, 1), NamedRangeMetadata.WorkbookScope, sheet.Id);
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var local = session.BuildRows().Single(row => !row.Scope.IsWorkbook);

        Run(workbook, session.BuildDeleteCommand(local)).Success.Should().BeTrue();

        workbook.NamedRanges.Should().ContainKey("Rate");
        workbook.ScopedNamedRanges.Should().NotContainKey(("Rate", sheet.Id));
    }

    [Fact]
    public void CreateFromSelection_UsesExistingFormulaNamespaceAndBuildsGuardedCommands()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        workbook.NamedFormulas["Region"] = "1";
        var session = new DefinedNamesSession(workbook, sheet.Id);
        var plan = session.PlanCreateNamesFromSelection(
            Range(sheet, 1, 1, 3, 2),
            new CreateNamesFromSelectionOptions(true, false, false, false),
            address => (sheet.GetCell(address)?.Value as TextValue)?.Value);

        plan.Select(item => item.Name).Should().Equal("Region_2", "Sales");
        foreach (var command in session.BuildCreateCommands(plan))
            Run(workbook, command).Success.Should().BeTrue();
        workbook.NamedFormulas.Should().ContainKey("Region");
        workbook.NamedRanges.Should().ContainKeys("Region_2", "Sales");
    }

    private static GridRange Cell(Sheet sheet, uint row, uint col) => Range(sheet, row, col, row, col);

    private static GridRange Range(Sheet sheet, uint row1, uint col1, uint row2, uint col2) =>
        new(new CellAddress(sheet.Id, row1, col1), new CellAddress(sheet.Id, row2, col2));

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new TestContext(workbook));

    private sealed class TestContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException();
    }
}
