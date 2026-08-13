using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for the Q-calc-settings group's Avalonia fixes:
/// J27 — the Avalonia Options dialog must seed its calc-mode radio buttons from the live
/// workbook's <see cref="Workbook.CalculationMode"/> (not the persisted <c>AppOptions.AutoCalculate</c>
/// default), and must only force-apply a calc-mode change back onto the workbook when the user
/// actually changed it.
/// J58 — iterative-calculation controls (enable/max iterations/max change) must be wired to
/// <see cref="Workbook.IterativeCalculation"/>/<see cref="Workbook.MaxCalculationIterations"/>/
/// <see cref="Workbook.MaxCalculationChange"/> via the shared <see cref="SetIterativeCalculationOptionsCommand"/>.
///
/// The Avalonia Options dialog is built as programmatic C# with local closures (no named fields to
/// drive via reflection like the WPF host's XAML-named controls), so these tests exercise the
/// session-level command integration directly and pin the source fix for the seeding bug, mirroring
/// the existing <c>AvaloniaShellSourceTests</c> source-assertion pattern for this file.
/// </summary>
public sealed class QCalcSettingsAvaloniaOptionsTests
{
    [Fact]
    public void SetIterativeCalculationOptionsCommand_AppliesThroughWorkbookSession()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);
        session.Workbook.IterativeCalculation.Should().BeFalse();

        var result = session.ExecuteReviewCommand(new SetIterativeCalculationOptionsCommand(true, 250, 0.0005));

        result.Success.Should().BeTrue(result.ErrorMessage);
        session.Workbook.IterativeCalculation.Should().BeTrue();
        session.Workbook.MaxCalculationIterations.Should().Be(250);
        session.Workbook.MaxCalculationChange.Should().Be(0.0005);
    }

    [Fact]
    public void SetIterativeCalculationOptionsCommand_RejectsInvalidBoundsThroughWorkbookSession()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 240, viewportWidth: 320);

        var result = session.ExecuteReviewCommand(new SetIterativeCalculationOptionsCommand(true, 0, 0.001));

        result.Success.Should().BeFalse();
        session.Workbook.IterativeCalculation.Should().BeFalse();
    }

    [Fact]
    public void AvaloniaOptions_SeedsCalcRadiosFromLiveWorkbookCalculationMode()
    {
        var source = ReadAvaloniaOptionsSource();

        // J27 fix: the calc-mode radios must be seeded from the live workbook's calculation mode
        // (via the existing CalculationModeIsManual helper), not the persisted AppOptions snapshot
        // loaded a few lines above (`current.AutoCalculate`).
        source.Should().Contain("var calculationState = CalculationOptionsDialogState.FromWorkbook(workbook);");
        source.Should().Contain("var workbookAutoCalculate = calculationState.AutoCalculate;");
        source.Should().Contain("IsChecked = workbookAutoCalculate };");
        source.Should().Contain("IsChecked = !workbookAutoCalculate };");
        source.Should().NotContain("IsChecked = current.AutoCalculate };");
        source.Should().NotContain("IsChecked = !current.AutoCalculate };");
    }

    [Fact]
    public void AvaloniaOptions_ExposesIterativeCalculationControlsSeededFromWorkbook()
    {
        var source = ReadAvaloniaOptionsSource();

        source.Should().Contain("var workbook = _session.Workbook;");
        source.Should().Contain("var iterativeBox = new CheckBox { Content = OptionsText(\"Options_EnableIterativeCalculation\"), IsChecked = workbook.IterativeCalculation };");
        source.Should().Contain("Text = (workbook.MaxCalculationIterations ?? DefaultMaxCalculationIterations).ToString(),");
        source.Should().Contain("Text = (workbook.MaxCalculationChange ?? DefaultMaxCalculationChange).ToString(System.Globalization.CultureInfo.InvariantCulture),");
        source.Should().Contain("AutomationProperties.SetAutomationId(iterativeBox, \"OptionsIterativeCalculationCheckBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(maxIterationsBox, \"OptionsMaxIterationsBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(maxChangeBox, \"OptionsMaxChangeBox\");");
    }

    [Fact]
    public void AvaloniaOptions_UsesWpfFormulaRuleSurfaceWithoutExtraMasterRow()
    {
        var source = ReadAvaloniaOptionsSource();

        // WPF's authoritative Formulas page exposes the individual rule switches, not an
        // additional master switch. Keep the persisted legacy field unchanged while removing
        // its non-authoritative row from the height-constrained paired surface.
        source.Should().NotContain("OptionsEnableErrorCheckingCheckBox");
        source.Should().Contain("current.ErrorCheckingEnabled,");
        source.Should().Contain("FormulaErrorCheckingRuleCatalog.SupportedRules");
        source.Should().Contain("CalculationWorkflow.ChangeFormulaErrorRules(");
        source.Should().NotContain("CalculationCommandPolicy.PlanFormulaErrorRuleChanges(");
    }

    [Fact]
    public void AvaloniaOptions_DelegatesCalculationSubmissionToPresentation()
    {
        var source = ReadAvaloniaOptionsSource();

        source.Should().Contain("CalculationOptionsSubmissionPlanner.Plan(");
        source.Should().Contain("private void ApplyCalculationOptionsSubmission(CalculationOptionsSubmission? submission)");
        source.Should().Contain("CalculationOptionsSubmissionCoordinator.Apply(CalculationWorkflow, submission)");
        source.Should().NotContain("CalculationWorkflow.ChangeIterativeCalculation(");
        source.Should().NotContain("CalculationCommandPolicy.PlanIterativeCalculationChange(");
        source.Should().NotContain("ApplyCalculationRecalculation(");
    }

    [Fact]
    public void AvaloniaOptions_CalcModeChangeDecisionIsNotRendererOwned()
    {
        var source = ReadAvaloniaOptionsSource();

        source.Should().NotContain("var wantManual = !input.AutoCalculate;");
        source.Should().NotContain("SetCalculationMode(wantManual ? WorkbookCalculationMode.Manual : WorkbookCalculationMode.Automatic);");
        source.Should().Contain("CalculationOptionsSubmissionPlanner.Plan(");
    }

    private static string ReadAvaloniaOptionsSource() =>
        File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));
}
