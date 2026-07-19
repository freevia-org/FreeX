using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the non-UI glue backing the Avalonia Defined Names dialogs (Name Manager, Define Name,
/// Create Names from Selection): projecting the workbook's named ranges into list rows, mapping a validated
/// Define-Name draft onto the Core add/edit command, mapping the create-from-selection plan onto add
/// commands, and mapping a delete onto the remove command. The commands are run against a workbook through a
/// minimal command context to assert their effect (name / scope / refers-to). No running UI is required.
/// </summary>
public sealed class DefinedNamesShellGlueTests
{
    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new GlueTestCommandContext(workbook));

    // ── BuildRows: project stored named ranges ────────────────────────────────

    [Fact]
    public void BuildRows_ProjectsStoredNamedRanges_WithScopeAndRefersTo()
    {
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedRange("Sales", Range(sheet, 1, 1, 3, 1));
        workbook.DefineNamedRange(
            "SheetTotal",
            Range(sheet, 5, 1, 5, 1),
            new NamedRangeMetadata("Sheet1", "row total"));

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        rows.Should().HaveCount(2);

        var sales = rows.Single(r => r.Name == "Sales");
        sales.ScopeLabel.Should().Be(DefinedNameScope.WorkbookLabel);
        sales.RefersTo.Should().Be("Sheet1!A1:A3");
        sales.IsWorkbookScoped.Should().BeTrue();

        var sheetTotal = rows.Single(r => r.Name == "SheetTotal");
        sheetTotal.ScopeLabel.Should().Be("Sheet1");
        sheetTotal.RefersTo.Should().Be("Sheet1!A5");
        sheetTotal.Comment.Should().Be("row total");
        sheetTotal.IsWorkbookScoped.Should().BeFalse();
    }

    [Fact]
    public void BuildRows_ProjectsScopedNamedRanges_WithScopeAndRefersTo()
    {
        // A genuine sheet-scoped named RANGE (Excel "localSheetId"), defined via the 4-arg scoped
        // DefineNamedRange overload — not a workbook-global name with a metadata label. This lives in
        // workbook.ScopedNamedRanges, not workbook.NamedRanges, and must still show up in the Name Manager.
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedRange(
            "LocalRange",
            Range(sheet, 1, 1, 5, 1),
            new NamedRangeMetadata("Sheet1", "local only"),
            sheet.Id);

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        var localRange = rows.Should().ContainSingle().Which;
        localRange.Name.Should().Be("LocalRange");
        localRange.ScopeLabel.Should().Be("Sheet1");
        localRange.RefersTo.Should().Be("Sheet1!A1:A5");
        localRange.Comment.Should().Be("local only");
        localRange.IsWorkbookScoped.Should().BeFalse();
    }

    [Fact]
    public void BuildRows_ProjectsScopedNamedRangesAlongsideWorkbookRangesAndScopedFormulas()
    {
        // Sibling no-regression check: a scoped named range must not crowd out (or be crowded out by) the
        // workbook-global named ranges or the sheet-scoped named formulas already handled by BuildRows.
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedRange("Global", Range(sheet, 1, 1, 1, 1));
        workbook.DefineNamedRange("LocalRange", Range(sheet, 2, 1, 2, 1), NamedRangeMetadata.WorkbookScope, sheet.Id);
        workbook.DefineNamedFormula("LocalCalc", "A1*2", sheet.Id);

        var rows = DefinedNamesShellGlue.BuildRows(workbook);

        rows.Should().HaveCount(3);
        rows.Select(r => r.Name).Should().BeEquivalentTo("Global", "LocalRange", "LocalCalc");
        rows.Single(r => r.Name == "LocalRange").ScopeLabel.Should().Be("Sheet1");
        rows.Single(r => r.Name == "LocalCalc").ScopeLabel.Should().Be("Sheet1");
    }

    [Fact]
    public void ProjectRows_FiltersToWorksheetScopedNames()
    {
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedRange("Global", Range(sheet, 1, 1, 1, 1));
        workbook.DefineNamedRange(
            "Local",
            Range(sheet, 2, 1, 2, 1),
            new NamedRangeMetadata("Sheet1", ""));

        var rows = DefinedNamesShellGlue.ProjectRows(workbook, DefinedNameFilter.Worksheet);

        rows.Should().ContainSingle().Which.Name.Should().Be("Local");
    }

    // ── BuildDefineCommand: map a validated draft onto the add/edit command ────

    [Fact]
    public void BuildDefineCommand_AddsNewName_WithScopeMetadataAndRange()
    {
        var (workbook, sheet) = CreateWorkbook();
        var draft = new DefinedNameDraft(
            "Revenue",
            DefinedNameScope.ForSheet(sheet.Id, "Sheet1"),
            "Sheet1!A1:B2",
            "yearly");
        var range = Range(sheet, 1, 1, 2, 2);

        var command = DefinedNamesShellGlue.BuildDefineCommand(draft, range);
        var outcome = Run(workbook, command);

        // A sheet-scoped draft must define a sheet-scoped name (Excel "localSheetId"), not a
        // workbook-global one: it lives in ScopedNamedRanges, not the global NamedRanges dictionary.
        outcome.Success.Should().BeTrue();
        workbook.NamedRanges.Should().NotContainKey("Revenue");
        workbook.ScopedNamedRanges.Should().ContainKey(("Revenue", sheet.Id));
        workbook.TryGetNamedRange("Revenue", sheet.Id, out var stored).Should().BeTrue();
        stored.Should().Be(range);
        workbook.TryGetScopedNamedRangeMetadata("Revenue", sheet.Id, out var metadata).Should().BeTrue();
        metadata.Scope.Should().Be("Sheet1");
        metadata.Comment.Should().Be("yearly");
    }

    [Fact]
    public void BuildDefineCommand_ReplacesExistingName_WhenEditing()
    {
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedRange("Budget", Range(sheet, 1, 1, 1, 1));

        var draft = new DefinedNameDraft(
            "Budget",
            DefinedNameScope.Workbook,
            "Sheet1!A1:A10",
            "");
        var newRange = Range(sheet, 1, 1, 10, 1);

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDefineCommand(draft, newRange));

        outcome.Success.Should().BeTrue();
        workbook.NamedRanges.Should().ContainKey("Budget");
        workbook.NamedRanges["Budget"].Should().Be(newRange);
        workbook.NamedRangeMetadataByName["Budget"].Scope.Should().Be(DefinedNameScope.WorkbookLabel);
    }

    // ── BuildCreateCommands: map the create-from-selection plan onto add cmds ──

    [Fact]
    public void BuildCreateCommands_DefinesEachPlannedName()
    {
        var (workbook, sheet) = CreateWorkbook();
        // A1:B3 with column headers "Region"/"Sales" on the top row.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(sheet, 1, 1, 3, 2),
            new CreateNamesFromSelectionOptions(UseTopRow: true, UseLeftColumn: false, UseBottomRow: false, UseRightColumn: false),
            address => sheet.GetCell(address)?.Value is TextValue text ? text.Value : null,
            workbook.NamedRanges.Keys);

        plan.Should().HaveCount(2);
        plan.Select(p => p.Name).Should().Equal("Region", "Sales");

        var commands = DefinedNamesShellGlue.BuildCreateCommands(plan);
        commands.Should().HaveCount(2);
        foreach (var command in commands)
            Run(workbook, command).Success.Should().BeTrue();

        workbook.NamedRanges.Should().ContainKey("Region");
        workbook.NamedRanges.Should().ContainKey("Sales");
        // The top-row labels name the cells beneath them (rows 2-3 in each column).
        workbook.NamedRanges["Region"].Should().Be(Range(sheet, 2, 1, 3, 1));
        workbook.NamedRanges["Sales"].Should().Be(Range(sheet, 2, 2, 3, 2));
    }

    // ── BuildDeleteCommand: map a delete onto the remove command ──────────────

    [Fact]
    public void BuildDeleteCommand_RemovesTheName()
    {
        var (workbook, sheet) = CreateWorkbook();
        workbook.DefineNamedRange("Temp", Range(sheet, 1, 1, 1, 1));

        var outcome = Run(workbook, DefinedNamesShellGlue.BuildDeleteCommand("Temp"));

        outcome.Success.Should().BeTrue();
        workbook.NamedRanges.Should().NotContainKey("Temp");
    }

    // ── BuildScopeChoices ─────────────────────────────────────────────────────

    [Fact]
    public void BuildScopeChoices_ListsWorkbookThenEachSheet()
    {
        var (workbook, _) = CreateWorkbook();
        workbook.AddSheet("Sheet2");

        var choices = DefinedNamesShellGlue.BuildScopeChoices(workbook);

        choices.Select(c => c.Label).Should().Equal(DefinedNameScope.WorkbookLabel, "Sheet1", "Sheet2");
        choices[0].Scope.IsWorkbook.Should().BeTrue();
        choices[1].Scope.IsWorkbook.Should().BeFalse();
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running named-range commands against a workbook.</summary>
    private sealed class GlueTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
