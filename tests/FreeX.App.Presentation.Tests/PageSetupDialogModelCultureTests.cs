using System.Collections.Generic;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Regression coverage for G39: the Page Setup header/footer margin fields must accept the
/// current-culture decimal separator (e.g. ',' under de-DE/ru-RU) as well as the invariant '.'
/// separator, matching the sibling numeric-input parsing patterns used elsewhere in the app
/// (<see cref="FreeX.App.Presentation.NumericInputParser"/>).
/// </summary>
public sealed class PageSetupDialogModelCultureTests
{
    [Fact]
    public void TryBuildCommandPlan_AcceptsCommaDecimalHeaderFooterMarginsUnderDeDeCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            HeaderMarginText = "0,6",
            FooterMarginText = "0,7",
        };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeTrue();
        result.Plan!.PageSetupCommand.Apply(new PageSetupCultureTestCommandContext(workbook));
        sheet.HeaderMargin.Should().Be(0.6);
        sheet.FooterMargin.Should().Be(0.7);
    }

    [Fact]
    public void TryBuildCommandPlan_StillAcceptsInvariantDotDecimalHeaderFooterMarginsUnderDeDeCulture()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("de-DE");

        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            HeaderMarginText = "0.6",
            FooterMarginText = "0.7",
        };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeTrue();
        result.Plan!.PageSetupCommand.Apply(new PageSetupCultureTestCommandContext(workbook));
        sheet.HeaderMargin.Should().Be(0.6);
        sheet.FooterMargin.Should().Be(0.7);
    }

    private sealed class PageSetupCultureTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
