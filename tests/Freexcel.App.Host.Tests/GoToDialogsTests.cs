using System.IO;
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
    public void GoToDialog_ExposesKeyboardAccessKeysForReferenceAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoToDialog.cs"));

        source.Should().Contain("Content = \"_Go to:\"");
        source.Should().Contain("Selection history");
        source.Should().Contain("Content = \"_Reference:\"");
        source.Should().Contain("Target = _addressBox");
        source.Should().Contain("Content = \"S_pecial...\"");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
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
    public void GoToSpecialDialog_ExposesKeyboardAccessKeysForChoicesAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoToSpecialDialog.cs"));

        foreach (var expected in new[]
        {
            "new(GoToSpecialKind.Blanks, \"_Blanks\")",
            "new(GoToSpecialKind.Constants, \"_Constants\")",
            "new(GoToSpecialKind.Formulas, \"_Formulas\")",
            "new(GoToSpecialKind.Comments, \"Co_mments\")",
            "new(GoToSpecialKind.DataValidation, \"_Data validation\")",
            "new(GoToSpecialKind.VisibleCellsOnly, \"_Visible cells only\")"
        })
            source.Should().Contain(expected);

        foreach (var expected in new[]
        {
            "_Current region",
            "Current _array",
            "_Objects",
            "Row _differences",
            "Column di_fferences",
            "_Last cell",
            "_Conditional formats"
        })
            source.Should().Contain(expected);

        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void TryParseChoice_MapsDisplayTextThroughExistingParser()
    {
        GoToSpecialDialog.TryParseChoice("Data validation", out var kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.DataValidation);
    }
}
