using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public class ReferenceGeometryTests
{
    [Fact]
    public void Offset_UsesCachedArgumentsAndKeepsFullAxisBoundsWithoutMaterializing()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3));
        var evaluator = new FormulaEvaluator();
        var dynamicResult = evaluator.ResolveReferenceGeometry("=OFFSET(Data!$A$1,0,0,Data!$B$1,2)", sheet, workbook);
        dynamicResult.Status.Should().Be(ReferenceGeometryStatus.Success);
        dynamicResult.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(4));
        evaluator.ResolveReferenceGeometry("OFFSET(Data!$A$1,0,0,Data!$B$1,2)", sheet, workbook)
            .Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)));

        var fullColumn = evaluator.ResolveReferenceGeometry("OFFSET(Data!$A:$A,0,0)", sheet, workbook);
        fullColumn.Status.Should().Be(ReferenceGeometryStatus.Success);
        fullColumn.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, CellAddress.MaxRow, 1)));
        var fullRow = evaluator.ResolveReferenceGeometry("OFFSET(Data!$1:$1,2,0,1,16384)", sheet, workbook);
        fullRow.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, CellAddress.MaxCol)));
    }

    [Fact]
    public void Unions_AreOrderedAndAllOrNothing()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var evaluator = new FormulaEvaluator();
        var result = evaluator.ResolveReferenceGeometry("Data!A1:B2,OFFSET(Data!D1,1,0,2,1)", sheet, workbook);
        result.Status.Should().Be(ReferenceGeometryStatus.Success);
        result.Areas.Should().Equal(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            new GridRange(new CellAddress(sheet.Id, 2, 4), new CellAddress(sheet.Id, 3, 4)));
        var invalid = evaluator.ResolveReferenceGeometry("Data!A1,OFFSET(Data!D1,0,0,0,1)", sheet, workbook);
        invalid.Status.Should().Be(ReferenceGeometryStatus.Error);
        invalid.Error.Should().Be(ErrorValue.Ref);
        invalid.Areas.Should().BeEmpty();
    }

    [Fact]
    public void IndexChooseIndirectAndNames_ReturnReferenceIdentity()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["DynamicArea"] = "OFFSET(Data!A1,1,2,3,2)";
        var evaluator = new FormulaEvaluator();
        var single = evaluator.ResolveReferenceGeometry("INDEX(DynamicArea,2,1)", sheet, workbook);
        single.Status.Should().Be(ReferenceGeometryStatus.Success);
        single.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 3, 3)));
        var choice = evaluator.ResolveReferenceGeometry("CHOOSE(2,A1,INDIRECT(\"Data!C2:D4\"))", sheet, workbook);
        choice.Status.Should().Be(ReferenceGeometryStatus.Success);
        choice.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 2, 3), new CellAddress(sheet.Id, 4, 4)));
        var r1c1 = evaluator.ResolveReferenceGeometry("INDIRECT(\"R2C3:R4C4\",FALSE)", sheet, workbook);
        r1c1.Areas.Should().Equal(choice.Areas);
    }

    [Fact]
    public void DistinguishesInvalidExpressionsFromUnsupportedFormsAndCycles()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        workbook.NamedFormulas["CycleA"] = "CycleB";
        workbook.NamedFormulas["CycleB"] = "CycleA";
        var evaluator = new FormulaEvaluator();
        evaluator.ResolveReferenceGeometry("CycleA", sheet, workbook).Error.Should().Be(ErrorValue.Ref);
        evaluator.ResolveReferenceGeometry("42", sheet, workbook).Error.Should().Be(ErrorValue.Value);
        evaluator.ResolveReferenceGeometry("NA()", sheet, workbook).Error.Should().Be(ErrorValue.NA);
        evaluator.ResolveReferenceGeometry("SUM(1,2)", sheet, workbook).Error.Should().Be(ErrorValue.Value);
        evaluator.ResolveReferenceGeometry("SUM(1/0)", sheet, workbook).Error.Should().Be(ErrorValue.DivByZero);
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,UNKNOWN_EXCEL_FUNCTION(),1)", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        evaluator.ResolveReferenceGeometry("INDEX((A1,B1),1,1,0)", sheet, workbook).Error.Should().Be(ErrorValue.Value);
        evaluator.ResolveReferenceGeometry("INDEX((A1,B1),1,1,3)", sheet, workbook).Error.Should().Be(ErrorValue.Ref);
        evaluator.ResolveReferenceGeometry("@A1", sheet, workbook).Areas.Should().Equal(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
        evaluator.ResolveReferenceGeometry("OFFSET(@A1,0,0)", sheet, workbook).Areas.Should().Equal(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,0,1)", sheet, workbook).Error.Should().Be(ErrorValue.Ref);
        evaluator.ResolveReferenceGeometry("Data:Other!A1", sheet, workbook).Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        evaluator.ResolveReferenceGeometry("SOME_UNKNOWN_FUNCTION(A1)", sheet, workbook).Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        evaluator.ResolveReferenceGeometry("A1:INDEX(A:A,MATCH(2,B:B,0))", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        evaluator.ResolveReferenceGeometry("INDIRECT(\"'[1]Data'!A1\")", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
    }

    [Fact]
    public void ResolutionDepthLimit_IsUnsupportedWhileCyclesRemainErrors()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        for (var i = 0; i < 160; i++)
            workbook.NamedFormulas[$"LongName{i}"] = i == 159 ? "A1" : $"LongName{i + 1}";
        workbook.NamedFormulas["CycleFirst"] = "CycleSecond";
        workbook.NamedFormulas["CycleSecond"] = "CycleFirst";
        var evaluator = new FormulaEvaluator();
        evaluator.ResolveReferenceGeometry("LongName0", sheet, workbook).Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        var cycle = evaluator.ResolveReferenceGeometry("CycleFirst", sheet, workbook);
        cycle.Status.Should().Be(ReferenceGeometryStatus.Error);
        cycle.Error.Should().Be(ErrorValue.Ref);
    }

    [Theory]
    [InlineData("ROWS(CHOOSE(1,42,A1))")]
    [InlineData("ROWS(INDEX({1,2},1,1))")]
    public void AmbiguousReferenceFunctionDimensions_AreUnsupported(string height)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        new FormulaEvaluator().ResolveReferenceGeometry($"OFFSET(A1,0,0,{height},1)", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
    }

    [Fact]
    public void NamedFormula_RelativeReferenceUsesCurrentCellAndNullLeavesItUnshifted()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["RelativeArea"] = "B2:C3";
        var evaluator = new FormulaEvaluator();
        var raw = evaluator.ResolveReferenceGeometry("RelativeArea", sheet, workbook);
        raw.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3)));
        var moved = evaluator.ResolveReferenceGeometry("RelativeArea", sheet, workbook, new CellAddress(sheet.Id, 4, 4));
        moved.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 6, 6)));
    }

    [Fact]
    public void HugeIntersection_ReturnsUnsupportedWithoutExpandingCartesianProduct()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var many = string.Join(",", Enumerable.Repeat("A1", 257));
        var result = new FormulaEvaluator().ResolveReferenceGeometry($"(({many}) ({many}))", sheet, workbook);
        result.Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        result.Areas.Should().BeEmpty();
    }

    [Fact]
    public void QualifiedName_UsesRequestedScopeAndCallerContextForUnqualifiedBody()
    {
        var workbook = new Workbook("test");
        var caller = workbook.AddSheet("Caller");
        var scope = workbook.AddSheet("Scope");
        workbook.NamedFormulas["Target"] = "$A$1";
        workbook.DefineNamedFormula("Target", "$C$3", scope.Id);
        var evaluator = new FormulaEvaluator();
        evaluator.ResolveReferenceGeometry("Scope!Target", caller, workbook).Areas.Should().Equal(
            new GridRange(new CellAddress(caller.Id, 3, 3), new CellAddress(caller.Id, 3, 3)));
        evaluator.ResolveReferenceGeometry("Target", caller, workbook).Areas.Should().Equal(
            new GridRange(new CellAddress(caller.Id, 1, 1), new CellAddress(caller.Id, 1, 1)));
    }

    [Fact]
    public void ScalarNamedFormulas_PreserveUnsupportedProvenanceBeforeEvaluation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var evaluator = new FormulaEvaluator();
        workbook.NamedFormulas["HeightUnknown"] = "UNKNOWN_EXCEL_FUNCTION()";
        workbook.NamedFormulas["HeightWrapped"] = "IFERROR(HeightUnknown,1)";
        workbook.NamedFormulas["HeightUnparsed"] = "A1:INDEX(A:A,MATCH(2,B:B,0))";
        workbook.NamedFormulas["HeightCycleA"] = "HeightCycleB";
        workbook.NamedFormulas["HeightCycleB"] = "HeightCycleA";
        workbook.NamedFormulas["HeightError"] = "NA()";

        foreach (var height in new[] { "HeightUnknown", "HeightWrapped", "HeightUnparsed",
                     "IFERROR(UNKNOWN_EXCEL_FUNCTION(),1)" })
        {
            var result = evaluator.ResolveReferenceGeometry($"OFFSET(A1,0,0,{height},1)", sheet, workbook);
            result.Status.Should().Be(ReferenceGeometryStatus.Unsupported, height);
            result.Areas.Should().BeEmpty();
        }
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,HeightError,1)", sheet, workbook)
            .Error.Should().Be(ErrorValue.NA);
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,HeightCycleA,1)", sheet, workbook)
            .Error.Should().Be(ErrorValue.Ref);
    }

    [Theory]
    [InlineData("IFERROR('[1]Data'!B1,1)")]
    [InlineData("IFERROR('[1]Data'!B:B,1)")]
    [InlineData("IFERROR('C:\\Docs\\[Book.xlsx]Data'!$1:$1,1)")]
    [InlineData("IFERROR('[1]Data:Other'!A1,1)")]
    [InlineData("ExternalHeight")]
    public void ScalarExternalReferences_AreUnsupportedEvenWhenIfErrorWouldHideThem(string height)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["ExternalHeight"] = "IFERROR('[1]Data'!B1,1)";
        var result = new FormulaEvaluator().ResolveReferenceGeometry($"OFFSET(A1,0,0,{height},1)", sheet, workbook);
        result.Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        result.Areas.Should().BeEmpty();
    }

    [Fact]
    public void BranchingNamedScalarPreflight_IsBounded()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["BudgetNode0"] = "1";
        for (int i = 1; i <= 17; i++)
            workbook.NamedFormulas[$"BudgetNode{i}"] = $"SUM(BudgetNode{i - 1},BudgetNode{i - 1})";
        var result = new FormulaEvaluator().ResolveReferenceGeometry("OFFSET(A1,0,0,BudgetNode17,1)", sheet, workbook);
        result.Status.Should().Be(ReferenceGeometryStatus.Unsupported);
    }

    [Fact]
    public void ReferenceDimensions_UseUnclampedBoundsForIndirectAndNamedFullAxes()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedRanges["AllData"] = new GridRange(new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var evaluator = new FormulaEvaluator();
        var expected = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        foreach (var height in new[] { "ROWS(INDIRECT(\"A:A\"))", "ROWS(INDIRECT(\"A1:XFD1048576\"))", "ROWS(AllData)" })
        {
            var result = evaluator.ResolveReferenceGeometry($"OFFSET(A1,0,0,{height},1)", sheet, workbook);
            result.Status.Should().Be(ReferenceGeometryStatus.Success, height);
            result.Areas.Should().Equal(expected);
        }
        var width = evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,1,COLUMNS(INDIRECT(\"1:1\")))", sheet, workbook);
        width.Status.Should().Be(ReferenceGeometryStatus.Success);
        width.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, CellAddress.MaxCol)));
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,SUM(ROWS(INDIRECT(\"A:A\")),0),1)", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
    }

    [Theory]
    [InlineData("COUNTBLANK(AllData)")]
    [InlineData("COUNTBLANK(A:A)")]
    [InlineData("COUNTIF(AllData,\"\")")]
    [InlineData("IFERROR(COUNTIF(AllData,\"\"),1)")]
    [InlineData("SUM(A1:A10)")]
    [InlineData("SUM({1,2})")]
    public void RangeSensitiveScalarFunctions_AreUnsupportedUntilGeometryAware(string height)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedRanges["AllData"] = new GridRange(new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));
        var result = new FormulaEvaluator().ResolveReferenceGeometry($"OFFSET(B1,0,0,{height},1)", sheet, workbook);
        result.Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        result.Areas.Should().BeEmpty();
    }

    [Fact]
    public void ScalarCellAndNamedScalarArguments_StillResolve()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3));
        workbook.NamedFormulas["HeightScalar"] = "B1+1";
        var evaluator = new FormulaEvaluator();
        var fromCell = evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,IFERROR(B1,1),1)", sheet, workbook);
        fromCell.Status.Should().Be(ReferenceGeometryStatus.Success);
        fromCell.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));
        var fromName = evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,HeightScalar,1)", sheet, workbook);
        fromName.Status.Should().Be(ReferenceGeometryStatus.Success);
        fromName.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)));
    }

    [Theory]
    [InlineData("ROWS({1,2;3,4})", 2)]
    [InlineData("COLUMNS({1,2;3,4})", 2)]
    [InlineData("ROWS(42)", 1)]
    [InlineData("COLUMNS(\"x\")", 1)]
    public void ReferenceDimensions_ResolveLiteralArraysAndScalars(string height, uint expectedRows)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var result = new FormulaEvaluator().ResolveReferenceGeometry($"OFFSET(A1,0,0,{height},1)", sheet, workbook);
        result.Status.Should().Be(ReferenceGeometryStatus.Success);
        result.Areas.Should().Equal(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, expectedRows, 1)));
    }

    [Fact]
    public void ScalarComputedDimension_IsUnsupportedRatherThanAFalseError()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var evaluator = new FormulaEvaluator();
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,ROWS(1+2),1)", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,ROWS(SUM(1,2)),1)", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
    }

    [Fact]
    public void NamedScalarDimensions_DoNotBecomeFalseErrors_AndRealReferenceErrorsSurvive()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["ScalarName"] = "42";
        workbook.NamedFormulas["InvalidReference"] = "OFFSET(A1,-1,0)";
        var evaluator = new FormulaEvaluator();
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,ROWS(ScalarName),1)", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,COLUMNS(ScalarName),1)", sheet, workbook)
            .Status.Should().Be(ReferenceGeometryStatus.Unsupported);
        var invalid = evaluator.ResolveReferenceGeometry("OFFSET(A1,0,0,ROWS(InvalidReference),1)", sheet, workbook);
        invalid.Status.Should().Be(ReferenceGeometryStatus.Error);
        invalid.Error.Should().Be(ErrorValue.Ref);
    }
}
