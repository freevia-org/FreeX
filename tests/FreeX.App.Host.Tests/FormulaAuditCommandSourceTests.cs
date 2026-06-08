using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormulaAuditCommandSourceTests
{
    [Fact]
    public void TraceRibbonCommands_AddNextPlannerArrowsWithoutClearingSuccessfulTraces()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");
        var precedentTrace = source[
            source.IndexOf("private void TracePrecedentsForCell", StringComparison.Ordinal)..
            source.IndexOf("private void TraceDependentsBtn_Click", StringComparison.Ordinal)];
        var dependentTrace = source[
            source.IndexOf("private void TraceDependentsBtn_Click", StringComparison.Ordinal)..
            source.IndexOf("private void RemoveArrowsBtn_Click", StringComparison.Ordinal)];

        precedentTrace.Should().Contain("FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows");
        precedentTrace.Should().Contain("_formulaTraceArrows.AddRange(arrows);");
        precedentTrace.Should().NotContain("RemoveAll");
        precedentTrace.Should().NotContain("_formulaTraceArrows.Clear();");
        precedentTrace.Should().NotContain("directly references");

        dependentTrace.Should().Contain("FormulaTraceArrowPlanner.GetNextDependentTraceArrows");
        dependentTrace.Should().Contain("_formulaTraceArrows.AddRange(arrows);");
        dependentTrace.Should().NotContain("RemoveAll");
        dependentTrace.Should().NotContain("_formulaTraceArrows.Clear();");
        dependentTrace.Should().NotContain("is directly referenced by");
    }
}
