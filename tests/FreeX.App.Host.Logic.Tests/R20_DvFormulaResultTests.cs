using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Data validation must evaluate a typed formula's computed result, not the blank placeholder on
/// a freshly parsed formula cell. Entry parsing and validation now belong to WorkbookSession, so
/// these tests exercise the portable commit path used by both desktop renderers.
/// </summary>
public sealed class R20_dv_formula_result_Tests
{
    [Fact]
    public void CommitCellText_EvaluatesTypedFormula()
    {
        using var session = CreateSession();
        var sheet = session.ActiveSheet;
        var b1 = new CellAddress(sheet.Id, 1, 2);
        session.SelectCell(b1);

        session.CommitCellText("=100").Success.Should().BeTrue();

        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void FormulaEntry_OutOfRange_IsRejected_WhenAllowBlankTrue()
    {
        using var session = CreateSession();
        var sheet = session.ActiveSheet;
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.DataValidations.Add(CreateWholeNumberRule(b1, allowBlank: true));
        session.SelectCell(b1);

        session.CommitCellText("=100").Success.Should().BeFalse();

        sheet.GetCell(b1).Should().BeNull();
    }

    [Fact]
    public void FormulaEntry_InRange_IsAccepted_WhenAllowBlankFalse()
    {
        using var session = CreateSession();
        var sheet = session.ActiveSheet;
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.DataValidations.Add(CreateWholeNumberRule(b1, allowBlank: false));
        session.SelectCell(b1);

        session.CommitCellText("=5").Success.Should().BeTrue();

        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(5));
    }

    private static WorkbookSession CreateSession() =>
        new WorkbookSessionFactory().CreateNew(120, 160);

    private static DataValidation CreateWholeNumberRule(CellAddress address, bool allowBlank) =>
        new()
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = allowBlank,
            ShowErrorMessage = true,
            AlertStyle = DvAlertStyle.Stop
        };
}
