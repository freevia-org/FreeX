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
    public void FindFormulaErrorIssues_ReturnsNumbersStoredAsText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(address, new TextValue("42"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.SheetName.Should().Be("Sheet1");
        issue.Cell.Should().Be("B3");
        issue.ErrorCode.Should().Be(FormulaAuditingService.NumberStoredAsTextErrorCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("number in this cell is formatted as text");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaRefersToBlankCellsForDirectRefsRangesAndCrossSheetRefs()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var formulaAddress = new CellAddress(sheet1.Id, 4, 4);

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), BlankValue.Instance);
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), BlankValue.Instance);
        sheet1.SetCell(formulaAddress, Cell.FromFormula("SUM(A1:B1,Sheet2!B2,C1)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet1.Id)
            .Should().ContainSingle().Subject;

        issue.SheetName.Should().Be("Sheet1");
        issue.Cell.Should().Be("D4");
        issue.ErrorCode.Should().Be(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode);
        issue.FormulaText.Should().Be("=SUM(A1:B1,Sheet2!B2,C1)");
        issue.Description.Should().Contain("blank cells");
    }

    [Theory]
    [InlineData("1/2/24")]
    [InlineData("01-02-24")]
    [InlineData("Jan 2, 24")]
    public void FindFormulaErrorIssues_ReturnsTextDatesWithTwoDigitYears(string value)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue(value));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.Cell.Should().Be("A2");
        issue.ErrorCode.Should().Be(FormulaAuditingService.TwoDigitYearTextDateErrorCode);
        issue.FormulaText.Should().BeNull();
        issue.Description.Should().Contain("two-digit year");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsInconsistentFormulaInRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("B2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("A2*2"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.InconsistentFormulaErrorCode).Subject;

        issue.Cell.Should().Be("C3");
        issue.FormulaText.Should().Be("=A2*2");
        issue.Description.Should().Contain("inconsistent with nearby formulas");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsInconsistentFormulaInColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("A1*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("A1*2"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.InconsistentFormulaErrorCode).Subject;

        issue.Cell.Should().Be("B3");
        issue.FormulaText.Should().Be("=A1*2");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsInColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromFormula("SUM(B1:B3)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("B5");
        issue.FormulaText.Should().Be("=SUM(B1:B3)");
        issue.Description.Should().Contain("omits adjacent cells");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsFormulaOmitsAdjacentCellsInRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), Cell.FromFormula("SUM(A2:C2)"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode).Subject;

        issue.Cell.Should().Be("E2");
        issue.FormulaText.Should().Be("=SUM(A2:C2)");
    }

    [Fact]
    public void FindFormulaErrorIssues_ReturnsUnlockedFormulaCells()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var unlockedStyleId = wb.RegisterStyle(new CellStyle { Locked = false });
        var address = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        var cell = Cell.FromFormula("B2*2");
        cell.StyleId = unlockedStyleId;
        sheet.SetCell(address, cell);

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle(i => i.ErrorCode == FormulaAuditingService.UnlockedFormulaCellsErrorCode).Subject;

        issue.Cell.Should().Be("B3");
        issue.FormulaText.Should().Be("=B2*2");
        issue.Description.Should().Contain("unlocked");
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledFormulaRefersToBlankCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("A1+1"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
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
    public void FindFormulaErrorIssues_SkipsDisabledNumbersStoredAsTextRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("42"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.NumberStoredAsTextErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledTwoDigitYearTextDateRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("1/2/24"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.TwoDigitYearTextDateErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledInconsistentFormulaRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("B2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromFormula("A2*2"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.InconsistentFormulaErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledFormulaOmitsAdjacentCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromFormula("SUM(A1:A2)"));
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_SkipsDisabledUnlockedFormulaCellsRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var unlockedStyleId = wb.RegisterStyle(new CellStyle { Locked = false });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        var cell = Cell.FromFormula("A1+1");
        cell.StyleId = unlockedStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), cell);
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.UnlockedFormulaCellsErrorCode);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().BeEmpty();
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
    public void SetFormulaErrorIgnoredCommand_IgnoresNumberStoredAsTextIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("42"));
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.NumberStoredAsTextErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresFormulaRefersToBlankCellsIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(address, Cell.FromFormula("A1+1"));
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresInconsistentFormulaIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromFormula("A2*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("B2*2"));
        sheet.SetCell(address, Cell.FromFormula("A2*2"));
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.InconsistentFormulaErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresFormulaOmitsAdjacentCellsIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));
        sheet.SetCell(address, Cell.FromFormula("SUM(A1:A2)"));
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode);
    }

    [Fact]
    public void SetFormulaErrorIgnoredCommand_IgnoresUnlockedFormulaCellsIssues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var unlockedStyleId = wb.RegisterStyle(new CellStyle { Locked = false });
        var address = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        var cell = Cell.FromFormula("A1+1");
        cell.StyleId = unlockedStyleId;
        sheet.SetCell(address, cell);
        var ctx = new SimpleCtx(wb);

        var command = new SetFormulaErrorIgnoredCommand(sheet.Id, address, ignored: true);

        command.Apply(ctx).Success.Should().BeTrue();
        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        command.Revert(ctx);

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle()
            .Which.ErrorCode.Should().Be(FormulaAuditingService.UnlockedFormulaCellsErrorCode);
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
                (FormulaAuditingService.InconsistentFormulaErrorCode, "Formulas inconsistent with nearby formulas"),
                (FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode, "Formulas which omit cells in a region"),
                (FormulaAuditingService.UnlockedFormulaCellsErrorCode, "Unlocked cells containing formulas"),
                (FormulaAuditingService.FormulaRefersToBlankCellsErrorCode, "Formulas referring to blank cells"),
                (FormulaAuditingService.TwoDigitYearTextDateErrorCode, "Cells containing years represented as 2 digits"),
                (FormulaAuditingService.NumberStoredAsTextErrorCode, "Numbers formatted as text or preceded by an apostrophe"));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
