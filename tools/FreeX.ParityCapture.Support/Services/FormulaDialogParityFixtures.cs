using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class EvaluateFormulaParityFixture
{
    public static FormulaEvaluationSummary CreateSummary(SheetId sheetId)
    {
        var address = new CellAddress(sheetId, 6, 4);
        return new FormulaEvaluationSummary(
            sheetId,
            "Sheet1",
            address,
            "=SUM(D2:D5)",
            "469",
            [
                new FormulaEvaluationStep("SUM(D2:D5)", "469"),
                new FormulaEvaluationStep("D2:D5", "{120;85;200;64}"),
                new FormulaEvaluationStep("=SUM(D2:D5)", "469"),
            ]);
    }
}

public static class ErrorCheckingParityFixture
{
    public static IReadOnlyList<FormulaErrorIssue> CreateIssues(SheetId sheetId) =>
    [
        new(
            sheetId,
            "Sheet1",
            new CellAddress(sheetId, 6, 4),
            "D6",
            ErrorValue.DivByZero.Code,
            "=D2/0",
            "Formula divides by zero."),
        new(
            sheetId,
            "Sheet1",
            new CellAddress(sheetId, 7, 4),
            "D7",
            FormulaAuditingService.FormulaStoredAsTextErrorCode,
            null,
            "The formula in this cell is stored as text."),
    ];
}
