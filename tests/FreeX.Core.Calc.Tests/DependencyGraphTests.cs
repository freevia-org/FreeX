using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Calc.Tests;

public class DependencyGraphTests
{
    [Fact]
    public void DependencyGraph_PreSizesRangePrecedentStorageForFormulaRebuilds()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("DependencyGraph.cs");
        var ensureCapacity = source[
            source.IndexOf("internal void EnsureFormulaCapacity", StringComparison.Ordinal)..
            source.IndexOf("private static readonly IReadOnlySet<CellAddress> EmptySet", StringComparison.Ordinal)];

        ensureCapacity.Should().Contain("_precedents.EnsureCapacity(formulaCount);");
        ensureCapacity.Should().Contain("_rangePrecedents.EnsureCapacity(formulaCount);");
    }

    [Fact]
    public void RecalcEngine_ScansFormulaCellsWithoutCopyingUsedCellDictionaries()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("RecalcEngine.cs");

        source.Should().NotContain(
            "GetUsedCells()",
            "full and sheet recalculation should stream occupied cells instead of allocating dictionaries");
    }

    [Fact]
    public void RecalcEngine_FiltersSheetReportsWithoutLinqListScaffolding()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("RecalcEngine.cs");

        source.Should().NotContain(
            "report.RecalculatedCells.Where",
            "sheet recalculation should avoid allocating LINQ iterators and filter lists when the report is already sheet-local");
        source.Should().NotContain(
            "report.Errors.Where",
            "sheet recalculation should avoid allocating LINQ iterators and filter lists when the report is already sheet-local");
        source.Should().NotContain(
            "report.CyclicCells.Where",
            "sheet recalculation should avoid allocating LINQ iterators and filter lists when the report is already sheet-local");
    }

    [Fact]
    public void RecalcEngine_CollectsFormulaCellsWithoutLinqScaffolding()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("RecalcEngine.cs");

        source.Should().Contain(
            "sheet.FormulaCellCount",
            "full and sheet recalculation should pre-size formula-cell lists from tracked sheet metadata");
        source.Should().Contain(
            "if (!sheet.HasFormulas)",
            "sheets without formulas should be skipped before enumerating occupied cells");
        source.Should().Contain(
            "sheet.EnumerateFormulaCells()",
            "recalculation should use tracked formula addresses instead of scanning every occupied value cell");
        source.Should().NotContain(
            ".Where(entry => entry.Cell.HasFormula)",
            "formula-cell collection should avoid LINQ iterator/list scaffolding on recalculation hot paths");
    }

    [Fact]
    public void RecalcEngine_ReturnsSharedEmptyReportForChangedValueCellsWithoutDependents()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("RecalcEngine.cs");
        var afterPlan = source[
            source.IndexOf("var plan = _graph.GetRecalcOrder(changedForTraversal);", StringComparison.Ordinal)..
            source.IndexOf("var recalculatedCount = 0;", StringComparison.Ordinal)];

        afterPlan.Should().Contain("plan.OrderedCells.Count == 0");
        afterPlan.Should().Contain("plan.CyclicCells.Count == 0");
        // R149-formula-volatility-manual-mode-fresh-formula-recalc: this guard now reads the
        // includeVolatileCells-gated local (see RecalcEngine.Recalculate's volatileCellsForPass)
        // instead of the raw _volatileCells field directly, so a caller that opts out of volatile
        // cells sees an empty set here too -- same short-circuit behaviour, renamed source.
        afterPlan.Should().Contain("volatileCellsForPass.Count == 0");
        afterPlan.Should().Contain("changedFormulaCells is null");
        afterPlan.Should().Contain("return EmptyReport;");
        source.Should().Contain("private static IReadOnlyList<CellAddress>? CollectChangedFormulaCells");
        source.Should().NotContain("changedCells.Where");

        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(42));
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        engine.Recalculate(workbook, [a1]).Should().BeSameAs(engine.Recalculate(workbook, [a1]));
    }

    [Fact]
    public void RecalcEngine_LazilyAllocatesErrorAndCycleReportLists()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("RecalcEngine.cs");
        // Slice ends at the spill follow-up block: that block runs only when a spill target
        // actually changed (guarded, cold path), so its own bounded allocations are fine — the
        // lazy-allocation contract protects the per-recalc HOT path up to the report construction.
        var recalculate = source[
            source.IndexOf("public RecalcReport Recalculate", StringComparison.Ordinal)..
            source.IndexOf("// Follow-up passes:", StringComparison.Ordinal)];

        recalculate.Should().Contain("List<(CellAddress Cell, string Error)>? errors = null;");
        recalculate.Should().Contain("HashSet<CellAddress>? seenCyclicCells = null;");
        recalculate.Should().Contain("errors ?? EmptyErrors");
        recalculate.Should().Contain("cyclicCells ?? EmptyCells");
        recalculate.Should().NotContain("new HashSet<CellAddress>();");
        recalculate.Should().NotContain("new List<(CellAddress Cell, string Error)>();");
    }

    [Fact]
    public void RecalcEngine_CollectsReferencesAndVolatileFunctionsInSingleAstWalk()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("RecalcEngine.cs");
        var registration = source[
            source.IndexOf("public void RegisterFormulaDependencies", StringComparison.Ordinal)..
            source.IndexOf("public void ClearFormulaDependencies", StringComparison.Ordinal)];
        var collectReferencesStart = source.IndexOf("private static bool CollectReferences", StringComparison.Ordinal);
        var referenceCollection = source[
            source.LastIndexOf("private static bool", collectReferencesStart, StringComparison.Ordinal)..
            source.IndexOf("private static GridRange CreateGridRange", StringComparison.Ordinal)];

        registration.Should().Contain("var containsVolatileFunction = CollectReferences(");
        source.Should().NotContain(
            "ContainsVolatileFunction(ast)",
            "formula dependency registration should not walk the AST a second time just to detect volatility");
        source.Should().NotContain(
            "private static bool ContainsVolatileFunction",
            "volatile detection should stay fused with reference collection on the registration hot path");
        referenceCollection.Should().Contain("IsVolatileFunctionName(func.FunctionName)");
        referenceCollection.Should().NotContain(
            "BuiltInFunctions.IsVolatile(",
            "dependency-only rebuild should not initialize the full built-in function registry just to detect volatile names");
        referenceCollection.Should().Contain("for (var i = 0; i < arguments.Count; i++)");
        referenceCollection.Should().Contain("CollectReferences(arguments[i]");
        referenceCollection.Should().NotContain(
            ".Any(",
            "volatile formula registration should avoid LINQ iterator/delegate work while walking function arguments");
    }

    [Fact]
    public void DependencyGraph_ReturnsSharedEmptyPlanWhenChangedCellsHaveNoDependents()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("DependencyGraph.cs");
        var getRecalcOrder = source[
            source.IndexOf("public RecalcPlan GetRecalcOrder", StringComparison.Ordinal)..
            source.IndexOf("private void EnqueueUnvisitedDependents", StringComparison.Ordinal)];

        getRecalcOrder.Should().Contain("if (changedCells is IReadOnlyList<CellAddress> changedList)");
        getRecalcOrder.Should().Contain("changedList.Count == 0 || !HasAnyDependents(changedList)");
        getRecalcOrder.Should().Contain("return EmptyPlan;");
        getRecalcOrder.Should().Contain("private bool HasAnyDependents(IReadOnlyList<CellAddress> cells)");
        getRecalcOrder.IndexOf("return EmptyPlan;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(getRecalcOrder.IndexOf("var toRecalc = new HashSet<CellAddress>();", StringComparison.Ordinal));
        source.Should().Contain("private static readonly RecalcPlan EmptyPlan = new([], []);");

        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);

        graph.GetRecalcOrder([a1]).Should().BeSameAs(graph.GetRecalcOrder([a1]));
    }

    [Fact]
    public void DependencyGraph_UsesLinearFastPathForSingleRootExactChains()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("DependencyGraph.cs");
        var getRecalcOrder = source[
            source.IndexOf("public RecalcPlan GetRecalcOrder", StringComparison.Ordinal)..
            source.IndexOf("var toRecalc = new HashSet<CellAddress>();", StringComparison.Ordinal)];
        var fastPath = source[
            source.IndexOf("private bool TryBuildSingleRootExactChainPlan", StringComparison.Ordinal)..
            source.IndexOf("public RecalcPlan GetEvaluationOrder", StringComparison.Ordinal)];

        getRecalcOrder.Should().Contain("TryBuildSingleRootExactChainPlan(changedList, out var chainPlan)");
        fastPath.Should().Contain("TryCountSingleRootExactChain");
        fastPath.Should().Contain("new List<CellAddress>(count)");
        fastPath.Should().NotContain("new HashSet<CellAddress>");
    }

    [Fact]
    public void DependencyGraph_UsesLeafFastPathForMultiRootExactDependents()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("DependencyGraph.cs");
        var getRecalcOrder = source[
            source.IndexOf("public RecalcPlan GetRecalcOrder", StringComparison.Ordinal)..
            source.IndexOf("var toRecalc = new HashSet<CellAddress>();", StringComparison.Ordinal)];
        var fastPath = source[
            source.IndexOf("private bool TryBuildSingleLeafExactDependentPlan", StringComparison.Ordinal)..
            source.IndexOf("public RecalcPlan GetEvaluationOrder", StringComparison.Ordinal)];

        getRecalcOrder.Should().Contain("TryBuildSingleLeafExactDependentPlan(changedList, out var leafPlan)");
        fastPath.Should().Contain("_rangeDependentsBySheet.Count != 0");
        fastPath.Should().Contain("downstream.Count != 0");
        fastPath.Should().NotContain("new HashSet<CellAddress>");

        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);
        var c1 = new CellAddress(sheet, 1, 3);
        var d1 = new CellAddress(sheet, 1, 4);

        graph.SetDependencies(c1, [a1, b1]);

        var leafPlan = graph.GetRecalcOrder([a1, b1]);
        leafPlan.CyclicCells.Should().BeEmpty();
        leafPlan.OrderedCells.Should().Equal([c1]);

        graph.SetDependencies(d1, [c1]);

        var downstreamPlan = graph.GetRecalcOrder([a1, b1]);
        downstreamPlan.CyclicCells.Should().BeEmpty();
        downstreamPlan.OrderedCells.Should().Equal([c1, d1]);
    }

    [Fact]
    public void DependencyGraph_UsesLeafFastPathForSingleRangeDependent()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("DependencyGraph.cs");
        var getRecalcOrder = source[
            source.IndexOf("public RecalcPlan GetRecalcOrder", StringComparison.Ordinal)..
            source.IndexOf("var toRecalc = new HashSet<CellAddress>();", StringComparison.Ordinal)];
        var fastPath = source[
            source.IndexOf("private bool TryBuildSingleLeafRangeDependentPlan", StringComparison.Ordinal)..
            source.IndexOf("public RecalcPlan GetEvaluationOrder", StringComparison.Ordinal)];

        getRecalcOrder.Should().Contain("TryBuildSingleLeafRangeDependentPlan(changedList, out var rangeLeafPlan)");
        fastPath.Should().Contain("TryCollectSingleRangeDependent");
        fastPath.Should().Contain("HasAnyDependent(dependent)");
        fastPath.Should().Contain("new RecalcPlan([dependent], [])");
        fastPath.Should().NotContain("new HashSet<CellAddress>");

        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var input = new CellAddress(sheet.Id, 5, 1);
        var rangeLeaf = new CellAddress(sheet.Id, 1, 2);
        var downstream = new CellAddress(sheet.Id, 1, 3);

        engine.RegisterFormulaDependencies(
            rangeLeaf,
            new Parser(new Lexer("=SUM(A1:A10)").Tokenize()).Parse(),
            sheet.Id,
            workbook);

        var leafPlan = graph.GetRecalcOrder([input]);
        leafPlan.CyclicCells.Should().BeEmpty();
        leafPlan.OrderedCells.Should().Equal([rangeLeaf]);

        engine.RegisterFormulaDependencies(
            downstream,
            new Parser(new Lexer("=SUM(B1:B10)").Tokenize()).Parse(),
            sheet.Id,
            workbook);

        var downstreamPlan = graph.GetRecalcOrder([input]);
        downstreamPlan.CyclicCells.Should().BeEmpty();
        downstreamPlan.OrderedCells.Should().Equal([rangeLeaf, downstream]);
    }

    [Fact]
    public void SetDependencies_TracksDependents()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);

        // B1 depends on A1 (e.g. =A1+1)
        graph.SetDependencies(b1, [a1]);

        graph.GetDirectDependents(a1).Should().Contain(b1);
        graph.GetDirectPrecedents(b1).Should().Contain(a1);
    }

    [Fact]
    public void SetDependencies_CopiesCallerOwnedSets()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);
        var c1 = new CellAddress(sheet, 1, 3);
        var callerOwned = new HashSet<CellAddress> { a1 };

        graph.SetDependencies(b1, callerOwned);
        callerOwned.Add(c1);

        graph.GetDirectPrecedents(b1).Should().BeEquivalentTo([a1]);
        graph.GetDirectDependents(c1).Should().NotContain(b1);
    }

    [Fact]
    public void SetDependenciesFromOwnedSet_UsesFreshSetWithoutCopying()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);
        var owned = new HashSet<CellAddress> { a1 };
        var helper = typeof(DependencyGraph).GetMethod(
            "SetDependenciesFromOwnedSet",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        helper.Should().NotBeNull("RecalcEngine should have an owned HashSet path for formula dependencies");
        helper!.Invoke(graph, [b1, owned]);

        graph.GetDirectPrecedents(b1).Should().BeSameAs(owned);
        graph.GetDirectDependents(a1).Should().Contain(b1);
    }

    [Fact]
    public void ClearDependencies_RemovesLinks()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);

        graph.SetDependencies(b1, [a1]);
        graph.ClearDependencies(b1);

        graph.GetDirectDependents(a1).Should().NotContain(b1);
    }

    [Fact]
    public void RecalcOrder_ReturnsTopologicalOrder()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2); // =A1+1
        var c1 = new CellAddress(sheet, 1, 3); // =B1*2

        graph.SetDependencies(b1, [a1]);
        graph.SetDependencies(c1, [b1]);

        var plan = graph.GetRecalcOrder([a1]);

        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().HaveCount(2);
        var b1Idx = plan.OrderedCells.ToList().IndexOf(b1);
        var c1Idx = plan.OrderedCells.ToList().IndexOf(c1);
        b1Idx.Should().BeLessThan(c1Idx, "B1 should be recalculated before C1");
    }

    [Fact]
    public void RecalcOrder_DetectsCycles()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);

        // A1 -> B1 -> A1 (circular)
        graph.SetDependencies(a1, [b1]);
        graph.SetDependencies(b1, [a1]);

        var plan = graph.GetRecalcOrder([a1]);

        plan.CyclicCells.Should().NotBeEmpty();
    }

    /// <summary>
    /// Regression for the circular-dependency over-marking bug: cells that merely DEPEND ON a
    /// cyclic cell (but are not themselves part of the cycle) must not be classified as circular.
    /// A1 = B1 + 1, B1 = A1 (a genuine 2-cycle); C1 = A1 (downstream of the cycle);
    /// D1 = C1 (further downstream). Only A1 and B1 are true cycle members — C1 and D1 must still
    /// appear in the evaluation order so they evaluate (and naturally inherit the propagated
    /// #CIRCULAR! value from A1), rather than being marked cyclic themselves.
    /// </summary>
    [Fact]
    public void RecalcOrder_DownstreamOfCycle_IsNotClassifiedCircular()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);
        var c1 = new CellAddress(sheet, 1, 3);
        var d1 = new CellAddress(sheet, 1, 4);

        // A1 -> B1 -> A1 (circular); C1 depends on A1; D1 depends on C1 (both downstream only).
        graph.SetDependencies(a1, [b1]);
        graph.SetDependencies(b1, [a1]);
        graph.SetDependencies(c1, [a1]);
        graph.SetDependencies(d1, [c1]);

        var plan = graph.GetRecalcOrder([a1]);

        plan.CyclicCells.Should().Contain(a1);
        plan.CyclicCells.Should().Contain(b1);
        plan.CyclicCells.Should().NotContain(c1, "C1 merely depends on a cyclic cell but is not itself part of the cycle");
        plan.CyclicCells.Should().NotContain(d1, "D1 is even further downstream of the cycle and must not be classified circular");

        plan.OrderedCells.Should().Contain(c1, "C1 must still be evaluated so it receives A1's propagated error");
        plan.OrderedCells.Should().Contain(d1, "D1 must still be evaluated so it receives C1's propagated error");

        var c1Index = plan.OrderedCells.ToList().IndexOf(c1);
        var d1Index = plan.OrderedCells.ToList().IndexOf(d1);
        c1Index.Should().BeLessThan(d1Index, "C1 must evaluate before D1 since D1 reads C1");
    }

    /// <summary>
    /// Same scenario as <see cref="RecalcOrder_DownstreamOfCycle_IsNotClassifiedCircular"/> but
    /// exercised through <see cref="DependencyGraph.GetEvaluationOrder"/>, which RecalcEngine also
    /// uses to build its combined dirty-cell plan.
    /// </summary>
    [Fact]
    public void EvaluationOrder_DownstreamOfCycle_IsNotClassifiedCircular()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);
        var c1 = new CellAddress(sheet, 1, 3);
        var d1 = new CellAddress(sheet, 1, 4);

        graph.SetDependencies(a1, [b1]);
        graph.SetDependencies(b1, [a1]);
        graph.SetDependencies(c1, [a1]);
        graph.SetDependencies(d1, [c1]);

        var plan = graph.GetEvaluationOrder([a1, b1, c1, d1]);

        plan.CyclicCells.Should().Contain(a1);
        plan.CyclicCells.Should().Contain(b1);
        plan.CyclicCells.Should().NotContain(c1);
        plan.CyclicCells.Should().NotContain(d1);
        plan.OrderedCells.Should().Contain(c1);
        plan.OrderedCells.Should().Contain(d1);
    }

    /// <summary>
    /// End-to-end regression through RecalcEngine: A1/B1 form a real cycle and must be reported
    /// circular, but seed to 0 (Excel's non-iterative circular-reference behaviour) rather than a
    /// fabricated #CIRCULAR! error. C1 (=A1) and D1 (=C1) are only downstream of the cycle and
    /// must evaluate normally, receiving the propagated 0 from A1 rather than being classified
    /// as circular themselves.
    /// </summary>
    [Fact]
    public void RecalcEngine_DownstreamOfCircularReference_EvaluatesInsteadOfBeingMarkedCircular()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var d1 = new CellAddress(sheet.Id, 1, 4);

        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "A1");
        sheet.SetFormula(c1, "A1");
        sheet.SetFormula(d1, "C1");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1);
        report.CyclicCells.Should().Contain(b1);
        report.CyclicCells.Should().NotContain(c1, "C1 only depends on the cyclic A1/B1 pair; it is not itself circular");
        report.CyclicCells.Should().NotContain(d1, "D1 is further downstream and must not be classified circular either");

        var zero = new NumberValue(0);
        sheet.GetValue(a1).Should().Be(zero, "Excel seeds a non-iterative circular reference to 0, not a fabricated error");
        sheet.GetValue(b1).Should().Be(zero);
        // C1 = A1 evaluates normally and inherits A1's propagated 0.
        sheet.GetValue(c1).Should().Be(zero);
        // D1 = C1 evaluates normally and inherits C1's propagated 0 in turn.
        sheet.GetValue(d1).Should().Be(zero);
    }

    /// <summary>
    /// R66-calc-iterative-circular-6-1: the cyclic cell itself must compute as 0, not a fabricated
    /// #CIRCULAR! error value — so a downstream IFERROR wrapping it must NOT fire (there is no
    /// error to catch) and must see the real 0, exactly like Excel.
    /// </summary>
    [Fact]
    public void RecalcEngine_CircularCell_SeedsZero_AndIferrorDoesNotFire()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "A1");
        sheet.SetFormula(e1, "IFERROR(A1,\"x\")");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1);
        sheet.GetValue(a1).Should().Be(new NumberValue(0));
        // IFERROR must not catch anything: A1 is a real number (0), so E1 must be 0, not "x".
        sheet.GetValue(e1).Should().Be(new NumberValue(0),
            "IFERROR must not fire against a circular reference's seeded value, exactly like Excel");
    }

    [Fact]
    public void LargeRangeDependency_RecalculatesOnlyWhenChangedCellIsInsideRange()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 2);
        var inside = new CellAddress(sheet.Id, 50000, 1);
        var outside = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(inside, new NumberValue(4));
        sheet.SetFormula(formula, "SUM(A1:A100000)");
        engine.RecalculateAllFormulas(workbook);
        sheet.GetValue(formula).Should().Be(new NumberValue(4));

        sheet.SetCell(inside, new NumberValue(9));
        var insideReport = engine.Recalculate(workbook, [inside]);

        insideReport.RecalculatedCells.Should().Contain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(9));

        sheet.SetCell(outside, new NumberValue(25));
        var outsideReport = engine.Recalculate(workbook, [outside]);

        outsideReport.RecalculatedCells.Should().NotContain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void ReplacingFormula_RemovesLargeRangeDependency()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 2);
        var oldInside = new CellAddress(sheet.Id, 50000, 1);
        var newPrecedent = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(oldInside, new NumberValue(4));
        sheet.SetCell(newPrecedent, new NumberValue(8));
        sheet.SetFormula(formula, "SUM(A1:A100000)");
        engine.RecalculateAllFormulas(workbook);

        sheet.SetFormula(formula, "C1");
        engine.RebuildFormulaDependencies(workbook);
        sheet.SetCell(oldInside, new NumberValue(99));
        var oldRangeReport = engine.Recalculate(workbook, [oldInside]);

        oldRangeReport.RecalculatedCells.Should().NotContain(formula);

        sheet.SetCell(newPrecedent, new NumberValue(12));
        var newPrecedentReport = engine.Recalculate(workbook, [newPrecedent]);

        newPrecedentReport.RecalculatedCells.Should().Contain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(12));
    }

    [Fact]
    public void CrossSheetLargeRangeDependency_RecalculatesFromSourceSheetChange()
    {
        var workbook = new Workbook("Test");
        var formulaSheet = workbook.AddSheet("Formula");
        var dataSheet = workbook.AddSheet("Data");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(formulaSheet.Id, 1, 1);
        var inside = new CellAddress(dataSheet.Id, 50000, 1);
        var outside = new CellAddress(formulaSheet.Id, 1, 2);

        dataSheet.SetCell(inside, new NumberValue(6));
        formulaSheet.SetFormula(formula, "SUM(Data!A1:A100000)");
        engine.RecalculateAllFormulas(workbook);
        formulaSheet.GetValue(formula).Should().Be(new NumberValue(6));

        dataSheet.SetCell(inside, new NumberValue(10));
        var insideReport = engine.Recalculate(workbook, [inside]);

        insideReport.RecalculatedCells.Should().Contain(formula);
        formulaSheet.GetValue(formula).Should().Be(new NumberValue(10));

        formulaSheet.SetCell(outside, new NumberValue(1));
        var outsideReport = engine.Recalculate(workbook, [outside]);

        outsideReport.RecalculatedCells.Should().NotContain(formula);
    }

    [Fact]
    public void RegisterFormulaDependencies_SheetQualifiedReferencesResolvePerWorkbookWhenAstIsReused()
    {
        var formulaSheetId = SheetId.New();
        var firstDataSheetId = SheetId.New();
        var secondDataSheetId = SheetId.New();
        var ast = new Parser(new Lexer("=Data!A1").Tokenize()).Parse();
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var formula = new CellAddress(formulaSheetId, 1, 1);

        var firstWorkbook = new Workbook("First");
        var firstFormulaSheet = new Sheet(formulaSheetId, "Formula");
        var firstDataSheet = new Sheet(firstDataSheetId, "Data");
        firstWorkbook.InsertSheet(0, firstFormulaSheet);
        firstWorkbook.InsertSheet(1, firstDataSheet);

        engine.RegisterFormulaDependencies(formula, ast, firstFormulaSheet.Id, firstWorkbook);
        graph.GetDirectPrecedents(formula)
            .Should()
            .Contain(new CellAddress(firstDataSheet.Id, 1, 1));

        var secondWorkbook = new Workbook("Second");
        var secondFormulaSheet = new Sheet(formulaSheetId, "Formula");
        var secondDataSheet = new Sheet(secondDataSheetId, "Data");
        secondWorkbook.InsertSheet(0, secondFormulaSheet);
        secondWorkbook.InsertSheet(1, secondDataSheet);

        engine.RegisterFormulaDependencies(formula, ast, secondFormulaSheet.Id, secondWorkbook);

        graph.GetDirectPrecedents(formula)
            .Should()
            .Contain(new CellAddress(secondDataSheet.Id, 1, 1));
        graph.GetDirectPrecedents(formula)
            .Should()
            .NotContain(new CellAddress(firstDataSheet.Id, 1, 1),
                "sheet-qualified references must not reuse dependency plans resolved against another workbook");
    }

    [Fact]
    public void NamedLargeRangeDependency_RecalculatesWhenChangedCellIsInsideRange()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 2);
        var inside = new CellAddress(sheet.Id, 50000, 1);

        workbook.DefineNamedRange(
            "BigInputs",
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 100000, 1)));
        sheet.SetCell(inside, new NumberValue(2));
        sheet.SetFormula(formula, "SUM(BigInputs)");
        engine.RecalculateAllFormulas(workbook);

        sheet.SetCell(inside, new NumberValue(7));
        var report = engine.Recalculate(workbook, [inside]);

        report.RecalculatedCells.Should().Contain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void FullColumnRangeDependency_RecalculatesWhenChangedCellIsInsideColumn()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 2);
        var inside = new CellAddress(sheet.Id, 50000, 1);
        var outside = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(inside, new NumberValue(4));
        sheet.SetFormula(formula, "SUM(A:A)");
        engine.RecalculateAllFormulas(workbook);

        sheet.SetCell(inside, new NumberValue(11));
        var insideReport = engine.Recalculate(workbook, [inside]);

        insideReport.RecalculatedCells.Should().Contain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(11));

        sheet.SetCell(outside, new NumberValue(99));
        var outsideReport = engine.Recalculate(workbook, [outside]);

        outsideReport.RecalculatedCells.Should().NotContain(formula);
    }

    [Fact]
    public void FullRowRangeDependency_RecalculatesWhenChangedCellIsInsideRow()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 2, 2);
        var inside = new CellAddress(sheet.Id, 1, 3);
        var outside = new CellAddress(sheet.Id, 3, 1);

        sheet.SetCell(inside, new NumberValue(5));
        sheet.SetFormula(formula, "SUM(1:1)");
        engine.RecalculateAllFormulas(workbook);

        sheet.SetCell(inside, new NumberValue(13));
        var insideReport = engine.Recalculate(workbook, [inside]);

        insideReport.RecalculatedCells.Should().Contain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(13));

        sheet.SetCell(outside, new NumberValue(99));
        var outsideReport = engine.Recalculate(workbook, [outside]);

        outsideReport.RecalculatedCells.Should().NotContain(formula);
    }

    [Fact]
    public void CrossSheetFullColumnRangeDependency_RecalculatesFromSourceSheetChange()
    {
        var workbook = new Workbook("Test");
        var formulaSheet = workbook.AddSheet("Formula");
        var dataSheet = workbook.AddSheet("Data");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(formulaSheet.Id, 1, 1);
        var inside = new CellAddress(dataSheet.Id, 50000, 1);

        dataSheet.SetCell(inside, new NumberValue(6));
        formulaSheet.SetFormula(formula, "SUM(Data!A:A)");
        engine.RecalculateAllFormulas(workbook);

        dataSheet.SetCell(inside, new NumberValue(14));
        var report = engine.Recalculate(workbook, [inside]);

        report.RecalculatedCells.Should().Contain(formula);
        formulaSheet.GetValue(formula).Should().Be(new NumberValue(14));
    }

    [Theory]
    [InlineData("SUM(A:A)")]           // full-column reference — 1,048,576 rows
    [InlineData("SUM(A1:Z10000)")]     // 26 × 10,000 = 260,000 cells
    public void RegisterFormulaDependencies_LargeRange_DoesNotExceed10kIndividualCellEntries(
        string formulaBody)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 10001, 27); // somewhere outside the ranges

        var ast = new Parser(new Lexer("=" + formulaBody).Tokenize()).Parse();
        engine.RegisterFormulaDependencies(formula, ast, sheet.Id, workbook);

        // Individual cell entries must not blow up — large ranges are stored compactly
        graph.GetDirectPrecedents(formula).Count.Should().BeLessThanOrEqualTo(10_000,
            $"formula '{formulaBody}' must not expand into >10k individual cell dependencies");

        // At least one compact range dependency should exist instead
        graph.GetDirectRangePrecedents(formula).Should().NotBeEmpty(
            $"large range formula '{formulaBody}' should register at least one compact GridRange dependency");
    }

    [Fact]
    public void RegisterFormulaDependencies_LargeRange_CompletesQuickly()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 2);

        // A full-column reference has 1,048,576 rows — this must not take 1 M iterations
        var ast = new Parser(new Lexer("=SUM(A:A)").Tokenize()).Parse();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.RegisterFormulaDependencies(formula, ast, sheet.Id, workbook);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "registering a full-column dependency must complete in under 500 ms");
    }

    [Fact]
    public void GetDirectRangePrecedents_ExposesCompactRangeWithoutExpandingCells()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 2);
        var changed = new CellAddress(sheet.Id, 50000, 1);

        sheet.SetFormula(formula, "SUM(A1:A100000)");
        engine.RegisterFormulaDependencies(
            formula,
            new Parser(new Lexer("=SUM(A1:A100000)").Tokenize()).Parse(),
            sheet.Id,
            workbook);

        graph.GetDirectPrecedents(formula).Should().BeEmpty();
        graph.GetDirectRangePrecedents(formula).Should().ContainSingle()
            .Which.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 100000, 1)));
        graph.GetDirectDependents(changed).Should().Contain(formula);
    }

    [Fact]
    public void RegisterFormulaDependencies_NonTinyRange_UsesCompactRange()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 20, 2);
        var inside = new CellAddress(sheet.Id, 5, 1);
        var outside = new CellAddress(sheet.Id, 11, 1);

        engine.RegisterFormulaDependencies(
            formula,
            new Parser(new Lexer("=SUM(A1:A10)").Tokenize()).Parse(),
            sheet.Id,
            workbook);

        graph.GetDirectPrecedents(formula).Should().BeEmpty();
        graph.GetDirectRangePrecedents(formula).Should().ContainSingle()
            .Which.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)));
        graph.GetRecalcOrder([inside]).OrderedCells.Should().Contain(formula);
        graph.GetRecalcOrder([outside]).OrderedCells.Should().NotContain(formula);
    }

    [Fact]
    public void RecalcOrder_DetectsCycleThroughLargeRangeDependency()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 1);

        sheet.SetFormula(formula, "SUM(A1:A100000)");
        engine.RecalculateAllFormulas(workbook);
        var report = engine.Recalculate(workbook, [formula]);

        report.CyclicCells.Should().Contain(formula);
        sheet.GetValue(formula).Should().Be(new NumberValue(0),
            "a self-referencing cycle through a large range must seed to 0, matching Excel's non-iterative circular-reference behaviour");
    }

}

public class VolatileFunctionTests
{
    private static RecalcEngine MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        return new RecalcEngine(graph, evaluator);
    }

    [Fact]
    public void Now_ReturnsDateTimeValue()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var result = evaluator.Evaluate("=NOW()", sheet);
        result.Should().BeOfType<DateTimeValue>();
    }

    [Fact]
    public void Today_ReturnsDateValue_WithTimeZero()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var result = evaluator.Evaluate("=TODAY()", sheet);
        result.Should().BeOfType<DateTimeValue>();
        var dt = ((DateTimeValue)result).ToDateTime();
        dt.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Rand_ReturnsNumberBetweenZeroAndOne()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var result = evaluator.Evaluate("=RAND()", sheet);
        result.Should().BeOfType<NumberValue>();
        var v = ((NumberValue)result).Value;
        v.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(1.0);
    }

    [Fact]
    public void Rand_ReturnsDifferentValuesOnSuccessiveCalls()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var r1 = ((NumberValue)evaluator.Evaluate("=RAND()", sheet)).Value;
        var r2 = ((NumberValue)evaluator.Evaluate("=RAND()", sheet)).Value;
        // Astronomically unlikely to be equal
        r1.Should().NotBe(r2);
    }

    [Fact]
    public void VolatileCell_RecalculatesOnEveryRecalcPass()
    {
        var engine = MakeEngine();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var a1 = new CellAddress(sheetId, 1, 1);

        sheet.SetFormula(a1, "NOW()");
        var lexer = new Lexer("=NOW()");
        var ast = new Parser(lexer.Tokenize()).Parse();
        engine.RegisterFormulaDependencies(a1, ast, sheetId);

        engine.Recalculate(workbook, []);
        var first = sheet.GetCell(a1)!.Value;
        first.Should().BeOfType<DateTimeValue>();

        Thread.Sleep(50);

        engine.Recalculate(workbook, []);
        var second = sheet.GetCell(a1)!.Value;
        second.Should().BeOfType<DateTimeValue>();
    }

    [Fact]
    public void NonVolatileCell_DoesNotRecalculate_WhenNothingChanged()
    {
        var engine = MakeEngine();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var a1 = new CellAddress(sheetId, 1, 1);
        var b1 = new CellAddress(sheetId, 1, 2);

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "SUM(A1)");
        var lexer = new Lexer("=SUM(A1)");
        var ast = new Parser(lexer.Tokenize()).Parse();
        engine.RegisterFormulaDependencies(b1, ast, sheetId);

        // B1 starts as a formula cell with no computed value yet
        var before = sheet.GetCell(b1)!.Value;

        // Recalculate with no changed cells — B1 should not be evaluated
        var report = engine.Recalculate(workbook, []);

        report.RecalculatedCells.Should().NotContain(b1);
        sheet.GetCell(b1)!.Value.Should().Be(before);
    }

    [Fact]
    public void CrossSheet_DependencyPropagates_OnRecalc()
    {
        var wb = new Workbook("Test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var s2a1 = new CellAddress(sheet2.Id, 1, 1);
        var s1b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet2.SetCell(s2a1, new NumberValue(10));
        sheet1.SetFormula(s1b1, "Sheet2!A1");
        var ast = new Parser(new Lexer("=Sheet2!A1").Tokenize()).Parse();
        engine.RegisterFormulaDependencies(s1b1, ast, sheet1.Id, wb);

        engine.Recalculate(wb, [s2a1]);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(10));

        sheet2.SetCell(s2a1, new NumberValue(99));
        engine.Recalculate(wb, [s2a1]);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void RebuildFormulaDependencies_AfterFormulaRewrite_TracksNewReferences()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetFormula(b1, "A2");
        engine.RegisterFormulaDependencies(
            b1,
            new Parser(new Lexer("=A2").Tokenize()).Parse(),
            sheet.Id,
            wb);

        new InsertRowsCommand(sheet.Id, 2).Apply(ctx);
        sheet.GetCell(b1)!.FormulaText.Should().Be("A3");

        var a3 = new CellAddress(sheet.Id, 3, 1);
        engine.RebuildFormulaDependencies(wb);
        sheet.SetCell(a3, new NumberValue(7));
        engine.Recalculate(wb, [a3]);

        sheet.GetValue(b1).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void FormulaRewriteOutcomeAffectedCells_RefreshesIncrementalDependencies()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetFormula(b1, "A2");
        engine.RegisterFormulaDependencies(
            b1,
            new Parser(new Lexer("=A2").Tokenize()).Parse(),
            sheet.Id,
            wb);

        var outcome = new InsertRowsCommand(sheet.Id, 2).Apply(ctx);

        outcome.AffectedCells.Should().Contain(b1);
        engine.Recalculate(wb, outcome.AffectedCells!);
        sheet.SetCell(a3, new NumberValue(7));
        engine.Recalculate(wb, [a3]);

        sheet.GetValue(b1).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void RecalculateAllFormulas_EvaluatesNonVolatileFormulaWithoutChangedCells()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1*2");

        var report = engine.RecalculateAllFormulas(wb);

        report.RecalculatedCells.Should().Contain(b1);
        sheet.GetValue(b1).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void RecalculateSheetFormulas_RecalculatesOnlyRequestedSheetFormulaCells()
    {
        var wb = new Workbook("Test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var s1a1 = new CellAddress(sheet1.Id, 1, 1);
        var s1b1 = new CellAddress(sheet1.Id, 1, 2);
        var s2a1 = new CellAddress(sheet2.Id, 1, 1);
        var s2b1 = new CellAddress(sheet2.Id, 1, 2);

        sheet1.SetCell(s1a1, new NumberValue(5));
        sheet1.SetFormula(s1b1, "A1*2");
        sheet2.SetCell(s2a1, new NumberValue(7));
        sheet2.SetFormula(s2b1, "A1*3");

        var report = engine.RecalculateSheetFormulas(wb, sheet1.Id);

        report.RecalculatedCells.Should().Contain(s1b1);
        report.RecalculatedCells.Should().NotContain(s2b1);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(10));
        sheet2.GetValue(s2b1).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void RecalculateSheetFormulas_ReportExcludesCrossSheetDependents()
    {
        var wb = new Workbook("Test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var s1a1 = new CellAddress(sheet1.Id, 1, 1);
        var s1b1 = new CellAddress(sheet1.Id, 1, 2);
        var s2b1 = new CellAddress(sheet2.Id, 1, 2);

        sheet1.SetCell(s1a1, new NumberValue(5));
        sheet1.SetFormula(s1b1, "A1*2");
        sheet2.SetFormula(s2b1, "Sheet1!B1+1");

        var report = engine.RecalculateSheetFormulas(wb, sheet1.Id);

        report.RecalculatedCells.Should().Contain(s1b1);
        report.RecalculatedCells.Should().NotContain(s2b1);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(10));
        // Shift+F9 "Calculate Sheet" must never mutate a cross-sheet dependent -- Sheet2!B1 has
        // never been evaluated at all (no prior full recalc), so it must be left exactly as it
        // was (blank), not silently computed as a side effect of recalculating Sheet1.
        sheet2.GetValue(s2b1).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void RecalculateSheetFormulas_LeavesCrossSheetDependentStaleUntilFullRecalc()
    {
        // Sheet1!A1=5, B1=A1*2; Sheet2!B1=Sheet1!B1+1. A full recalc first establishes Sheet2!B1's
        // prior value (11). Editing Sheet1!A1 and running Shift+F9 "Calculate Sheet" on Sheet1 only
        // must recalc Sheet1!B1 (200) while leaving Sheet2!B1 at its stale prior value (11) --
        // matching Excel, which only recalculates the active sheet and shows other sheets' formulas
        // referencing it as stale until they are themselves calculated. A subsequent full recalc
        // (F9 / RecalculateAllFormulas) must then bring Sheet2!B1 up to date (201).
        var wb = new Workbook("Test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var s1a1 = new CellAddress(sheet1.Id, 1, 1);
        var s1b1 = new CellAddress(sheet1.Id, 1, 2);
        var s2b1 = new CellAddress(sheet2.Id, 1, 2);

        sheet1.SetCell(s1a1, new NumberValue(5));
        sheet1.SetFormula(s1b1, "A1*2");
        sheet2.SetFormula(s2b1, "Sheet1!B1+1");

        engine.RecalculateAllFormulas(wb);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(10));
        sheet2.GetValue(s2b1).Should().Be(new NumberValue(11));

        sheet1.SetCell(s1a1, new NumberValue(100));
        var sheetReport = engine.RecalculateSheetFormulas(wb, sheet1.Id);

        sheetReport.RecalculatedCells.Should().Contain(s1b1);
        sheetReport.RecalculatedCells.Should().NotContain(s2b1);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(200));
        sheet2.GetValue(s2b1).Should().Be(new NumberValue(11), "Shift+F9 on Sheet1 must not recalculate Sheet2's cross-sheet dependent");

        var fullReport = engine.RecalculateAllFormulas(wb);

        fullReport.RecalculatedCells.Should().Contain(s2b1);
        sheet2.GetValue(s2b1).Should().Be(new NumberValue(201), "a subsequent full recalc must bring the stale cross-sheet dependent up to date");
    }

    [Fact]
    public void VolatileCell_EvaluatesBeforeItsDependents()
    {
        // A1 = =NOW() (volatile)
        // B1 = =A1 (depends on A1)
        // After recalc, B1 should have the same value as A1 (i.e. A1 was evaluated first)
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "NOW()");
        var lexerA = new Lexer("=NOW()");
        engine.RegisterFormulaDependencies(a1, new Parser(lexerA.Tokenize()).Parse(), sheet.Id);

        sheet.SetFormula(b1, "A1");
        var lexerB = new Lexer("=A1");
        engine.RegisterFormulaDependencies(b1, new Parser(lexerB.Tokenize()).Parse(), sheet.Id);

        engine.Recalculate(wb, []);

        sheet.GetValue(a1).Should().BeOfType<DateTimeValue>();
        sheet.GetValue(b1).Should().BeOfType<DateTimeValue>();
        // B1 should equal A1 (meaning A1 was evaluated before B1 read it)
        sheet.GetValue(b1).Should().Be(sheet.GetValue(a1));
    }
}

public class AstCacheTests
{
    [Fact]
    public void RecalcEngine_FormulaChange_UsesNewAstNotCached()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(5));

        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(b1, "A1*2");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);
        sheet.GetValue(b1).Should().Be(new NumberValue(10));

        // Change the formula — setter clears CachedAst so re-parse occurs
        sheet.SetFormula(b1, "A1*3");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);
        sheet.GetValue(b1).Should().Be(new NumberValue(15),
            "after formula change the cached AST must be invalidated and re-parsed");
    }

    [Fact]
    public void RecalcEngine_SameFormula_UsesAstCacheOnSecondPass()
    {
        // Verify that the cache is populated after the first eval and survives a second recalc
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(4));

        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(b1, "A1+1");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1]);
        sheet.GetValue(b1).Should().Be(new NumberValue(5));

        // Mutate A1, recalc again — result changes, proving cache is still used correctly
        sheet.SetCell(a1, new NumberValue(10));
        engine.Recalculate(wb, [a1]);
        sheet.GetValue(b1).Should().Be(new NumberValue(11),
            "second recalc with same formula should still produce a correct result via cached AST");
    }

    [Fact]
    public void RebuildFormulaDependencies_ReusesCachedAstOnSecondPass()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        const uint formulaCount = 2_000;

        for (uint row = 1; row <= formulaCount; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
            sheet.SetFormula(new CellAddress(sheet.Id, row, 3), $"A{row}+B{row}");
        }

        var coldMemory = GC.GetAllocatedBytesForCurrentThread();
        var cold = Stopwatch.StartNew();
        engine.RebuildFormulaDependencies(wb);
        cold.Stop();
        coldMemory = GC.GetAllocatedBytesForCurrentThread() - coldMemory;

        var sampleFormula = sheet.GetCell(new CellAddress(sheet.Id, 1, 3))!;
        var cachedAst = sampleFormula.CachedAst;
        cachedAst.Should().NotBeNull();

        var cachedMemory = GC.GetAllocatedBytesForCurrentThread();
        var cached = Stopwatch.StartNew();
        engine.RebuildFormulaDependencies(wb);
        cached.Stop();
        cachedMemory = GC.GetAllocatedBytesForCurrentThread() - cachedMemory;

        Console.WriteLine(
            $"Dependency rebuild cold: {cold.Elapsed.TotalMilliseconds:F2}ms, {coldMemory:N0} bytes; " +
            $"cached: {cached.Elapsed.TotalMilliseconds:F2}ms, {cachedMemory:N0} bytes");

        sampleFormula.CachedAst.Should().BeSameAs(cachedAst);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(10));
        engine.Recalculate(wb, [a1]);

        sheet.GetValue(c1).Should().Be(new NumberValue(12));
    }

    [Fact]
    public void RebuildFormulaDependencies_PreservesVolatileTrackingWhenReusingCachedAst()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "NOW()");
        sheet.SetFormula(b1, "A1");

        engine.RebuildFormulaDependencies(wb);
        var cachedAst = sheet.GetCell(a1)!.CachedAst;
        cachedAst.Should().NotBeNull();

        engine.RebuildFormulaDependencies(wb);
        sheet.GetCell(a1)!.CachedAst.Should().BeSameAs(cachedAst);

        var report = engine.Recalculate(wb, []);

        report.RecalculatedCells.Should().Contain(a1);
        report.RecalculatedCells.Should().Contain(b1);
        sheet.GetValue(a1).Should().BeOfType<DateTimeValue>();
        sheet.GetValue(b1).Should().Be(sheet.GetValue(a1));
    }

    [Fact]
    public void RegisterFormulaDependencies_VolatileFormulaStillCollectsSiblingReferences()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var ast = new Parser(new Lexer("=NOW()+A1").Tokenize()).Parse();

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "NOW()+A1");
        engine.RegisterFormulaDependencies(b1, ast, sheet.Id, wb);

        graph.GetDirectPrecedents(b1).Should().Contain(a1);
        graph.GetDirectDependents(a1).Should().Contain(b1);
        engine.Recalculate(wb, []).RecalculatedCells.Should().Contain(b1);
    }

    [Fact]
    public void Recalculate_DirtyFormulaRoots_EvaluatesPrecedentsBeforeDependents()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(a2, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        sheet.SetFormula(a1, "A2+1");
        engine.RebuildFormulaDependencies(wb);

        var report = engine.Recalculate(wb, [b1, a1]);

        sheet.GetValue(a1).Should().Be(new NumberValue(2));
        sheet.GetValue(b1).Should().Be(new NumberValue(3));
        report.RecalculatedCells.Should().ContainInOrder(a1, b1);
    }

    [Fact]
    public void RecalculateAllFormulas_EvaluatesInsertedDependentAfterPrecedent()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(a2, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        sheet.SetFormula(a1, "A2+1");

        engine.RecalculateAllFormulas(wb);

        sheet.GetValue(a1).Should().Be(new NumberValue(2));
        sheet.GetValue(b1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Recalculate_ParseFailureClearsStaleDependenciesAndVolatileTracking()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "NOW()+A1");
        engine.RebuildFormulaDependencies(wb);

        sheet.SetFormula(b1, "NOW(");
        var parseReport = engine.Recalculate(wb, [b1]);

        sheet.GetValue(b1).Should().Be(ErrorValue.Value);
        parseReport.Errors.Should().ContainSingle(error => error.Cell.Equals(b1));

        sheet.SetCell(a1, new NumberValue(2));
        var dependencyReport = engine.Recalculate(wb, [a1]);
        dependencyReport.Errors.Should().BeEmpty();
        dependencyReport.RecalculatedCells.Should().BeEmpty();

        var volatileReport = engine.Recalculate(wb, []);
        volatileReport.Errors.Should().BeEmpty();
        volatileReport.RecalculatedCells.Should().BeEmpty();
    }

    [Fact]
    public void Recalculate_NamedFormulaPrecedentEdit_RecalculatesDependentCell()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(2));
        wb.NamedFormulas["DoubleInput"] = "A1*2";
        sheet.SetFormula(b1, "DoubleInput");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [b1]);

        sheet.GetValue(b1).Should().Be(new NumberValue(4));

        sheet.SetCell(a1, new NumberValue(3));
        var report = engine.Recalculate(wb, [a1]);

        sheet.GetValue(b1).Should().Be(new NumberValue(6));
        report.RecalculatedCells.Should().Contain(b1);
    }

    [Fact]
    public void Recalculate_NamedFormulaVolatileFunction_RecalculatesWithoutChangedCells()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        wb.NamedFormulas["Clock"] = "NOW()";
        sheet.SetFormula(a1, "Clock");
        engine.RebuildFormulaDependencies(wb);

        var report = engine.Recalculate(wb, []);

        report.RecalculatedCells.Should().Contain(a1);
        sheet.GetValue(a1).Should().BeOfType<DateTimeValue>();
    }
}

