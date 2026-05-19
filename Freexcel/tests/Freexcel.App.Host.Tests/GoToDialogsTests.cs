using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class GoToDialogsTests
{
    [Fact]
    public void TryParseAddress_AcceptsA1ReferenceOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        GoToDialog.TryParseAddress("B5", sheetId, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 5, 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotACell")]
    [InlineData("A0")]
    public void TryParseAddress_RejectsInvalidReference(string input)
    {
        GoToDialog.TryParseAddress(input, SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void GetChoices_ExposesExcelGoToSpecialCoreChoices()
    {
        var choices = GoToSpecialDialog.GetChoices();

        choices.Select(choice => choice.Kind).Should().Contain([
            GoToSpecialKind.Blanks,
            GoToSpecialKind.Constants,
            GoToSpecialKind.Formulas,
            GoToSpecialKind.Comments,
            GoToSpecialKind.DataValidation,
            GoToSpecialKind.VisibleCellsOnly]);
    }

    [Fact]
    public void TryParseChoice_MapsDisplayTextThroughExistingParser()
    {
        GoToSpecialDialog.TryParseChoice("Data validation", out var kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.DataValidation);
    }
}
