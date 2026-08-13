using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class TranslateDialogPlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    private static CellAddress Cell(string a1) => CellAddress.Parse(a1, Sheet);

    [Fact]
    public void Languages_IncludeAutoDetectAndDefaultTargets()
    {
        TranslateDialogPlanner.Languages.Should().Contain(o => o.Code == TranslateDialogPlanner.DefaultFromCode);
        TranslateDialogPlanner.Languages.Should().Contain(o => o.Code == TranslateDialogPlanner.DefaultToCode);
        TranslateDialogPlanner.Languages.Select(o => o.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SuggestTargetReference_PicksCellToTheRight()
    {
        TranslateDialogPlanner.SuggestTargetReference(Cell("B2")).Should().Be("C2");
    }

    [Fact]
    public void TryPlan_RejectsEmptyTranslation()
    {
        var ok = TranslateDialogPlanner.TryPlan(
            Sheet, Cell("A1"), "   ", "B1", "auto", "en", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(TranslateDialogValidationError.EmptyTranslation);
    }

    [Fact]
    public void TryPlan_RejectsMissingTarget()
    {
        var ok = TranslateDialogPlanner.TryPlan(
            Sheet, Cell("A1"), "hola", " ", "auto", "en", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(TranslateDialogValidationError.MissingTargetReference);
    }

    [Fact]
    public void TryPlan_RejectsInvalidTargetReference()
    {
        var ok = TranslateDialogPlanner.TryPlan(
            Sheet, Cell("A1"), "hola", "not-a-ref!", "auto", "en", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(TranslateDialogValidationError.InvalidTargetReference);
    }

    [Fact]
    public void TryPlan_RejectsTargetEqualToSourceCell()
    {
        var ok = TranslateDialogPlanner.TryPlan(
            Sheet, Cell("A1"), "hola", "A1", "auto", "en", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(TranslateDialogValidationError.SameSourceAndTarget);
    }

    [Fact]
    public void TryPlan_SingleCellTarget_WritesWholeTranslation()
    {
        var ok = TranslateDialogPlanner.TryPlan(
            Sheet, Cell("A1"), "line1\nline2", "B1", "auto", "fr", out var plan, out var error);

        ok.Should().BeTrue();
        error.Should().Be(TranslateDialogValidationError.None);
        plan.ToLanguageCode.Should().Be("fr");
        plan.FromLanguageCode.Should().Be("auto");
        plan.Writes.Should().ContainSingle();
        plan.Writes[0].Address.Should().Be(Cell("B1"));
        plan.Writes[0].Text.Should().Be("line1\nline2");
    }

    [Fact]
    public void TryPlan_MultiCellTarget_FillsSuccessiveCellsRowByRow()
    {
        var ok = TranslateDialogPlanner.TryPlan(
            Sheet, Cell("A1"), "one\ntwo\nthree", "B1:B3", "auto", "en", out var plan, out _);

        ok.Should().BeTrue();
        plan.Writes.Select(w => (w.Address.ToA1(), w.Text)).Should().Equal(
            ("B1", "one"), ("B2", "two"), ("B3", "three"));
    }

    [Fact]
    public void TryPlan_MultiCellTarget_AppendsOverflowLinesToLastCell()
    {
        var ok = TranslateDialogPlanner.TryPlan(
            Sheet, Cell("A1"), "one\ntwo\nthree\nfour", "B1:B2", "auto", "en", out var plan, out _);

        ok.Should().BeTrue();
        plan.Writes.Should().HaveCount(2);
        plan.Writes[0].Text.Should().Be("one");
        plan.Writes[1].Text.Should().Be("two\nthree\nfour");
        // Never writes outside the chosen 2-cell range.
        plan.Writes.Should().OnlyContain(w => plan.TargetRange.Contains(w.Address));
    }

    [Fact]
    public void BuildCommand_AppliesAndRevertsMultiCellTranslationAtomically()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(b1, new TextValue("old one"));
        sheet.SetCell(b2, new TextValue("old two"));
        TranslateDialogPlanner.TryPlan(
            sheet.Id,
            source,
            "new one\nnew two",
            "B1:B2",
            "en",
            "fr",
            out var plan,
            out _).Should().BeTrue();
        var command = TranslateDialogPlanner.BuildCommand(plan);
        var context = new WorkbookCommandContext(workbook);

        command.Apply(context).Success.Should().BeTrue();
        ((TextValue)sheet.GetCell(b1)!.Value!).Value.Should().Be("new one");
        ((TextValue)sheet.GetCell(b2)!.Value!).Value.Should().Be("new two");

        command.Revert(context);
        ((TextValue)sheet.GetCell(b1)!.Value!).Value.Should().Be("old one");
        ((TextValue)sheet.GetCell(b2)!.Value!).Value.Should().Be("old two");
    }
}
