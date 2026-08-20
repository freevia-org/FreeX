using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for round-159 finding shared-dialog-validation F1. The WPF ribbon's Define Name
/// dialog (DefineNameBtn_Click in MainWindow.FormulaCommands.cs) must gate its OK button with the same
/// permissive "any parseable formula" check that Name Manager's New/Edit buttons
/// (NamedRangeDialog.xaml.cs) and the actual command-building logic (DefinedNamesSession.PlanSave) use --
/// not the stricter "literal range/cell/existing-name only" check, which would silently reject valid
/// named formulas that the rest of the app accepts.
/// </summary>
public sealed class R159_DefineNameFormulaGateTests
{
    private const string FormulaRefersTo = "=SUM(A1:A10)+1";

    [Fact]
    public void DefinedNamesSession_TryParseRangeAndValidateRefersTo_DisagreeOnAFormula()
    {
        // This is the underlying divergence the finding is about: TryParseRange only accepts a
        // literal range/cell/existing-name reference, while ValidateRefersTo (and therefore
        // PlanSave, which actually builds the save command) accepts any parseable formula.
        var (_, _, session) = CreateSession();

        session.TryParseRange(FormulaRefersTo, out _).Should().BeFalse(
            "TryParseRange only recognizes literal ranges/cells/existing names, not general formulas");
        session.ValidateRefersTo(FormulaRefersTo).IsValid.Should().BeTrue(
            "ValidateRefersTo is the permissive check the command layer (PlanSave) actually uses");
    }

    [Fact]
    public void DefinedNamesSession_PlanSave_AcceptsTheSameFormulaTryParseRangeWouldReject()
    {
        var (_, _, session) = CreateSession();

        var plan = session.PlanSave(new DefinedNameDraft("Total", DefinedNameScope.Workbook, FormulaRefersTo));

        session.TryParseRange(FormulaRefersTo, out _).Should().BeFalse(
            "the strict range gate would have refused this exact text");
        plan.IsValid.Should().BeTrue("PlanSave builds a valid command for the same formula");
        plan.Command.Should().NotBeNull();
    }

    [Fact]
    public void DefineNameBtnClick_WiresTheSamePermissiveGateAsNameManagerNewAndEdit()
    {
        // Pins the actual fix: the WPF ribbon's Define Name entry point (MainWindow.FormulaCommands.cs)
        // must use definedNames.ValidateRefersTo(...) -- the exact same expression Name Manager's
        // New/Edit buttons use in NamedRangeDialog.xaml.cs (isValidRange: rangeText =>
        // _definedNames.ValidateRefersTo(rangeText).IsValid) -- not the stricter TryParseRange.
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");
        var clickHandlerStart = source.IndexOf("private void DefineNameBtn_Click", StringComparison.Ordinal);
        var clickHandlerEnd = source.IndexOf("private void CreateNamesFromSelectionBtn_Click", StringComparison.Ordinal);
        clickHandlerStart.Should().BeGreaterThanOrEqualTo(0);
        clickHandlerEnd.Should().BeGreaterThan(clickHandlerStart);
        var handlerSource = source[clickHandlerStart..clickHandlerEnd];

        handlerSource.Should().Contain(
            "isValidRange: rangeText => definedNames.ValidateRefersTo(rangeText).IsValid",
            "Define Name must accept a formula the same way Name Manager's New/Edit buttons do");
        handlerSource.Should().NotContain(
            "isValidRange: rangeText => definedNames.TryParseRange(rangeText, out _)",
            "the strict range-only gate rejects valid named formulas that PlanSave accepts");
    }

    [Fact]
    public void NamedRangeDialogNewAndEdit_StillUseTheSamePermissiveGate_SiblingUnaffected()
    {
        // Sibling no-regression check: Name Manager's own New/Edit wiring (the source of truth this
        // fix copies from) must be untouched.
        var source = DialogSourceTestSupport.ReadHostSources("NamedRangeDialog.xaml.cs");
        var occurrences = System.Text.RegularExpressions.Regex.Matches(
            source,
            System.Text.RegularExpressions.Regex.Escape(
                "isValidRange: rangeText => _definedNames.ValidateRefersTo(rangeText).IsValid"));
        occurrences.Count.Should().Be(2, "both the New and Edit buttons must keep using the permissive formula gate");
    }

    private static (Workbook Workbook, Sheet Sheet, DefinedNamesSession Session) CreateSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new DefinedNamesSession(workbook, sheet.Id));
    }
}
