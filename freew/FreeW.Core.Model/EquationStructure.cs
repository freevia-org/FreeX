namespace FreeW.Core.Model;

/// <summary>
/// Structural (model-level) equality for equations. The model stores every slot twice — a plain-text
/// string and an optional nested <see cref="Equation"/> — and lets adjacent text fragments be split
/// arbitrarily, so two equations that are the same mathematics can differ field by field. This type
/// defines the canonical reading used by the linear-format round trip:
/// <list type="bullet">
/// <item>adjacent <see cref="MathRunKind.Text"/> fragments are one fragment, empty ones do not exist;</item>
/// <item>a slot's content is its nested equation when present, otherwise its text;</item>
/// <item>only the properties meaningful to a fragment's <see cref="MathRunKind"/> are compared.</item>
/// </list>
/// </summary>
public static class EquationStructure
{
    /// <summary>
    /// True when the two equations are structurally the same. The display flag and justification of the
    /// top-level equations are compared only when <paramref name="compareLayout"/> is true; nested slot
    /// equations never carry layout.
    /// </summary>
    public static bool AreEquivalent(Equation? left, Equation? right, bool compareLayout = false)
    {
        if (left is null || right is null)
            return left is null && right is null;

        if (compareLayout
            && (left.IsDisplayMath != right.IsDisplayMath
                || (left.IsDisplayMath && left.Justification != right.Justification)))
        {
            return false;
        }

        return RunsEqual(CanonicalRuns(left.Runs), CanonicalRuns(right.Runs), depth: 0);
    }

    /// <summary>True when the two fragment lists are structurally the same.</summary>
    public static bool AreEquivalent(IReadOnlyList<MathRun> left, IReadOnlyList<MathRun> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return RunsEqual(CanonicalRuns(left), CanonicalRuns(right), depth: 0);
    }

    /// <summary>Merges adjacent text fragments and drops empty ones (one level; slots are untouched).</summary>
    public static List<MathRun> CanonicalRuns(IEnumerable<MathRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var result = new List<MathRun>();
        foreach (var run in runs)
        {
            if (run.Kind != MathRunKind.Text)
            {
                result.Add(run);
                continue;
            }

            if (run.Text.Length == 0)
                continue;

            if (result.Count > 0 && result[^1].Kind == MathRunKind.Text)
                result[^1] = MathRun.PlainText(result[^1].Text + run.Text);
            else
                result.Add(MathRun.PlainText(run.Text));
        }

        return result;
    }

    /// <summary>The canonical content of a slot: the nested equation's fragments when present, else its text.</summary>
    public static List<MathRun> SlotRuns(Equation? equation, string? text) =>
        equation is not null
            ? CanonicalRuns(equation.Runs)
            : string.IsNullOrEmpty(text) ? [] : [MathRun.PlainText(text)];

    /// <summary>True when the canonical slot content is nothing but (possibly empty) plain text.</summary>
    public static bool IsPlainText(IReadOnlyList<MathRun> canonicalRuns, out string text)
    {
        ArgumentNullException.ThrowIfNull(canonicalRuns);

        if (canonicalRuns.Count == 0)
        {
            text = string.Empty;
            return true;
        }

        if (canonicalRuns.Count == 1 && canonicalRuns[0].Kind == MathRunKind.Text)
        {
            text = canonicalRuns[0].Text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    /// <summary>The number of rows a matrix/equation-array grid holds.</summary>
    internal static int RowCount(MathMatrix? matrix) => matrix?.RowCount ?? 0;

    /// <summary>The number of cells in one row of a matrix/equation-array grid.</summary>
    internal static int ColumnCount(MathMatrix matrix, int rowIndex)
    {
        var textColumns = rowIndex < matrix.Rows.Count ? matrix.Rows[rowIndex].Count : 0;
        var equationColumns = rowIndex < matrix.CellEquations.Count ? matrix.CellEquations[rowIndex].Count : 0;
        return Math.Max(textColumns, equationColumns);
    }

    /// <summary>The canonical content of one matrix/equation-array cell.</summary>
    internal static List<MathRun> CellRuns(MathMatrix matrix, int rowIndex, int columnIndex)
    {
        var equation = matrix.CellEquationAt(rowIndex, columnIndex);
        var text = rowIndex < matrix.Rows.Count && columnIndex < matrix.Rows[rowIndex].Count
            ? matrix.Rows[rowIndex][columnIndex]
            : string.Empty;
        return SlotRuns(equation, text);
    }

    private static bool RunsEqual(List<MathRun> left, List<MathRun> right, int depth)
    {
        if (left.Count != right.Count)
            return false;

        // Guards against a (malformed) cyclic slot graph; real equations nest far less deeply.
        if (depth > 256)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!RunEqual(left[index], right[index], depth))
                return false;
        }

        return true;
    }

    private static bool SlotEqual(Equation? leftEquation, string leftText, Equation? rightEquation, string rightText, int depth) =>
        RunsEqual(SlotRuns(leftEquation, leftText), SlotRuns(rightEquation, rightText), depth + 1);

    private static bool RunEqual(MathRun left, MathRun right, int depth)
    {
        if (left.Kind != right.Kind)
            return false;

        switch (left.Kind)
        {
            case MathRunKind.Text:
                return string.Equals(left.Text, right.Text, StringComparison.Ordinal);

            case MathRunKind.Preserved:
                return string.Equals(left.RawOmml, right.RawOmml, StringComparison.Ordinal);

            case MathRunKind.Superscript:
            case MathRunKind.UpperLimit:
                return SlotEqual(left.ScriptBaseEquation, left.Base, right.ScriptBaseEquation, right.Base, depth)
                    && SlotEqual(left.ScriptSupEquation, left.Sup, right.ScriptSupEquation, right.Sup, depth);

            case MathRunKind.Subscript:
            case MathRunKind.LowerLimit:
                return SlotEqual(left.ScriptBaseEquation, left.Base, right.ScriptBaseEquation, right.Base, depth)
                    && SlotEqual(left.ScriptSubEquation, left.Sub, right.ScriptSubEquation, right.Sub, depth);

            case MathRunKind.SubSuperscript:
                return SlotEqual(left.ScriptBaseEquation, left.Base, right.ScriptBaseEquation, right.Base, depth)
                    && SlotEqual(left.ScriptSubEquation, left.Sub, right.ScriptSubEquation, right.Sub, depth)
                    && SlotEqual(left.ScriptSupEquation, left.Sup, right.ScriptSupEquation, right.Sup, depth);

            case MathRunKind.Fraction:
                return left.FractionType == right.FractionType
                    && SlotEqual(left.NumeratorEquation, left.Numerator, right.NumeratorEquation, right.Numerator, depth)
                    && SlotEqual(left.DenominatorEquation, left.Denominator, right.DenominatorEquation, right.Denominator, depth);

            case MathRunKind.Radical:
                return SlotEqual(left.DegreeEquation, left.Degree, right.DegreeEquation, right.Degree, depth)
                    && SlotEqual(left.RadicandEquation, left.Base, right.RadicandEquation, right.Base, depth);

            case MathRunKind.NAry:
                return string.Equals(left.Operator, right.Operator, StringComparison.Ordinal)
                    && SlotEqual(left.NAryLowerLimitEquation, left.Sub, right.NAryLowerLimitEquation, right.Sub, depth)
                    && SlotEqual(left.NAryUpperLimitEquation, left.Sup, right.NAryUpperLimitEquation, right.Sup, depth)
                    && SlotEqual(left.NAryOperandEquation, left.Base, right.NAryOperandEquation, right.Base, depth);

            case MathRunKind.Accent:
                return string.Equals(left.Accent, right.Accent, StringComparison.Ordinal)
                    && SlotEqual(left.DecoratorBaseEquation, left.Base, right.DecoratorBaseEquation, right.Base, depth);

            case MathRunKind.Bar:
                return left.BarTop == right.BarTop
                    && SlotEqual(left.DecoratorBaseEquation, left.Base, right.DecoratorBaseEquation, right.Base, depth);

            case MathRunKind.GroupChar:
                return string.Equals(left.GroupChr, right.GroupChr, StringComparison.Ordinal)
                    && string.Equals(left.GroupChrPos, right.GroupChrPos, StringComparison.OrdinalIgnoreCase)
                    && SlotEqual(left.DecoratorBaseEquation, left.Base, right.DecoratorBaseEquation, right.Base, depth);

            case MathRunKind.FunctionApply:
                return SlotEqual(left.FunctionNameEquation, left.FuncName, right.FunctionNameEquation, right.FuncName, depth)
                    && SlotEqual(left.FunctionArgumentEquation, left.Base, right.FunctionArgumentEquation, right.Base, depth);

            case MathRunKind.Delimiter:
                return DelimiterEqual(left, right, depth);

            case MathRunKind.Matrix:
            case MathRunKind.EquationArray:
                return MatrixEqual(left.Matrix, right.Matrix, depth);

            default:
                return false;
        }
    }

    private static bool DelimiterEqual(MathRun left, MathRun right, int depth)
    {
        if (!string.Equals(left.OpenChar, right.OpenChar, StringComparison.Ordinal)
            || !string.Equals(left.CloseChar, right.CloseChar, StringComparison.Ordinal)
            || left.AdditionalDelimiterArguments.Count != right.AdditionalDelimiterArguments.Count
            || !SlotEqual(left.DelimiterContentEquation, left.Base, right.DelimiterContentEquation, right.Base, depth))
        {
            return false;
        }

        if (left.AdditionalDelimiterArguments.Count == 0)
            return true;

        if (!string.Equals(left.DelimiterSeparator, right.DelimiterSeparator, StringComparison.Ordinal))
            return false;

        for (var index = 0; index < left.AdditionalDelimiterArguments.Count; index++)
        {
            var leftEquation = index < left.AdditionalDelimiterContentEquations.Count
                ? left.AdditionalDelimiterContentEquations[index]
                : null;
            var rightEquation = index < right.AdditionalDelimiterContentEquations.Count
                ? right.AdditionalDelimiterContentEquations[index]
                : null;
            if (!SlotEqual(
                    leftEquation,
                    left.AdditionalDelimiterArguments[index],
                    rightEquation,
                    right.AdditionalDelimiterArguments[index],
                    depth))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatrixEqual(MathMatrix? left, MathMatrix? right, int depth)
    {
        var rowCount = RowCount(left);
        if (rowCount != RowCount(right))
            return false;

        if (left is null || right is null)
            return true;

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var columnCount = ColumnCount(left, rowIndex);
            if (columnCount != ColumnCount(right, rowIndex))
                return false;

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                if (!RunsEqual(
                        CellRuns(left, rowIndex, columnIndex),
                        CellRuns(right, rowIndex, columnIndex),
                        depth + 1))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
