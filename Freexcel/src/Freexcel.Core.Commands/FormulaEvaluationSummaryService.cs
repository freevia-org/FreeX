using System.Globalization;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed record FormulaEvaluationSummary(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    string FormulaText,
    string ValueText,
    IReadOnlyList<FormulaEvaluationStep> Steps);

public sealed record FormulaEvaluationStep(string Expression, string ValueText);

public sealed record FormulaEvaluationHighlight(string Prefix, string Highlight, string Suffix);

public sealed class FormulaEvaluationSession
{
    private FormulaEvaluationSession(FormulaEvaluationSummary summary)
    {
        Summary = summary;
    }

    public FormulaEvaluationSummary Summary { get; }
    public int CurrentStepIndex { get; private set; }
    public int CurrentStepNumber => CurrentStepIndex + 1;
    public int StepCount => Summary.Steps.Count;
    public bool CanMovePrevious => CurrentStepIndex > 0;
    public bool CanMoveNext => CurrentStepIndex < Summary.Steps.Count - 1;
    public FormulaEvaluationStep? CurrentStep =>
        Summary.Steps.Count == 0 ? null : Summary.Steps[CurrentStepIndex];
    public FormulaEvaluationHighlight CurrentHighlight => BuildCurrentHighlight();

    public static FormulaEvaluationSession Start(FormulaEvaluationSummary summary) => new(summary);

    public bool MoveNext()
    {
        if (!CanMoveNext)
            return false;

        CurrentStepIndex++;
        return true;
    }

    public bool MovePrevious()
    {
        if (!CanMovePrevious)
            return false;

        CurrentStepIndex--;
        return true;
    }

    public bool StepOut()
    {
        var expression = CurrentStep?.Expression;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        for (var index = CurrentStepIndex + 1; index < Summary.Steps.Count; index++)
        {
            var candidate = Summary.Steps[index].Expression;
            if (candidate.Length > expression.Length &&
                candidate.Contains(expression, StringComparison.OrdinalIgnoreCase))
            {
                CurrentStepIndex = index;
                return true;
            }
        }

        return false;
    }

    private FormulaEvaluationHighlight BuildCurrentHighlight()
    {
        var formula = Summary.FormulaText;
        var expression = CurrentStep?.Expression;
        if (string.IsNullOrEmpty(expression))
            return new FormulaEvaluationHighlight("", formula, "");

        var index = formula.IndexOf(expression, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return new FormulaEvaluationHighlight("", formula, "");

        return new FormulaEvaluationHighlight(
            formula[..index],
            formula.Substring(index, expression.Length),
            formula[(index + expression.Length)..]);
    }
}

public static class FormulaEvaluationSummaryService
{
    public static FormulaEvaluationSummary? GetSummary(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        var cell = sheet?.GetCell(address);
        if (sheet is null || cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
            return null;

        return new FormulaEvaluationSummary(
            sheet.Id,
            sheet.Name,
            address,
            "=" + cell.FormulaText,
            FormatValue(cell.Value),
            BuildSteps(workbook, sheet, cell.FormulaText));
    }

    private static IReadOnlyList<FormulaEvaluationStep> BuildSteps(Workbook workbook, Sheet sheet, string formulaText)
    {
        try
        {
            var ast = new Parser(new Lexer("=" + formulaText).Tokenize()).Parse();
            var evaluator = new FormulaEvaluator();
            var steps = new List<FormulaEvaluationStep>();
            CollectSteps(ast, sheet, workbook, evaluator, steps);
            return steps;
        }
        catch
        {
            return [];
        }
    }

    private static void CollectSteps(
        FormulaNode node,
        Sheet sheet,
        Workbook workbook,
        FormulaEvaluator evaluator,
        List<FormulaEvaluationStep> steps)
    {
        switch (node)
        {
            case BinaryOpNode binary:
                CollectSteps(binary.Left, sheet, workbook, evaluator, steps);
                CollectSteps(binary.Right, sheet, workbook, evaluator, steps);
                break;
            case UnaryOpNode unary:
                CollectSteps(unary.Operand, sheet, workbook, evaluator, steps);
                break;
            case FunctionCallNode function:
                foreach (var arg in function.Arguments)
                    CollectSteps(arg, sheet, workbook, evaluator, steps);
                break;
        }

        steps.Add(new FormulaEvaluationStep(
            FormulaSerializer.Serialize(node),
            FormatValue(evaluator.Evaluate(node, sheet, workbook))));
    }

    private static string FormatValue(ScalarValue value) => value switch
    {
        NumberValue number => number.Value.ToString("G15", CultureInfo.CurrentCulture),
        TextValue text => text.Value,
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        BlankValue => "",
        _ => value.ToString() ?? ""
    };
}
