using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FormulaAuditingServiceTests
{
    [Fact]
    public void GetDirectPrecedents_ReturnsCellsFromRefsRangesCrossSheetRefsAndNamedRanges()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var formulaAddress = new CellAddress(sheet1.Id, 5, 1);
        var namedStart = new CellAddress(sheet1.Id, 10, 1);
        var namedEnd = new CellAddress(sheet1.Id, 11, 1);
        wb.DefineNamedRange("Rates", new GridRange(namedStart, namedEnd));

        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:B2,Sheet2!C3,Rates)"));

        var precedents = FormulaAuditingService.GetDirectPrecedents(wb, formulaAddress);

        precedents.Should().Equal(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 1, 2),
            new CellAddress(sheet1.Id, 2, 1),
            new CellAddress(sheet1.Id, 2, 2),
            namedStart,
            namedEnd,
            new CellAddress(sheet2.Id, 3, 3));
    }

    [Fact]
    public void GetPrecedentTraceArrows_ReturnsMultiLevelFormulaChain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var arrows = FormulaAuditingService.GetPrecedentTraceArrows(wb, c1);

        arrows.Should().Equal(
            new FormulaTraceArrow(b1, c1),
            new FormulaTraceArrow(a1, b1));
    }

    [Fact]
    public void GetDirectDependents_ReturnsFormulaCellsThatReferenceAddress()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var target = new CellAddress(sheet1.Id, 2, 1);
        var localDependent = new CellAddress(sheet1.Id, 1, 2);
        var rangeDependent = new CellAddress(sheet1.Id, 4, 1);
        var crossSheetDependent = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(localDependent, Cell.FromFormula("A2*2"));
        sheet1.SetCell(rangeDependent, Cell.FromFormula("SUM(A1:A3)"));
        sheet2.SetCell(crossSheetDependent, Cell.FromFormula("Sheet1!A2"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), Cell.FromFormula("Sheet1!A3"));

        var dependents = FormulaAuditingService.GetDirectDependents(wb, target);

        dependents.Should().Equal(localDependent, rangeDependent, crossSheetDependent);
    }

    [Fact]
    public void GetDependentTraceArrows_ReturnsMultiLevelFormulaChain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(b1, Cell.FromFormula("A1+1"));
        sheet.SetCell(c1, Cell.FromFormula("B1*2"));

        var arrows = FormulaAuditingService.GetDependentTraceArrows(wb, a1);

        arrows.Should().Equal(
            new FormulaTraceArrow(a1, b1),
            new FormulaTraceArrow(b1, c1));
    }

    [Fact]
    public void FindFormulaErrors_ReturnsFormulaCellsWithCachedErrorsInSheetOrder()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);

        var later = Cell.FromFormula("1/0");
        later.Value = ErrorValue.DivByZero;
        sheet.SetCell(a2, later);

        var earlier = Cell.FromFormula("MISSING()");
        earlier.Value = ErrorValue.Name;
        sheet.SetCell(b1, earlier);

        var errors = FormulaAuditingService.FindFormulaErrors(wb, sheet.Id);

        errors.Should().HaveCount(2);
        errors[0].Address.Should().Be(b1);
        errors[0].FormulaText.Should().Be("MISSING()");
        errors[0].Error.Should().Be(ErrorValue.Name);
        errors[1].Address.Should().Be(a2);
        errors[1].FormulaText.Should().Be("1/0");
        errors[1].Error.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void FindFormulaErrors_CanLimitResultsToRequestedSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), ErrorValue.Ref);
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), ErrorValue.Value);

        var errors = FormulaAuditingService.FindFormulaErrors(wb, sheet2.Id);

        errors.Should().ContainSingle();
        errors[0].SheetId.Should().Be(sheet2.Id);
        errors[0].SheetName.Should().Be("Sheet2");
        errors[0].Address.Should().Be(new CellAddress(sheet2.Id, 1, 1));
        errors[0].FormulaText.Should().BeNull();
        errors[0].Error.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsUserFacingIssueMessages()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 3);
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        sheet.SetCell(address, cell);

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.SheetName.Should().Be("Sheet1");
        issue.Cell.Should().Be("C2");
        issue.ErrorCode.Should().Be("#DIV/0!");
        issue.FormulaText.Should().Be("=1/0");
        issue.Description.Should().Contain("division by zero");
    }

    [Fact]
    public void FindFormulaErrorIssues_DetectsNumbersStoredAsTextAndSkipsNonnumericText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var numericText = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(numericText, new TextValue("123.45"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("ordinary text"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("   "));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("NaN"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Infinity"));
        var formulaAddress = new CellAddress(sheet.Id, 6, 1);
        var formula = Cell.FromFormula("\"123\"");
        formula.Value = new TextValue("123");
        sheet.SetCell(formulaAddress, formula);

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Address.Should().Be(numericText);
        issue.ErrorCode.Should().Be(FormulaErrorCheckingRuleCatalog.NumberStoredAsTextCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("number stored as text");
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledNumberStoredAsTextRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("123"));
        var disable = new SetFormulaErrorCheckingRuleCommand(
            FormulaErrorCheckingRuleCatalog.NumberStoredAsTextCode,
            enabled: false);

        disable.Apply(new SimpleCtx(wb)).Success.Should().BeTrue();

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsIgnoredNumberStoredAsTextCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var cell = Cell.FromValue(new TextValue("123"));
        cell.IgnoreFormulaError = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_DetectsFormulasThatReferToBlankCellsAcrossRefsAndRanges()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var localFormula = new CellAddress(sheet1.Id, 1, 1);
        var rangeFormula = new CellAddress(sheet1.Id, 2, 1);
        var crossSheetFormula = new CellAddress(sheet1.Id, 3, 1);
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), BlankValue.Instance);
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), BlankValue.Instance);
        sheet1.SetCell(localFormula, Cell.FromFormula("B1+1"));
        sheet1.SetCell(rangeFormula, Cell.FromFormula("SUM(B2:C2)"));
        sheet1.SetCell(crossSheetFormula, Cell.FromFormula("Sheet2!A1+1"));

        var issues = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet1.Id);

        issues.Select(issue => (issue.Address, issue.ErrorCode, issue.FormulaText))
            .Should().Equal(
                (localFormula, FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode, "=B1+1"),
                (rangeFormula, FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode, "=SUM(B2:C2)"),
                (crossSheetFormula, FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode, "=Sheet2!A1+1"));
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledFormulaRefersToBlankCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromFormula("B1+1"));
        var disable = new SetFormulaErrorCheckingRuleCommand(
            FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode,
            enabled: false);

        disable.Apply(new SimpleCtx(wb)).Success.Should().BeTrue();

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsMixedIssuesInDeterministicCellOrder()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var textAddress = new CellAddress(sheet.Id, 1, 1);
        var blankRefAddress = new CellAddress(sheet.Id, 1, 2);
        var errorAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(errorAddress, ErrorValue.Value);
        sheet.SetCell(blankRefAddress, Cell.FromFormula("C1+1"));
        sheet.SetCell(textAddress, new TextValue("42"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Select(issue => (issue.Cell, issue.ErrorCode))
            .Should().Equal(
                ("A1", FormulaErrorCheckingRuleCatalog.NumberStoredAsTextCode),
                ("B1", FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode),
                ("A2", ErrorValue.Value.Code));
    }

    [Fact]
    public void FindFormulaErrors_SkipsIgnoredFormulaErrors()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ignoredAddress = new CellAddress(sheet.Id, 1, 1);
        var visibleAddress = new CellAddress(sheet.Id, 2, 1);
        var ignored = Cell.FromFormula("1/0");
        ignored.Value = ErrorValue.DivByZero;
        ignored.IgnoreFormulaError = true;
        var visible = Cell.FromFormula("MISSING()");
        visible.Value = ErrorValue.Name;
        sheet.SetCell(ignoredAddress, ignored);
        sheet.SetCell(visibleAddress, visible);

        FormulaAuditingService.FindFormulaErrors(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.Address.Should().Be(visibleAddress);
    }

    [Fact]
    public void FindFormulaErrors_SkipsDisabledErrorCheckingRules()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var div0Address = new CellAddress(sheet.Id, 1, 1);
        var nameAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(div0Address, new Cell { FormulaText = "1/0", Value = ErrorValue.DivByZero });
        sheet.SetCell(nameAddress, new Cell { FormulaText = "MISSING()", Value = ErrorValue.Name });
        wb.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);

        FormulaAuditingService.FindFormulaErrors(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.Address.Should().Be(nameAddress);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_SetsStateAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        sheet.SetCell(address, cell);
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeTrue();

        command.Revert(ctx);

        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeFalse();
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_CanIgnoreNonErrorIssueAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("123"));
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        sheet.GetCell(address)!.IgnoreFormulaError.Should().BeFalse();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().ContainSingle();
    }

    [Fact]
    public void SetFormulaErrorCheckingRuleCommand_TogglesRuleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorCheckingRuleCommand(ErrorValue.DivByZero.Code, enabled: false);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.DisabledFormulaErrorCodes.Should().Contain(ErrorValue.DivByZero.Code);

        command.Revert(ctx);

        wb.DisabledFormulaErrorCodes.Should().BeEmpty();
    }

    [Fact]
    public void FormulaErrorCheckingRuleCatalog_ListsSupportedOptionsInStableExcelLikeOrder()
    {
        FormulaErrorCheckingRuleCatalog.SupportedRules.Select(rule => (rule.ErrorCode, rule.Label))
            .Should().Equal(
                (ErrorValue.DivByZero.Code, "Formulas that divide by zero"),
                (ErrorValue.Value.Code, "Formulas with incompatible values"),
                (ErrorValue.Ref.Code, "Formulas with invalid cell references"),
                (ErrorValue.Name.Code, "Formulas with unrecognized names"),
                (ErrorValue.NA.Code, "Formulas returning #N/A"),
                (ErrorValue.Num.Code, "Formulas with invalid numbers"),
                (ErrorValue.Null.Code, "Formulas with invalid intersections"),
                (ErrorValue.Spill.Code, "Formulas with blocked spill ranges"),
                (ErrorValue.Circular.Code, "Formulas with circular references"),
                (FormulaErrorCheckingRuleCatalog.NumberStoredAsTextCode, "Numbers stored as text"),
                (FormulaErrorCheckingRuleCatalog.FormulaRefersToBlankCellsCode, "Formulas referring to blank cells"));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
