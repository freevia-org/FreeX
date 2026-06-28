using FluentAssertions;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SheetUI;

public sealed class SheetDialogPlannerTests
{
    [Fact]
    public void CreateSheetNameResult_TrimsInput()
    {
        SheetDialogPlanner.CreateSheetNameResult("  Report  ")
            .Should()
            .Be(new SheetNameDialogResult("Report"));
    }

    [Theory]
    [InlineData("", SheetNameValidationError.Blank)]
    [InlineData("   ", SheetNameValidationError.Blank)]
    [InlineData("This sheet name is far too long for Excel", SheetNameValidationError.TooLong)]
    [InlineData("Bad/Name", SheetNameValidationError.InvalidCharacters)]
    [InlineData("'Report", SheetNameValidationError.InvalidApostrophe)]
    [InlineData("Report'", SheetNameValidationError.InvalidApostrophe)]
    public void TryCreateSheetNameResult_ClassifiesInvalidExcelSheetNames(
        string input,
        SheetNameValidationError expectedError)
    {
        SheetDialogPlanner.TryCreateSheetNameResult(input, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expectedError);
    }

    [Fact]
    public void TryCreateSheetNameResult_AcceptsTrimmedValidSheetName()
    {
        SheetDialogPlanner.TryCreateSheetNameResult("  Report  ", out var result, out var error)
            .Should()
            .BeTrue();

        result.Should().Be(new SheetNameDialogResult("Report"));
        error.Should().BeNull();
    }

    [Fact]
    public void BuildActivateSheetTargets_ListsVisibleSheetsAndSelectsActiveSheet()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        var hidden = workbook.AddSheet("Hidden");
        var second = workbook.AddSheet("Second");
        hidden.IsHidden = true;

        var targets = SheetDialogPlanner.BuildActivateSheetTargets(workbook);
        var selected = SheetDialogPlanner.FindInitialActivateSheetTarget(targets, second.Id);

        targets.Should().Equal(
            new SheetDialogTarget("First", first.Id),
            new SheetDialogTarget("Second", second.Id));
        selected.Should().Be(new SheetDialogTarget("Second", second.Id));
    }

    [Fact]
    public void FindInitialActivateSheetTarget_FallsBackToFirstVisibleTarget()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        workbook.AddSheet("Second");
        var targets = SheetDialogPlanner.BuildActivateSheetTargets(workbook);

        SheetDialogPlanner.FindInitialActivateSheetTarget(targets, SheetId.New())
            .Should()
            .Be(new SheetDialogTarget("First", first.Id));
    }

    [Fact]
    public void UnhideSheetPlanning_CapturesTargetsAndTrimsAcceptedResult()
    {
        var targets = SheetDialogPlanner.BuildUnhideSheetTargets([" Hidden 1 ", "Hidden 2"]);

        targets.Should().Equal(" Hidden 1 ", "Hidden 2");
        SheetDialogPlanner.CanAcceptUnhideSheetTarget("Hidden 2").Should().BeTrue();
        SheetDialogPlanner.CanAcceptUnhideSheetTarget("  ").Should().BeFalse();
        SheetDialogPlanner.CreateUnhideSheetResult("  Hidden 2  ")
            .Should()
            .Be(new UnhideSheetDialogResult("Hidden 2"));
    }
}
