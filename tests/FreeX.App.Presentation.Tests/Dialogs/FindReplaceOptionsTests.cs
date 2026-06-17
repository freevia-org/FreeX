using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class FindReplaceOptionsTests
{
    [Fact]
    public void Defaults_MatchExcelDialogDefaults()
    {
        var options = new FindReplaceOptions();

        options.LookIn.Should().Be(FindLookIn.Values);
        options.Within.Should().Be(FindWithin.Sheet);
        options.Search.Should().Be(FindSearchOrder.ByRows);
        options.MatchCase.Should().BeFalse();
        options.MatchEntireCell.Should().BeFalse();
        options.HasFormatConstraint.Should().BeFalse();
    }

    [Fact]
    public void ToFindOptions_ProjectsScopeFieldsOntoServiceRecord()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var format = new StyleDiff(Bold: true);
        var options = new FindReplaceOptions(
            LookIn: FindLookIn.Formulas,
            Within: FindWithin.Workbook,
            Search: FindSearchOrder.ByColumns,
            MatchCase: true,
            MatchEntireCell: true,
            CurrentSheetId: sheetId,
            RequiredFormat: format);

        var serviceOptions = options.ToFindOptions();

        serviceOptions.LookIn.Should().Be(FindLookIn.Formulas);
        serviceOptions.Within.Should().Be(FindWithin.Workbook);
        serviceOptions.SearchOrder.Should().Be(FindSearchOrder.ByColumns);
        serviceOptions.CurrentSheetId.Should().Be(sheetId);
        serviceOptions.RequiredFormat.Should().Be(format);
    }

    [Fact]
    public void FromFindOptions_RestoresDialogDtoIncludingMatchToggles()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var serviceOptions = new FindOptions(
            Within: FindWithin.Workbook,
            CurrentSheetId: sheetId,
            SearchOrder: FindSearchOrder.ByColumns,
            LookIn: FindLookIn.Notes);

        var dto = FindReplaceOptions.FromFindOptions(serviceOptions, matchCase: true, matchEntireCell: true);

        dto.LookIn.Should().Be(FindLookIn.Notes);
        dto.Within.Should().Be(FindWithin.Workbook);
        dto.Search.Should().Be(FindSearchOrder.ByColumns);
        dto.CurrentSheetId.Should().Be(sheetId);
        dto.MatchCase.Should().BeTrue();
        dto.MatchEntireCell.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ThroughServiceRecord_PreservesScopeFields()
    {
        var original = new FindReplaceOptions(
            LookIn: FindLookIn.Comments,
            Within: FindWithin.Workbook,
            Search: FindSearchOrder.ByColumns,
            MatchCase: true,
            MatchEntireCell: false,
            CurrentSheetId: new SheetId(Guid.NewGuid()));

        var roundTripped = FindReplaceOptions.FromFindOptions(
            original.ToFindOptions(),
            original.MatchCase,
            original.MatchEntireCell);

        roundTripped.Should().Be(original);
    }

    [Fact]
    public void HasFormatConstraint_IsTrue_WhenRequiredFormatPresent()
    {
        var options = new FindReplaceOptions(RequiredFormat: new StyleDiff(Italic: true));

        options.HasFormatConstraint.Should().BeTrue();
    }

    [Theory]
    [InlineData(FindLookIn.Formulas)]
    [InlineData(FindLookIn.Values)]
    [InlineData(FindLookIn.Notes)]
    [InlineData(FindLookIn.Comments)]
    public void LookIn_RoundTripsForEveryServiceValue(FindLookIn lookIn)
    {
        var dto = new FindReplaceOptions(LookIn: lookIn);

        dto.ToFindOptions().LookIn.Should().Be(lookIn);
    }
}
