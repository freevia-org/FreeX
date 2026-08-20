using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed record FormulaEvaluationSummary(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    string FormulaText,
    string ValueText,
    IReadOnlyList<FormulaEvaluationStep> Steps);

public sealed record FormulaEvaluationStep(
    string Expression,
    string ValueText,
    FormulaEvaluationSummary? NestedSummary = null);

public sealed record FormulaEvaluationHighlight(string Prefix, string Highlight, string Suffix);

public sealed class FormulaEvaluationSession
{
    private readonly Stack<FormulaEvaluationSessionFrame> _parentFrames = [];
    private FormulaEvaluationSummary _summary;

    private FormulaEvaluationSession(FormulaEvaluationSummary summary)
    {
        _summary = summary;
    }

    public FormulaEvaluationSummary Summary => _summary;
    public int CurrentStepIndex { get; private set; }
    public int CurrentStepNumber => CurrentStepIndex + 1;
    public int StepCount => Summary.Steps.Count;
    public bool CanMovePrevious => CurrentStepIndex > 0;
    public bool CanMoveNext => CurrentStepIndex < Summary.Steps.Count - 1;
    public bool CanStepIn => CurrentStep?.NestedSummary is not null;
    public bool CanStepOut => _parentFrames.Count > 0 || FindContainingStepIndex() is not null;
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

    public bool StepIn()
    {
        if (CurrentStep?.NestedSummary is not { } nestedSummary)
            return false;

        _parentFrames.Push(new FormulaEvaluationSessionFrame(_summary, CurrentStepIndex));
        _summary = nestedSummary;
        CurrentStepIndex = 0;
        return true;
    }

    public bool StepOut()
    {
        if (_parentFrames.TryPop(out var parent))
        {
            _summary = parent.Summary;
            CurrentStepIndex = parent.StepIndex;
            return true;
        }

        if (FindContainingStepIndex() is { } containingStepIndex)
        {
            CurrentStepIndex = containingStepIndex;
            return true;
        }

        return false;
    }

    private int? FindContainingStepIndex()
    {
        var expression = CurrentStep?.Expression;
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        for (var index = CurrentStepIndex + 1; index < Summary.Steps.Count; index++)
        {
            var candidate = Summary.Steps[index].Expression;
            if (candidate.Length > expression.Length &&
                candidate.Contains(expression, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    private FormulaEvaluationHighlight BuildCurrentHighlight()
    {
        var formula = Summary.FormulaText;
        var expression = CurrentStep?.Expression;
        if (string.IsNullOrEmpty(expression))
            return new FormulaEvaluationHighlight("", formula, "");

        var occurrence = CountPriorOccurrences(expression) + 1;
        var index = FindExpressionTokenIndex(formula, expression, occurrence);
        if (index < 0)
            return new FormulaEvaluationHighlight("", formula, "");

        return new FormulaEvaluationHighlight(
            formula[..index],
            formula.Substring(index, expression.Length),
            formula[(index + expression.Length)..]);
    }

    /// <summary>
    /// Counts how many steps before <see cref="CurrentStepIndex"/> share the current step's
    /// serialized expression text. CollectSteps walks the AST left-to-right in the same order the
    /// corresponding sub-expressions appear in the source formula, so the Nth step carrying a given
    /// expression text lines up with the Nth textual occurrence of that text in the formula. This
    /// lets a repeated identical sub-expression (e.g. "=A1+A1") highlight the occurrence the current
    /// step actually evaluates instead of always the first one.
    /// </summary>
    private int CountPriorOccurrences(string expression)
    {
        var count = 0;
        for (var i = 0; i < CurrentStepIndex; i++)
        {
            if (string.Equals(Summary.Steps[i].Expression, expression, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Finds the <paramref name="occurrence"/>-th (1-based) match of <paramref name="expression"/>
    /// inside <paramref name="formula"/>, skipping matches that are merely a substring of a longer
    /// reference/identifier token (e.g. "A1" inside "A11") so a repeated identical sub-expression or
    /// a prefix collision highlights the correct span rather than an embedded occurrence.
    /// </summary>
    private static int FindExpressionTokenIndex(string formula, string expression, int occurrence)
    {
        var searchStart = 0;
        var remaining = occurrence;
        while (searchStart <= formula.Length - expression.Length)
        {
            var index = formula.IndexOf(expression, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return -1;

            var boundaryBefore = index == 0 ||
                !IsFormulaHighlightTokenChar(expression[0]) ||
                !IsFormulaHighlightTokenChar(formula[index - 1]);

            var endIndex = index + expression.Length;
            var boundaryAfter = endIndex >= formula.Length ||
                !IsFormulaHighlightTokenChar(expression[^1]) ||
                !IsFormulaHighlightTokenChar(formula[endIndex]);

            if (boundaryBefore && boundaryAfter)
            {
                remaining--;
                if (remaining == 0)
                    return index;
            }

            searchStart = index + 1;
        }

        return -1;
    }

    private static bool IsFormulaHighlightTokenChar(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '.' or '$';
}

internal sealed record FormulaEvaluationSessionFrame(FormulaEvaluationSummary Summary, int StepIndex);

public static class FormulaEvaluationSummaryService
{
    public static FormulaEvaluationSummary? GetSummary(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        var cell = sheet?.GetCell(address);
        if (sheet is null || cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
            return null;

        return BuildSummary(workbook, sheet, address, cell, []);
    }

    private static FormulaEvaluationSummary BuildSummary(
        Workbook workbook,
        Sheet sheet,
        CellAddress address,
        Cell cell,
        HashSet<CellAddress> visited)
    {
        visited.Add(address);

        return new FormulaEvaluationSummary(
            sheet.Id,
            sheet.Name,
            address,
            "=" + cell.FormulaText,
            FormatValue(cell.Value),
            BuildSteps(workbook, sheet, cell.FormulaText!, visited));
    }

    private static IReadOnlyList<FormulaEvaluationStep> BuildSteps(
        Workbook workbook,
        Sheet sheet,
        string formulaText,
        HashSet<CellAddress> visited)
    {
        try
        {
            var ast = new Parser(new Lexer("=" + formulaText).Tokenize()).Parse();
            var evaluator = new FormulaEvaluator();
            var steps = new List<FormulaEvaluationStep>();
            CollectSteps(ast, sheet, workbook, evaluator, steps, visited);
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
        List<FormulaEvaluationStep> steps,
        HashSet<CellAddress> visited)
    {
        switch (node)
        {
            case BinaryOpNode binary:
                CollectSteps(binary.Left, sheet, workbook, evaluator, steps, visited);
                CollectSteps(binary.Right, sheet, workbook, evaluator, steps, visited);
                break;
            case UnaryOpNode unary:
                CollectSteps(unary.Operand, sheet, workbook, evaluator, steps, visited);
                break;
            case FunctionCallNode function:
                foreach (var arg in function.Arguments)
                    CollectSteps(arg, sheet, workbook, evaluator, steps, visited);
                break;
        }

        steps.Add(new FormulaEvaluationStep(
            FormulaSerializer.Serialize(node),
            FormatValue(evaluator.Evaluate(node, sheet, workbook)),
            TryBuildNestedSummary(workbook, sheet, node, visited)));
    }

    private static FormulaEvaluationSummary? TryBuildNestedSummary(
        Workbook workbook,
        Sheet sheet,
        FormulaNode node,
        HashSet<CellAddress> visited)
    {
        if (node is not CellRefNode cellRef)
            return null;

        // A precedent reference carrying an explicit SheetName is a cross-sheet reference
        // (see FormulaEvaluator.References.cs) -- resolve it to its own sheet so Step In
        // works for cross-sheet precedents the same way Excel allows, not just same-sheet ones.
        var targetSheet = cellRef.SheetName is null ? sheet : workbook.GetSheet(cellRef.SheetName);
        if (targetSheet is null)
            return null;

        var address = new CellAddress(targetSheet.Id, cellRef.Row, cellRef.ColumnNumber);
        if (!visited.Add(address))
            return null;

        try
        {
            var cell = targetSheet.GetCell(address);
            if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
                return null;

            return BuildSummary(workbook, targetSheet, address, cell, new HashSet<CellAddress>(visited));
        }
        finally
        {
            visited.Remove(address);
        }
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
