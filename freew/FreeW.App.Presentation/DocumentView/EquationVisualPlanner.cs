using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum EquationVisualSegmentRole
{
    Text,
    Base,
    Superscript,
    Subscript,
    FractionNumerator,
    FractionBar,
    FractionDenominator,
    RadicalDegree,
    RadicalSign,
    RadicalRadicand,
    NAryOperator,
    NAryLowerLimit,
    NAryUpperLimit,
    NAryOperand,
    MatrixOpenDelimiter,
    MatrixCell,
    MatrixColumnSeparator,
    MatrixRowSeparator,
    MatrixCloseDelimiter,
    AccentMark,
    AccentBase,
    BarMark,
    BarBase,
    DelimiterOpen,
    DelimiterContent,
    DelimiterClose,
    GroupCharMark,
    GroupCharBase,
    FunctionName,
    FunctionOpenDelimiter,
    FunctionArgument,
    FunctionCloseDelimiter,
    LinearFallback
}

public enum EquationVisualElementKind
{
    Segments,
    Fraction,
    Radical,
    NAry,
    Matrix,
    Accent,
    Bar,
    Delimiter,
    GroupChar,
    FunctionApply
}

public enum EquationVisualBaselineRole
{
    Normal,
    Superscript,
    Subscript
}

public sealed record EquationVisualStyle(
    string FontFamily,
    bool Italic,
    double FontSizeScale,
    EquationVisualBaselineRole BaselineRole,
    double BaselineOffsetEm);

public sealed record EquationVisualSegment(
    string Text,
    EquationVisualSegmentRole Role,
    EquationVisualStyle Style);

public sealed record EquationVisualMatrixCell(int RowIndex, int ColumnIndex, string Text);

public sealed record EquationVisualMatrixRow(
    int RowIndex,
    IReadOnlyList<EquationVisualMatrixCell> Cells);

public sealed record EquationVisualElement(
    EquationVisualElementKind Kind,
    string LinearText,
    IReadOnlyList<EquationVisualSegment> Segments,
    string Numerator,
    string Denominator,
    string Radicand,
    string Degree,
    string Operator = "",
    string LowerLimit = "",
    string UpperLimit = "",
    string Operand = "")
{
    public IReadOnlyList<EquationVisualMatrixRow> MatrixRows { get; init; } = [];
    public string BaseText { get; init; } = string.Empty;
    public string Accent { get; init; } = string.Empty;
    public bool BarTop { get; init; } = true;
    public string OpenDelimiter { get; init; } = string.Empty;
    public string CloseDelimiter { get; init; } = string.Empty;
    public string GroupCharacter { get; init; } = string.Empty;
    public string GroupCharacterPosition { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
    public string FunctionArgument { get; init; } = string.Empty;
    public EquationVisualPlan? NumeratorPlan { get; init; }
    public EquationVisualPlan? DenominatorPlan { get; init; }
    public EquationVisualPlan? RadicandPlan { get; init; }
    public EquationVisualPlan? DelimiterContentPlan { get; init; }

    public int MatrixRowCount => MatrixRows.Count;

    public int MatrixColumnCount => MatrixRows.Count == 0
        ? 0
        : MatrixRows.Max(row => row.Cells.Count);

    public bool GroupCharacterTop => !string.Equals(
        GroupCharacterPosition,
        "bot",
        StringComparison.OrdinalIgnoreCase);

    public static EquationVisualElement FromSegments(
        string linearText,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(EquationVisualElementKind.Segments, linearText, segments, string.Empty, string.Empty, string.Empty, string.Empty);

    public static EquationVisualElement Fraction(
        string linearText,
        string numerator,
        string denominator,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? numeratorPlan = null,
        EquationVisualPlan? denominatorPlan = null) =>
        new(EquationVisualElementKind.Fraction, linearText, segments, numerator, denominator, string.Empty, string.Empty)
        {
            NumeratorPlan = numeratorPlan,
            DenominatorPlan = denominatorPlan
        };

    public static EquationVisualElement Radical(
        string linearText,
        string radicand,
        string degree,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? radicandPlan = null) =>
        new(EquationVisualElementKind.Radical, linearText, segments, string.Empty, string.Empty, radicand, degree)
        {
            RadicandPlan = radicandPlan
        };

    public static EquationVisualElement NAry(
        string linearText,
        string @operator,
        string lowerLimit,
        string upperLimit,
        string operand,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.NAry,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            @operator,
            lowerLimit,
            upperLimit,
            operand);

    public static EquationVisualElement Matrix(
        string linearText,
        IReadOnlyList<EquationVisualMatrixRow> rows,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.Matrix,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            MatrixRows = rows
        };

    public static EquationVisualElement AccentElement(
        string linearText,
        string baseText,
        string accent,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.Accent,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            Accent = accent
        };

    public static EquationVisualElement Bar(
        string linearText,
        string baseText,
        bool barTop,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.Bar,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            BarTop = barTop
        };

    public static EquationVisualElement Delimiter(
        string linearText,
        string baseText,
        string openDelimiter,
        string closeDelimiter,
        IReadOnlyList<EquationVisualSegment> segments,
        EquationVisualPlan? delimiterContentPlan = null) =>
        new(
            EquationVisualElementKind.Delimiter,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            OpenDelimiter = openDelimiter,
            CloseDelimiter = closeDelimiter,
            DelimiterContentPlan = delimiterContentPlan
        };

    public static EquationVisualElement GroupChar(
        string linearText,
        string baseText,
        string groupCharacter,
        string groupCharacterPosition,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.GroupChar,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            BaseText = baseText,
            GroupCharacter = groupCharacter,
            GroupCharacterPosition = groupCharacterPosition
        };

    public static EquationVisualElement FunctionApply(
        string linearText,
        string functionName,
        string argument,
        IReadOnlyList<EquationVisualSegment> segments) =>
        new(
            EquationVisualElementKind.FunctionApply,
            linearText,
            segments,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty)
        {
            FunctionName = functionName,
            FunctionArgument = argument
        };
}

public sealed record EquationVisualPlan(
    string LinearText,
    string MathFontFamily,
    bool Italic,
    IReadOnlyList<EquationVisualSegment> Segments,
    IReadOnlyList<EquationVisualElement> Elements);

public static class EquationVisualPlanner
{
    public const string DefaultMathFontFamily = "Cambria Math, Cambria, Times New Roman, serif";
    public const double ScriptFontSizeScale = 0.65;
    public const double StructureFontSizeScale = 0.9;
    public const double SuperscriptBaselineOffsetEm = 0.25;
    public const double SubscriptBaselineOffsetEm = -0.18;
    public const double LargeOperatorFontSizeScale = 1.45;
    public const double DecoratorFontSizeScale = 0.85;
    public const double DelimiterFontSizeScale = 1.25;
    public const string FractionBarText = "\u2044";
    public const string RadicalSignText = "\u221a";
    public const string MatrixOpenDelimiterText = "[";
    public const string MatrixCloseDelimiterText = "]";
    public const string MatrixColumnSeparatorText = "  ";
    public const string MatrixRowSeparatorText = "; ";
    public const string OverbarCueText = "\u00af";
    public const string UnderbarCueText = "_";
    public const string FunctionOpenDelimiterText = "(";
    public const string FunctionCloseDelimiterText = ")";

    private static EquationVisualStyle NormalStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        FontSizeScale: 1.0,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle SuperscriptStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        ScriptFontSizeScale,
        EquationVisualBaselineRole.Superscript,
        SuperscriptBaselineOffsetEm);

    private static EquationVisualStyle SubscriptStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        ScriptFontSizeScale,
        EquationVisualBaselineRole.Subscript,
        SubscriptBaselineOffsetEm);

    private static EquationVisualStyle StructureStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: true,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle LargeOperatorStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        LargeOperatorFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle MatrixDelimiterStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        FontSizeScale: 1.0,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle MatrixSeparatorStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle DecoratorStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        DecoratorFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle DelimiterStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        DelimiterFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle FunctionNameStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    private static EquationVisualStyle FunctionDelimiterStyle { get; } = new(
        DefaultMathFontFamily,
        Italic: false,
        StructureFontSizeScale,
        EquationVisualBaselineRole.Normal,
        BaselineOffsetEm: 0.0);

    public static EquationVisualPlan Build(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);

        return Build(equation, depth: 0);
    }

    private static EquationVisualPlan Build(Equation equation, int depth)
    {
        var segments = new List<EquationVisualSegment>();
        var elements = new List<EquationVisualElement>();
        foreach (var run in equation.Runs)
            AddRunVisual(run, segments, elements, depth);

        if (segments.Count == 0 && equation.LinearText.Length > 0)
        {
            var fallback = new EquationVisualSegment(
                equation.LinearText,
                EquationVisualSegmentRole.LinearFallback,
                NormalStyle);
            segments.Add(fallback);
            elements.Add(EquationVisualElement.FromSegments(equation.LinearText, [fallback]));
        }

        return new EquationVisualPlan(
            equation.LinearText,
            DefaultMathFontFamily,
            Italic: true,
            Segments: segments,
            Elements: elements);
    }

    private static void AddRunVisual(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        switch (run.Kind)
        {
            case MathRunKind.Text:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Text, EquationVisualSegmentRole.Text, NormalStyle));
                break;

            case MathRunKind.Superscript:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Base, EquationVisualSegmentRole.Base, NormalStyle),
                    Segment(run.Sup, EquationVisualSegmentRole.Superscript, SuperscriptStyle));
                break;

            case MathRunKind.Subscript:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Base, EquationVisualSegmentRole.Base, NormalStyle),
                    Segment(run.Sub, EquationVisualSegmentRole.Subscript, SubscriptStyle));
                break;

            case MathRunKind.SubSuperscript:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.Base, EquationVisualSegmentRole.Base, NormalStyle),
                    Segment(run.Sub, EquationVisualSegmentRole.Subscript, SubscriptStyle),
                    Segment(run.Sup, EquationVisualSegmentRole.Superscript, SuperscriptStyle));
                break;

            case MathRunKind.Fraction:
                AddFractionElement(run, segments, elements, depth);
                break;

            case MathRunKind.Radical:
                AddRadicalElement(run, segments, elements, depth);
                break;

            case MathRunKind.NAry:
                AddNAryElement(run, segments, elements);
                break;

            case MathRunKind.Matrix:
                AddMatrixElement(run, segments, elements);
                break;

            case MathRunKind.Accent:
                AddAccentElement(run, segments, elements);
                break;

            case MathRunKind.Bar:
                AddBarElement(run, segments, elements);
                break;

            case MathRunKind.Delimiter:
                AddDelimiterElement(run, segments, elements, depth);
                break;

            case MathRunKind.GroupChar:
                AddGroupCharElement(run, segments, elements);
                break;

            case MathRunKind.FunctionApply:
                AddFunctionApplyElement(run, segments, elements);
                break;

            default:
                AddSegmentElement(
                    run.LinearText,
                    segments,
                    elements,
                    Segment(run.LinearText, EquationVisualSegmentRole.LinearFallback, NormalStyle));
                break;
        }
    }

    private static void AddSegmentElement(
        string linearText,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        params EquationVisualSegment?[] candidates)
    {
        var runSegments = candidates.Where(segment => segment is not null).Cast<EquationVisualSegment>().ToList();
        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.FromSegments(linearText, runSegments));
    }

    private static void AddFractionElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var numeratorPlan = BuildSlotPlan(run.NumeratorEquation, depth);
        var denominatorPlan = BuildSlotPlan(run.DenominatorEquation, depth);
        var numeratorText = numeratorPlan?.LinearText ?? run.Numerator;
        var denominatorText = denominatorPlan?.LinearText ?? run.Denominator;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, numeratorText, EquationVisualSegmentRole.FractionNumerator, StructureStyle);
        AddIfAny(runSegments, FractionBarText, EquationVisualSegmentRole.FractionBar, NormalStyle);
        AddIfAny(runSegments, denominatorText, EquationVisualSegmentRole.FractionDenominator, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Fraction(
            run.LinearText,
            numeratorText,
            denominatorText,
            runSegments,
            numeratorPlan,
            denominatorPlan));
    }

    private static EquationVisualPlan? BuildSlotPlan(Equation? equation, int depth) =>
        equation is null || depth >= MathRun.MaxNestedEquationDepth
            ? null
            : Build(equation, depth + 1);

    private static void AddRadicalElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var radicandPlan = BuildSlotPlan(run.RadicandEquation, depth);
        var radicandText = radicandPlan?.LinearText ?? run.Base;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.Degree, EquationVisualSegmentRole.RadicalDegree, SuperscriptStyle);
        AddIfAny(runSegments, RadicalSignText, EquationVisualSegmentRole.RadicalSign, NormalStyle);
        AddIfAny(runSegments, radicandText, EquationVisualSegmentRole.RadicalRadicand, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Radical(
            run.LinearText,
            radicandText,
            run.Degree,
            runSegments,
            radicandPlan));
    }

    private static void AddNAryElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.Operator, EquationVisualSegmentRole.NAryOperator, LargeOperatorStyle);
        AddIfAny(runSegments, run.Sub, EquationVisualSegmentRole.NAryLowerLimit, SubscriptStyle);
        AddIfAny(runSegments, run.Sup, EquationVisualSegmentRole.NAryUpperLimit, SuperscriptStyle);
        AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.NAryOperand, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.NAry(
            run.LinearText,
            run.Operator,
            run.Sub,
            run.Sup,
            run.Base,
            runSegments));
    }

    private static void AddMatrixElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        if (run.Matrix is null)
        {
            AddSegmentElement(
                run.LinearText,
                segments,
                elements,
                Segment(run.LinearText, EquationVisualSegmentRole.LinearFallback, NormalStyle));
            return;
        }

        var matrixRows = BuildMatrixRows(run.Matrix);
        var runSegments = new List<EquationVisualSegment>
        {
            new(MatrixOpenDelimiterText, EquationVisualSegmentRole.MatrixOpenDelimiter, MatrixDelimiterStyle)
        };

        for (var rowIndex = 0; rowIndex < matrixRows.Count; rowIndex++)
        {
            if (rowIndex > 0)
                runSegments.Add(new EquationVisualSegment(
                    MatrixRowSeparatorText,
                    EquationVisualSegmentRole.MatrixRowSeparator,
                    MatrixSeparatorStyle));

            var row = matrixRows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                if (columnIndex > 0)
                    runSegments.Add(new EquationVisualSegment(
                        MatrixColumnSeparatorText,
                        EquationVisualSegmentRole.MatrixColumnSeparator,
                        MatrixSeparatorStyle));

                var cell = row.Cells[columnIndex];
                runSegments.Add(new EquationVisualSegment(
                    cell.Text,
                    EquationVisualSegmentRole.MatrixCell,
                    StructureStyle));
            }
        }

        runSegments.Add(new EquationVisualSegment(
            MatrixCloseDelimiterText,
            EquationVisualSegmentRole.MatrixCloseDelimiter,
            MatrixDelimiterStyle));

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Matrix(run.LinearText, matrixRows, runSegments));
    }

    private static IReadOnlyList<EquationVisualMatrixRow> BuildMatrixRows(MathMatrix matrix)
    {
        var columnCount = matrix.ColumnCount;
        var rows = new List<EquationVisualMatrixRow>();

        for (var rowIndex = 0; rowIndex < matrix.Rows.Count; rowIndex++)
        {
            var sourceRow = matrix.Rows[rowIndex];
            var cells = new List<EquationVisualMatrixCell>();
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var text = columnIndex < sourceRow.Count ? sourceRow[columnIndex] : string.Empty;
                cells.Add(new EquationVisualMatrixCell(rowIndex, columnIndex, text));
            }

            rows.Add(new EquationVisualMatrixRow(rowIndex, cells));
        }

        return rows;
    }

    private static void AddAccentElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, AccentCueText(run.Accent), EquationVisualSegmentRole.AccentMark, DecoratorStyle);
        AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.AccentBase, StructureStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.AccentElement(
            run.LinearText,
            run.Base,
            run.Accent,
            runSegments));
    }

    private static void AddBarElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var runSegments = new List<EquationVisualSegment>();
        var markText = run.BarTop ? OverbarCueText : UnderbarCueText;
        if (run.BarTop)
        {
            AddIfAny(runSegments, markText, EquationVisualSegmentRole.BarMark, DecoratorStyle);
            AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.BarBase, StructureStyle);
        }
        else
        {
            AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.BarBase, StructureStyle);
            AddIfAny(runSegments, markText, EquationVisualSegmentRole.BarMark, DecoratorStyle);
        }

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Bar(
            run.LinearText,
            run.Base,
            run.BarTop,
            runSegments));
    }

    private static void AddDelimiterElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements,
        int depth)
    {
        var delimiterContentPlan = BuildSlotPlan(run.DelimiterContentEquation, depth);
        var contentText = delimiterContentPlan?.LinearText ?? run.Base;

        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.OpenChar, EquationVisualSegmentRole.DelimiterOpen, DelimiterStyle);
        AddIfAny(runSegments, contentText, EquationVisualSegmentRole.DelimiterContent, StructureStyle);
        AddIfAny(runSegments, run.CloseChar, EquationVisualSegmentRole.DelimiterClose, DelimiterStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.Delimiter(
            run.LinearText,
            contentText,
            run.OpenChar,
            run.CloseChar,
            runSegments,
            delimiterContentPlan));
    }

    private static void AddGroupCharElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var groupOnTop = !string.Equals(run.GroupChrPos, "bot", StringComparison.OrdinalIgnoreCase);
        var runSegments = new List<EquationVisualSegment>();
        if (groupOnTop)
        {
            AddIfAny(runSegments, run.GroupChr, EquationVisualSegmentRole.GroupCharMark, DecoratorStyle);
            AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.GroupCharBase, StructureStyle);
        }
        else
        {
            AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.GroupCharBase, StructureStyle);
            AddIfAny(runSegments, run.GroupChr, EquationVisualSegmentRole.GroupCharMark, DecoratorStyle);
        }

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.GroupChar(
            run.LinearText,
            run.Base,
            run.GroupChr,
            run.GroupChrPos,
            runSegments));
    }

    private static void AddFunctionApplyElement(
        MathRun run,
        List<EquationVisualSegment> segments,
        List<EquationVisualElement> elements)
    {
        var runSegments = new List<EquationVisualSegment>();
        AddIfAny(runSegments, run.FuncName, EquationVisualSegmentRole.FunctionName, FunctionNameStyle);
        if (!string.IsNullOrEmpty(run.FuncName))
            AddIfAny(runSegments, FunctionOpenDelimiterText, EquationVisualSegmentRole.FunctionOpenDelimiter, FunctionDelimiterStyle);
        AddIfAny(runSegments, run.Base, EquationVisualSegmentRole.FunctionArgument, StructureStyle);
        if (!string.IsNullOrEmpty(run.FuncName))
            AddIfAny(runSegments, FunctionCloseDelimiterText, EquationVisualSegmentRole.FunctionCloseDelimiter, FunctionDelimiterStyle);

        if (runSegments.Count == 0)
            return;

        segments.AddRange(runSegments);
        elements.Add(EquationVisualElement.FunctionApply(
            run.LinearText,
            run.FuncName,
            run.Base,
            runSegments));
    }

    private static string AccentCueText(string accent)
    {
        if (string.IsNullOrEmpty(accent))
            return "^";

        return accent switch
        {
            "\u0302" => "^",
            "\u0303" => "~",
            "\u0304" => OverbarCueText,
            "\u0307" => ".",
            "\u0308" => "..",
            "\u20d7" => "\u2192",
            _ => accent
        };
    }

    private static EquationVisualSegment? Segment(
        string? text,
        EquationVisualSegmentRole role,
        EquationVisualStyle style)
    {
        return string.IsNullOrEmpty(text)
            ? null
            : new EquationVisualSegment(text, role, style);
    }

    private static void AddIfAny(
        List<EquationVisualSegment> segments,
        string? text,
        EquationVisualSegmentRole role,
        EquationVisualStyle style)
    {
        if (!string.IsNullOrEmpty(text))
            segments.Add(new EquationVisualSegment(text, role, style));
    }
}
