using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for R74-commands-name-manager-4-1. Define Name must reject an identifier
/// already owned by a range, formula, or table in the target scope before native command execution.
/// The duplicate policy and command construction now live in <see cref="DefinedNamesSession"/>.
/// </summary>
public sealed class R74_DefineNameDuplicateCheckTests
{
    [Fact]
    public void PlanSave_ExistingWorkbookNamedFormula_IsDuplicate()
    {
        var (workbook, sheet, session) = CreateSession();
        workbook.NamedFormulas["Revenue"] = "0.08";

        var plan = PlanRange(session, "Revenue", DefinedNameScope.Workbook, sheet);

        plan.Validation.Name.Error.Should().Be(DefinedNameError.Duplicate);
        plan.Command.Should().BeNull();
        workbook.NamedFormulas["Revenue"].Should().Be("0.08");
    }

    [Fact]
    public void PlanSave_ExistingWorkbookNamedRange_IsDuplicate()
    {
        var (workbook, sheet, session) = CreateSession();
        workbook.DefineNamedRange("Sales", Cell(sheet));

        var plan = PlanRange(session, "Sales", DefinedNameScope.Workbook, sheet);

        plan.Validation.Name.Error.Should().Be(DefinedNameError.Duplicate);
        plan.Command.Should().BeNull();
    }

    [Fact]
    public void PlanSave_ExistingSheetFormula_OnlyConflictsInItsOwnScope()
    {
        var (workbook, sheet, session) = CreateSession();
        workbook.DefineNamedFormula("LocalRate", "0.05", sheet.Id);

        var localPlan = PlanRange(session, "LocalRate", session.GetScope(sheet.Id), sheet);
        var workbookPlan = PlanRange(session, "LocalRate", DefinedNameScope.Workbook, sheet);

        localPlan.Validation.Name.Error.Should().Be(DefinedNameError.Duplicate);
        localPlan.Command.Should().BeNull();
        workbookPlan.Validation.Name.IsValid.Should().BeTrue();
        workbookPlan.Command.Should().NotBeNull();
    }

    [Fact]
    public void PlanSave_BrandNewName_IsDefinable()
    {
        var (_, sheet, session) = CreateSession();

        var plan = PlanRange(session, "BrandNewName", DefinedNameScope.Workbook, sheet);

        plan.IsValid.Should().BeTrue();
    }

    [Fact]
    public void DefineNameBtnClick_ValidatesSharedPlanBeforeExecutingNativeCommand()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");
        var clickHandlerStart = source.IndexOf("private void DefineNameBtn_Click", StringComparison.Ordinal);
        var clickHandlerEnd = source.IndexOf("private void CreateNamesFromSelectionBtn_Click", StringComparison.Ordinal);
        clickHandlerStart.Should().BeGreaterThanOrEqualTo(0);
        clickHandlerEnd.Should().BeGreaterThan(clickHandlerStart);
        var handlerSource = source[clickHandlerStart..clickHandlerEnd];

        var planSaveIndex = handlerSource.IndexOf("var plan = definedNames.PlanSave(draft);", StringComparison.Ordinal);
        var validationIndex = handlerSource.IndexOf("if (!plan.Validation.Name.IsValid)", StringComparison.Ordinal);
        var executeIndex = handlerSource.IndexOf("TryExecuteCommand(", StringComparison.Ordinal);

        planSaveIndex.Should().BeGreaterThanOrEqualTo(0);
        validationIndex.Should().BeGreaterThan(planSaveIndex);
        executeIndex.Should().BeGreaterThan(validationIndex);
        handlerSource.Should().Contain("UiText.Get(\"NameDefinition_NameConflictsMessage\")");
    }

    private static (Workbook Workbook, Sheet Sheet, DefinedNamesSession Session) CreateSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new DefinedNamesSession(workbook, sheet.Id));
    }

    private static DefinedNameCommandPlan PlanRange(
        DefinedNamesSession session,
        string name,
        DefinedNameScope scope,
        Sheet sheet) =>
        session.PlanSave(new DefinedNameDraft(name, scope, $"{sheet.Name}!A1:A1"));

    private static GridRange Cell(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
}
