using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView.MathLayout;

/// <summary>TeX/OpenType math styles: display, text, script and script-script.</summary>
public enum MathLayoutStyle
{
    Display,
    Text,
    Script,
    ScriptScript
}

/// <summary>A positioned drawing instruction. Coordinates are DIPs, y grows downwards, the equation's
/// baseline is y = 0 and its left edge x = 0.</summary>
public abstract record MathDrawItem
{
    /// <summary>Returns a copy moved by (dx, dy).</summary>
    public abstract MathDrawItem Offset(double dx, double dy);
}

/// <summary>Text drawn with the math font, its baseline at (<see cref="X"/>, <see cref="BaselineY"/>).</summary>
public sealed record MathTextDrawItem(string Text, double X, double BaselineY, double FontSize) : MathDrawItem
{
    public override MathDrawItem Offset(double dx, double dy) => this with { X = X + dx, BaselineY = BaselineY + dy };
}

/// <summary>A single math-font glyph by id (a size variant or assembly part), baseline at (X, BaselineY).</summary>
public sealed record MathGlyphDrawItem(int GlyphId, double X, double BaselineY, double FontSize) : MathDrawItem
{
    public override MathDrawItem Offset(double dx, double dy) => this with { X = X + dx, BaselineY = BaselineY + dy };
}

/// <summary>A filled rectangle (fraction rule, radical overbar, over/under bar); (X, Y) is its top-left.</summary>
public sealed record MathRuleDrawItem(double X, double Y, double Width, double Height) : MathDrawItem
{
    public override MathDrawItem Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>The laid-out equation: its extent around the baseline and the drawing instructions.</summary>
public sealed record MathLayoutResult(
    double Width,
    double Ascent,
    double Descent,
    MathLayoutStyle Style,
    IReadOnlyList<MathDrawItem> Items)
{
    public double Height => Ascent + Descent;

    /// <summary>
    /// Extent above the baseline the equation claims in its line: its ink plus half the font's MathLeading.
    /// Word sizes a line holding a tall inline equation this way (lines measured against Word renders).
    /// </summary>
    public double LineAscent { get; init; } = double.NaN;

    /// <summary>Extent below the baseline the equation claims in its line; see <see cref="LineAscent"/>.</summary>
    public double LineDescent { get; init; } = double.NaN;

    /// <summary><see cref="LineAscent"/>, or the ink ascent when no line box was computed.</summary>
    public double BoxAscent => double.IsNaN(LineAscent) ? Ascent : Math.Max(Ascent, LineAscent);

    /// <summary><see cref="LineDescent"/>, or the ink descent when no line box was computed.</summary>
    public double BoxDescent => double.IsNaN(LineDescent) ? Descent : Math.Max(Descent, LineDescent);
}

/// <summary>
/// Recursive box layout for <see cref="Equation"/>s, reproducing Word's built-up ("Professional") math:
/// each node measures to a box (width, ascent, descent, italic correction) and children are placed with the
/// OpenType MATH rules Word itself applies with Cambria Math — fractions on the math axis with display/text
/// shifts, scripts with shift/drop/gap rules and italic correction, n-ary limits centred above/below in
/// display (integrals and inline operators take side scripts, as Word's default m:naryLim/m:intLim do),
/// radicals with a size-variant surd and kerned degree, delimiters and group characters stretched from the
/// font's glyph variants/assemblies, TeX inter-atom spacing, math-italic letters, and 73%/60% script sizes.
/// The engine is toolkit-neutral: it emits <see cref="MathDrawItem"/>s that a WPF/Avalonia adapter paints.
/// </summary>
public static class EquationMathLayout
{
    /// <summary>Word's thin space between a function name and its argument is TeX's 3mu.</summary>
    private const double MuPerEm = 18.0;

    /// <summary>Display integrals request this multiple of DisplayOperatorMinHeight (see AddNAry).</summary>
    private const double DisplayIntegralHeightFactor = 1.7;

    /// <summary>Lays out an equation at <paramref name="fontSize"/> (DIPs per em).</summary>
    public static MathLayoutResult Layout(
        Equation equation,
        IMathLayoutFont font,
        double fontSize,
        bool? display = null,
        MathDocumentSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(equation);
        ArgumentNullException.ThrowIfNull(font);

        settings ??= MathDocumentSettings.Default;

        // m:dispDef off lays display equations out with the inline (text-style) rules.
        var style = (display ?? equation.IsDisplayMath) && settings.DisplayDefaults ? MathLayoutStyle.Display : MathLayoutStyle.Text;
        var engine = new Engine(font, fontSize <= 0 ? 14.6667 : fontSize, settings);
        var box = engine.Row(equation.Runs, new Ctx(style, Cramped: false, Upright: false), depth: 0);
        // Word keeps the font's MathLeading of white space around an equation when it sizes the line, half
        // above and half below its ink, so a line grown by a tall inline equation gets that leading too.
        var leading = font.Constants.MathLeading * (fontSize <= 0 ? 14.6667 : fontSize) / 2;
        return new MathLayoutResult(box.Width, box.Ascent, box.Descent, style, box.Items)
        {
            LineAscent = box.Ascent + leading,
            LineDescent = box.Descent + leading
        };
    }

    private readonly record struct Ctx(MathLayoutStyle Style, bool Cramped, bool Upright)
    {
        public bool IsScript => Style is MathLayoutStyle.Script or MathLayoutStyle.ScriptScript;

        public Ctx ScriptStyle(bool cramped) => this with
        {
            Style = Style is MathLayoutStyle.Display or MathLayoutStyle.Text ? MathLayoutStyle.Script : MathLayoutStyle.ScriptScript,
            Cramped = Cramped || cramped,
            Upright = false
        };

        public Ctx ScriptScriptStyle() => this with { Style = MathLayoutStyle.ScriptScript, Cramped = true, Upright = false };

        public Ctx FractionPart(bool denominator) => this with
        {
            Style = Style switch
            {
                MathLayoutStyle.Display => MathLayoutStyle.Text,
                MathLayoutStyle.Text => MathLayoutStyle.Script,
                _ => MathLayoutStyle.ScriptScript
            },
            Cramped = Cramped || denominator,
            Upright = false
        };

        public Ctx WithCramped() => this with { Cramped = true };

        public Ctx Plain() => this with { Upright = false };
    }

    /// <summary>A laid-out box: extents around its baseline plus its drawing items (relative to its origin).</summary>
    private sealed class Box
    {
        public double Width;
        public double Ascent;
        public double Descent;
        public double Italic;
        public double TopAccentX = double.NaN;
        public bool IsSingleCharacter;
        public bool IsSpace;
        public MathAtomClass FirstClass = MathAtomClass.Ord;
        public MathAtomClass LastClass = MathAtomClass.Ord;
        public readonly List<MathDrawItem> Items = [];

        public MathAtomClass Class
        {
            set
            {
                FirstClass = value;
                LastClass = value;
            }
        }

        public double Height => Ascent + Descent;

        /// <summary>Places a child with its origin at (x, -up): <paramref name="up"/> raises it.</summary>
        public void Add(Box child, double x, double up)
        {
            foreach (var item in child.Items)
                Items.Add(item.Offset(x, -up));
            Ascent = Math.Max(Ascent, child.Ascent + up);
            Descent = Math.Max(Descent, child.Descent - up);
            Width = Math.Max(Width, x + child.Width);
        }

        public void AddRule(double x, double top, double width, double height)
        {
            if (width <= 0 || height <= 0)
                return;
            Items.Add(new MathRuleDrawItem(x, top, width, height));
            Ascent = Math.Max(Ascent, -top);
            Descent = Math.Max(Descent, top + height);
            Width = Math.Max(Width, x + width);
        }
    }

    private sealed class Engine(IMathLayoutFont font, double baseSize, MathDocumentSettings settings)
    {
        private readonly MathFontConstants _k = font.Constants;

        private double Size(Ctx ctx) => ctx.Style switch
        {
            MathLayoutStyle.Script => baseSize * _k.ScriptPercentScaleDown / 100.0,
            MathLayoutStyle.ScriptScript => baseSize * _k.ScriptScriptPercentScaleDown / 100.0,
            _ => baseSize
        };

        // ── rows and atoms ────────────────────────────────────────────────────────────────────────

        public Box Row(IEnumerable<MathRun> runs, Ctx ctx, int depth)
        {
            var items = new List<Box>();
            foreach (var run in runs)
                AddRun(run, ctx, depth, items);
            return Join(items, ctx);
        }

        private Box Slot(Equation? equation, string fallback, Ctx ctx, int depth)
        {
            if (equation is not null && depth < MathRun.MaxNestedEquationDepth)
                return Row(equation.Runs, ctx, depth + 1);

            var items = new List<Box>();
            AddText(fallback, ctx, items);
            return Join(items, ctx);
        }

        /// <summary>Concatenates boxes left to right, inserting TeX inter-atom spacing between them.</summary>
        private Box Join(List<Box> items, Ctx ctx)
        {
            var row = new Box();
            if (items.Count == 0)
                return row;

            ResolveBinaryOperators(items);
            var size = Size(ctx);
            double x = 0;
            double firstRealX = 0;
            Box? previous = null;
            Box? firstReal = null;
            foreach (var item in items)
            {
                if (item.IsSpace)
                {
                    x += item.Width;
                    continue;
                }

                if (previous is not null)
                    x += MathText.SpacingMu(previous.LastClass, item.FirstClass, ctx.IsScript) / MuPerEm * size;

                if (firstReal is null)
                {
                    firstReal = item;
                    firstRealX = x;
                }

                row.Add(item, x, 0);
                x += item.Width;
                previous = item;
            }

            row.Width = Math.Max(row.Width, x);
            if (firstReal is not null && previous is not null)
            {
                row.FirstClass = firstReal.FirstClass;
                row.LastClass = previous.LastClass;
                row.Italic = ReferenceEquals(previous, items[^1]) ? previous.Italic : 0;
                if (ReferenceEquals(firstReal, previous))
                {
                    row.IsSingleCharacter = firstReal.IsSingleCharacter;
                    row.TopAccentX = firstRealX + firstReal.TopAccentX;
                }
            }

            return row;
        }

        /// <summary>TeX's rule: a binary operator with no left operand (or before a relation/closer) is ordinary.</summary>
        private static void ResolveBinaryOperators(List<Box> items)
        {
            var real = items.Where(item => !item.IsSpace).ToList();
            for (var i = 0; i < real.Count; i++)
            {
                if (real[i].FirstClass != MathAtomClass.Bin)
                    continue;

                var left = i == 0 ? (MathAtomClass?)null : real[i - 1].LastClass;
                var right = i == real.Count - 1 ? (MathAtomClass?)null : real[i + 1].FirstClass;
                if (left is null or MathAtomClass.Bin or MathAtomClass.Op or MathAtomClass.Rel or MathAtomClass.Open or MathAtomClass.Punct
                    || right is null or MathAtomClass.Rel or MathAtomClass.Close or MathAtomClass.Punct)
                {
                    real[i].Class = MathAtomClass.Ord;
                }
            }
        }

        private void AddText(string text, Ctx ctx, List<Box> items)
        {
            foreach (var atom in MathText.Atomize(text, ctx.Upright))
            {
                if (atom.IsSpace)
                {
                    var space = font.MeasureText(" ").Width;
                    items.Add(new Box { Width = (space > 0 ? space : 0.22) * Size(ctx), IsSpace = true });
                    continue;
                }

                var box = TextBox(atom.Text, ctx);
                box.Class = atom.Class;
                items.Add(box);
            }
        }

        private Box TextBox(string text, Ctx ctx)
        {
            var size = Size(ctx);
            var metrics = font.MeasureText(text);
            var box = new Box
            {
                Width = metrics.Width * size,
                Ascent = metrics.Ascent * size,
                Descent = metrics.Descent * size,
                Italic = metrics.ItalicCorrection * size,
                IsSingleCharacter = text.EnumerateRunes().Count() == 1
            };
            box.Items.Add(new MathTextDrawItem(text, 0, 0, size));
            if (box.IsSingleCharacter)
            {
                var codePoint = text.EnumerateRunes().First().Value;
                box.TopAccentX = font.TryGetGlyph(codePoint, out var glyph) && font.GetTopAccentAttachment(glyph) is { } attachment
                    ? attachment * size
                    : ((metrics.InkLeft + metrics.InkRight) / 2 * size) + (box.Italic / 2);
            }

            return box;
        }

        private Box GlyphBox(int glyphId, double size, double? italicOverride = null)
        {
            var metrics = font.MeasureGlyph(glyphId);
            var box = new Box
            {
                Width = metrics.Width * size,
                Ascent = metrics.Ascent * size,
                Descent = metrics.Descent * size,
                Italic = (italicOverride ?? metrics.ItalicCorrection) * size,
                IsSingleCharacter = true
            };
            box.Items.Add(new MathGlyphDrawItem(glyphId, 0, 0, size));
            return box;
        }

        private void AddRun(MathRun run, Ctx ctx, int depth, List<Box> items)
        {
            switch (run.Kind)
            {
                case MathRunKind.Text:
                    AddText(run.Text, run.IsUpright ? ctx with { Upright = true } : ctx, items);
                    break;
                case MathRunKind.Preserved:
                    AddText(string.IsNullOrEmpty(run.Text) ? run.LinearText : run.Text, ctx, items);
                    break;
                case MathRunKind.Superscript:
                    items.Add(Scripts(run, ctx, depth, sub: false, sup: true));
                    break;
                case MathRunKind.Subscript:
                    items.Add(Scripts(run, ctx, depth, sub: true, sup: false));
                    break;
                case MathRunKind.SubSuperscript:
                    items.Add(Scripts(run, ctx, depth, sub: true, sup: true));
                    break;
                case MathRunKind.LowerLimit:
                    items.Add(Limit(run, ctx, depth, below: true));
                    break;
                case MathRunKind.UpperLimit:
                    items.Add(Limit(run, ctx, depth, below: false));
                    break;
                case MathRunKind.Fraction:
                    items.Add(Fraction(run, ctx, depth));
                    break;
                case MathRunKind.Radical:
                    items.Add(Radical(run, ctx, depth));
                    break;
                case MathRunKind.NAry:
                    AddNAry(run, ctx, depth, items);
                    break;
                case MathRunKind.Accent:
                    items.Add(Accent(run, ctx, depth));
                    break;
                case MathRunKind.Bar:
                    items.Add(Bar(run, ctx, depth));
                    break;
                case MathRunKind.GroupChar:
                    items.Add(GroupChar(run, ctx, depth));
                    break;
                case MathRunKind.Delimiter:
                    items.Add(Delimiter(run, ctx, depth));
                    break;
                case MathRunKind.Matrix:
                    items.Add(run.Matrix is null ? TextRow(run.LinearText, ctx) : Grid(run.Matrix, ctx, depth, equationArray: false));
                    break;
                case MathRunKind.EquationArray:
                    items.Add(run.Matrix is null ? TextRow(run.LinearText, ctx) : Grid(run.Matrix, ctx, depth, equationArray: true));
                    break;
                case MathRunKind.FunctionApply:
                    AddFunction(run, ctx, depth, items);
                    break;
                default:
                    AddText(run.LinearText, ctx, items);
                    break;
            }
        }

        private Box TextRow(string text, Ctx ctx)
        {
            var items = new List<Box>();
            AddText(text, ctx, items);
            return Join(items, ctx);
        }

        // ── scripts and limits ────────────────────────────────────────────────────────────────────

        private Box Scripts(MathRun run, Ctx ctx, int depth, bool sub, bool sup)
        {
            var baseBox = Slot(run.ScriptBaseEquation, run.Base, ctx, depth);
            var subBox = sub ? Slot(run.ScriptSubEquation, run.Sub, ctx.ScriptStyle(cramped: true), depth) : null;
            var supBox = sup ? Slot(run.ScriptSupEquation, run.Sup, ctx.ScriptStyle(cramped: false), depth) : null;
            return AttachScripts(baseBox, subBox, supBox, ctx, isLargeOperator: false);
        }

        private Box AttachScripts(Box baseBox, Box? subBox, Box? supBox, Ctx ctx, bool isLargeOperator)
        {
            var size = Size(ctx);
            var simple = baseBox.IsSingleCharacter && !isLargeOperator;
            double supShift = 0, subShift = 0;
            if (supBox is not null)
            {
                supShift = Math.Max(
                    (ctx.Cramped ? _k.SuperscriptShiftUpCramped : _k.SuperscriptShiftUp) * size,
                    supBox.Descent + (_k.SuperscriptBottomMin * size));
                if (!simple)
                    supShift = Math.Max(supShift, baseBox.Ascent - (_k.SuperscriptBaselineDropMax * size));
            }

            if (subBox is not null)
            {
                subShift = Math.Max(_k.SubscriptShiftDown * size, subBox.Ascent - (_k.SubscriptTopMax * size));
                if (!simple)
                    subShift = Math.Max(subShift, baseBox.Descent + (_k.SubscriptBaselineDropMin * size));
            }

            if (supBox is not null && subBox is not null)
            {
                var gap = (supShift - supBox.Descent) - (subBox.Ascent - subShift);
                var minGap = _k.SubSuperscriptGapMin * size;
                if (gap < minGap)
                {
                    subShift += minGap - gap;
                    var supBottom = supShift - supBox.Descent;
                    var maxBottom = _k.SuperscriptBottomMaxWithSubscript * size;
                    if (supBottom < maxBottom)
                    {
                        var delta = Math.Min(maxBottom - supBottom, subShift - Math.Max(_k.SubscriptShiftDown * size, subBox.Ascent - (_k.SubscriptTopMax * size)));
                        if (delta > 0)
                        {
                            supShift += delta;
                            subShift -= delta;
                        }
                    }
                }
            }

            var box = new Box();
            box.Add(baseBox, 0, 0);
            box.FirstClass = baseBox.FirstClass;
            box.LastClass = isLargeOperator ? MathAtomClass.Op : MathAtomClass.Ord;
            var right = baseBox.Width;
            if (supBox is not null)
            {
                var x = baseBox.Width + (isLargeOperator ? baseBox.Italic / 2 : baseBox.Italic);
                box.Add(supBox, x, supShift);
                right = Math.Max(right, x + supBox.Width);
            }

            if (subBox is not null)
            {
                var x = baseBox.Width - (isLargeOperator ? baseBox.Italic / 2 : 0);
                box.Add(subBox, x, -subShift);
                right = Math.Max(right, x + subBox.Width);
            }

            box.Width = right + (supBox is not null || subBox is not null ? _k.SpaceAfterScript * size : 0);
            return box;
        }

        /// <summary>m:limLow / m:limUpp: the limit centred below/above its base, in script size.</summary>
        private Box Limit(MathRun run, Ctx ctx, int depth, bool below)
        {
            var baseBox = Slot(run.ScriptBaseEquation, run.Base, ctx, depth);
            var limit = below
                ? Slot(run.ScriptSubEquation, run.Sub, ctx.ScriptStyle(cramped: true), depth)
                : Slot(run.ScriptSupEquation, run.Sup, ctx.ScriptStyle(cramped: false), depth);
            var box = StackLimits(baseBox, below ? null : limit, below ? limit : null, ctx, italic: 0);
            box.FirstClass = ctx.Upright ? MathAtomClass.Op : baseBox.FirstClass;
            box.LastClass = ctx.Upright ? MathAtomClass.Op : baseBox.LastClass;
            return box;
        }

        private Box StackLimits(Box baseBox, Box? upper, Box? lower, Ctx ctx, double italic)
        {
            var size = Size(ctx);
            var width = Math.Max(baseBox.Width, Math.Max(upper?.Width ?? 0, lower?.Width ?? 0));
            var box = new Box();
            box.Add(baseBox, (width - baseBox.Width) / 2, 0);
            if (upper is not null)
            {
                var rise = Math.Max(_k.UpperLimitBaselineRiseMin * size, baseBox.Ascent + (_k.UpperLimitGapMin * size) + upper.Descent);
                box.Add(upper, ((width - upper.Width) / 2) + (italic / 2), rise);
            }

            if (lower is not null)
            {
                var drop = Math.Max(_k.LowerLimitBaselineDropMin * size, baseBox.Descent + (_k.LowerLimitGapMin * size) + lower.Ascent);
                box.Add(lower, ((width - lower.Width) / 2) - (italic / 2), -drop);
            }

            box.Width = width;
            return box;
        }

        // ── fractions ────────────────────────────────────────────────────────────────────────────

        private Box Fraction(MathRun run, Ctx ctx, int depth)
        {
            var size = Size(ctx);
            if (run.FractionType == MathFractionType.Linear)
            {
                var parts = new List<Box>
                {
                    Slot(run.NumeratorEquation, run.Numerator, ctx.Plain(), depth),
                    TextAtom("/", MathAtomClass.Ord, ctx),
                    Slot(run.DenominatorEquation, run.Denominator, ctx.Plain(), depth)
                };
                return Join(parts, ctx);
            }

            // m:smallFrac sets display fractions with the reduced, inline (text-style) rules.
            var partCtx = settings.SmallFractions && ctx.Style == MathLayoutStyle.Display
                ? ctx with { Style = MathLayoutStyle.Text }
                : ctx;
            var display = partCtx.Style == MathLayoutStyle.Display;
            var numerator = Slot(run.NumeratorEquation, run.Numerator, partCtx.FractionPart(denominator: false), depth);
            var denominator = Slot(run.DenominatorEquation, run.Denominator, partCtx.FractionPart(denominator: true), depth);
            var axis = _k.AxisHeight * size;

            if (run.FractionType == MathFractionType.Skewed)
                return SkewedFraction(numerator, denominator, ctx);

            double numShift, denShift;
            var box = new Box();
            var overhang = 0.06 * size;
            var margin = 0.06 * size;
            var inner = Math.Max(numerator.Width, denominator.Width) + (2 * overhang);
            if (run.FractionType == MathFractionType.NoBar)
            {
                numShift = (display ? _k.StackTopDisplayStyleShiftUp : _k.StackTopShiftUp) * size;
                denShift = (display ? _k.StackBottomDisplayStyleShiftDown : _k.StackBottomShiftDown) * size;
                var gapMin = (display ? _k.StackDisplayStyleGapMin : _k.StackGapMin) * size;
                var gap = (numShift - numerator.Descent) - (denominator.Ascent - denShift);
                if (gap < gapMin)
                {
                    numShift += (gapMin - gap) / 2;
                    denShift += (gapMin - gap) / 2;
                }
            }
            else
            {
                var thickness = _k.FractionRuleThickness * size;
                numShift = Math.Max(
                    (display ? _k.FractionNumeratorDisplayStyleShiftUp : _k.FractionNumeratorShiftUp) * size,
                    axis + (thickness / 2) + ((display ? _k.FractionNumDisplayStyleGapMin : _k.FractionNumeratorGapMin) * size) + numerator.Descent);
                denShift = Math.Max(
                    (display ? _k.FractionDenominatorDisplayStyleShiftDown : _k.FractionDenominatorShiftDown) * size,
                    denominator.Ascent + ((display ? _k.FractionDenomDisplayStyleGapMin : _k.FractionDenominatorGapMin) * size) - axis + (thickness / 2));
                box.AddRule(margin, -(axis + (thickness / 2)), inner, thickness);
            }

            box.Add(numerator, margin + ((inner - numerator.Width) / 2), numShift);
            box.Add(denominator, margin + ((inner - denominator.Width) / 2), -denShift);
            box.Width = inner + (2 * margin);
            return box;
        }

        private Box SkewedFraction(Box numerator, Box denominator, Ctx ctx)
        {
            var size = Size(ctx);
            var axis = _k.AxisHeight * size;
            var vgap = _k.SkewedFractionVerticalGap * size;
            var numShift = axis + (vgap / 2) + numerator.Descent;
            var denShift = denominator.Ascent - axis + (vgap / 2);
            var slashHeight = (numShift + numerator.Ascent) + (denShift + denominator.Descent);
            var slash = Stretch('/', slashHeight, ctx, vertical: true);
            var box = new Box();
            box.Add(numerator, 0, numShift);
            var hgap = Math.Max(_k.SkewedFractionHorizontalGap * size, slash.Width * 0.8);
            var slashX = numerator.Width + ((hgap - slash.Width) / 2);
            box.Add(slash, slashX, axis - ((slash.Ascent - slash.Descent) / 2));
            box.Add(denominator, numerator.Width + hgap, -denShift);
            return box;
        }

        private Box TextAtom(string text, MathAtomClass atomClass, Ctx ctx)
        {
            var box = TextBox(text, ctx);
            box.Class = atomClass;
            return box;
        }

        // ── radicals ─────────────────────────────────────────────────────────────────────────────

        private Box Radical(MathRun run, Ctx ctx, int depth)
        {
            var size = Size(ctx);
            var radicand = Slot(run.RadicandEquation, run.Base, ctx.WithCramped().Plain(), depth);
            var hasDegree = run.DegreeEquation is not null
                ? run.DegreeEquation.Runs.Count > 0 && !string.IsNullOrEmpty(run.DegreeEquation.LinearText)
                : !string.IsNullOrEmpty(run.Degree);
            var thickness = _k.RadicalRuleThickness * size;
            var gap = (ctx.Style == MathLayoutStyle.Display ? _k.RadicalDisplayStyleVerticalGap : _k.RadicalVerticalGap) * size;
            var target = radicand.Height + gap + thickness;
            var surd = Stretch('√', target, ctx, vertical: true);
            if (surd.Height > target)
                gap += (surd.Height - target) / 2;

            var surdTop = radicand.Ascent + gap + thickness;
            var surdUp = surdTop - surd.Ascent;
            var box = new Box();
            double surdX = 0;
            if (hasDegree)
            {
                var degree = Slot(run.DegreeEquation, run.Degree, ctx.ScriptScriptStyle(), depth);
                var kernBefore = _k.RadicalKernBeforeDegree * size;
                var kernAfter = _k.RadicalKernAfterDegree * size;
                var raise = (surdUp - surd.Descent) + (_k.RadicalDegreeBottomRaisePercent / 100.0 * surd.Height);
                box.Add(degree, kernBefore, raise + degree.Descent);
                surdX = Math.Max(0, kernBefore + degree.Width + kernAfter);
            }

            box.Add(surd, surdX, surdUp);
            var radicandX = surdX + surd.Width;
            box.AddRule(radicandX, -surdTop, radicand.Width, thickness);
            box.Add(radicand, radicandX, 0);
            box.Ascent = Math.Max(box.Ascent, surdTop + (_k.RadicalExtraAscender * size));
            box.Width = radicandX + radicand.Width;
            return box;
        }

        // ── n-ary operators ──────────────────────────────────────────────────────────────────────

        private void AddNAry(MathRun run, Ctx ctx, int depth, List<Box> items)
        {
            var size = Size(ctx);
            var opText = string.IsNullOrEmpty(run.Operator) ? "∫" : run.Operator;
            var codePoint = opText.EnumerateRunes().First().Value;
            var display = ctx.Style == MathLayoutStyle.Display;
            var integral = MathText.IsIntegral(codePoint);

            Box op;
            if (display && opText.EnumerateRunes().Count() == 1)
            {
                // Display operators take the first size variant at least DisplayOperatorMinHeight tall. Word
                // sets display integrals noticeably taller (about the height of a display fraction), so they
                // ask for the next Cambria Math size up.
                var minHeight = _k.DisplayOperatorMinHeight * size * (integral ? DisplayIntegralHeightFactor : 1.0);
                op = Stretch(codePoint, minHeight, ctx, vertical: true, largeOperator: true);
            }
            else
            {
                op = TextBox(opText, ctx.Plain());
            }

            // Centre the operator on the math axis.
            var axis = _k.AxisHeight * size;
            var centred = new Box { Italic = op.Italic, IsSingleCharacter = true };
            centred.Add(op, 0, axis - ((op.Ascent - op.Descent) / 2));
            centred.Width = op.Width;

            var lower = HasSlot(run.NAryLowerLimitEquation, run.Sub)
                ? Slot(run.NAryLowerLimitEquation, run.Sub, ctx.ScriptStyle(cramped: true), depth)
                : null;
            var upper = HasSlot(run.NAryUpperLimitEquation, run.Sup)
                ? Slot(run.NAryUpperLimitEquation, run.Sup, ctx.ScriptStyle(cramped: false), depth)
                : null;

            Box opBox;
            if (lower is null && upper is null)
            {
                opBox = centred;
            }
            else if (display && (run.NAryLimitLocation ?? (integral ? settings.IntegralLimitLocation : settings.NAryLimitLocation)) == MathLimitLocation.UnderOver)
            {
                // Explicit m:limLoc, else the document's m:naryLim / m:intLim; inline operators always
                // take side limits, as Word compresses them.
                opBox = StackLimits(centred, upper, lower, ctx, centred.Italic);
            }
            else
            {
                opBox = AttachScripts(centred, lower, upper, ctx, isLargeOperator: true);
            }

            opBox.Class = MathAtomClass.Op;
            items.Add(opBox);

            var operand = Slot(run.NAryOperandEquation, run.Base, ctx.Plain(), depth);
            if (operand.Width > 0 || operand.Items.Count > 0)
                items.Add(operand);
        }

        private static bool HasSlot(Equation? equation, string fallback) =>
            equation is not null ? equation.Runs.Count > 0 && !string.IsNullOrEmpty(equation.LinearText) : !string.IsNullOrEmpty(fallback);

        // ── functions ────────────────────────────────────────────────────────────────────────────

        private void AddFunction(MathRun run, Ctx ctx, int depth, List<Box> items)
        {
            var nameCtx = ctx with { Upright = true };
            var name = Slot(run.FunctionNameEquation, run.FuncName, nameCtx, depth);
            if (name.Width > 0 || name.Items.Count > 0)
            {
                name.Class = MathAtomClass.Op;
                items.Add(name);
            }

            var argument = Slot(run.FunctionArgumentEquation, run.Base, ctx.Plain(), depth);
            if (argument.Width > 0 || argument.Items.Count > 0)
            {
                // Word always sets the function-apply thin space, even before a fence.
                if (argument.FirstClass == MathAtomClass.Open && items.Count > 0)
                    items.Add(new Box { Width = 3 / MuPerEm * Size(ctx), IsSpace = true });
                items.Add(argument);
            }
        }

        // ── accents, bars, group characters ──────────────────────────────────────────────────────

        private Box Accent(MathRun run, Ctx ctx, int depth)
        {
            var size = Size(ctx);
            var baseBox = Slot(run.DecoratorBaseEquation, run.Base, ctx.WithCramped().Plain(), depth);
            var accentText = AccentCharacter(run.Accent);
            var codePoint = accentText.EnumerateRunes().First().Value;

            Box accent;
            double inkCentre;
            double raise;
            var combining = codePoint is >= 0x0300 and <= 0x036F or >= 0x20D0 and <= 0x20FF;
            if (combining && font.TryGetGlyph(codePoint, out var glyph))
            {
                // OpenType placement: combining accents are drawn for a base of AccentBaseHeight and
                // move up only by how much taller the base is; wide bases take a stretched variant.
                var stretchy = !baseBox.IsSingleCharacter && font.GetVariants(glyph, vertical: false).Count > 0;
                accent = stretchy ? Stretch(codePoint, baseBox.Width, ctx, vertical: false) : GlyphBox(glyph, size);
                if (accent.Items is [MathGlyphDrawItem only])
                {
                    var metrics = font.MeasureGlyph(only.GlyphId);
                    inkCentre = (font.GetTopAccentAttachment(only.GlyphId) ?? ((metrics.InkLeft + metrics.InkRight) / 2)) * size;
                }
                else
                {
                    inkCentre = accent.Width / 2;
                }

                raise = Math.Max(0, baseBox.Ascent - (_k.AccentBaseHeight * size));
            }
            else
            {
                // A spacing accent (or no font glyph) sits just above the base's ink.
                accent = TextBox(accentText, ctx.Plain() with { Upright = true });
                inkCentre = accent.Width / 2;
                raise = baseBox.Ascent + (0.06 * size) + accent.Descent;
            }

            var attach = !double.IsNaN(baseBox.TopAccentX) && baseBox.IsSingleCharacter ? baseBox.TopAccentX : baseBox.Width / 2;

            var box = new Box();
            var accentX = attach - inkCentre;
            var shift = Math.Min(0, accentX);
            box.Add(baseBox, -shift, 0);
            box.Add(accent, accentX - shift, raise);
            box.Width = Math.Max(baseBox.Width - shift, box.Width);
            box.FirstClass = baseBox.FirstClass;
            box.LastClass = baseBox.LastClass;
            box.Italic = baseBox.Italic;
            return box;
        }

        private static string AccentCharacter(string accent) => accent switch
        {
            null or "" => "̂",
            "^" or "hat" => "̂",
            "~" or "tilde" => "̃",
            "¯" or "bar" => "̅",
            "." or "dot" => "̇",
            ".." or "ddot" => "̈",
            "→" or "vec" => "⃗",
            _ => accent
        };

        private Box Bar(MathRun run, Ctx ctx, int depth)
        {
            var size = Size(ctx);
            var baseBox = Slot(run.DecoratorBaseEquation, run.Base, run.BarTop ? ctx.WithCramped().Plain() : ctx.Plain(), depth);
            var box = new Box();
            box.Add(baseBox, 0, 0);
            if (run.BarTop)
            {
                var thickness = _k.OverbarRuleThickness * size;
                var top = baseBox.Ascent + (_k.OverbarVerticalGap * size) + thickness;
                box.AddRule(0, -top, baseBox.Width, thickness);
                box.Ascent = Math.Max(box.Ascent, top + (_k.OverbarExtraAscender * size));
            }
            else
            {
                var thickness = _k.UnderbarRuleThickness * size;
                var top = baseBox.Descent + (_k.UnderbarVerticalGap * size);
                box.AddRule(0, top, baseBox.Width, thickness);
                box.Descent = Math.Max(box.Descent, top + thickness + (_k.UnderbarExtraDescender * size));
            }

            box.Width = baseBox.Width;
            box.FirstClass = baseBox.FirstClass;
            box.LastClass = baseBox.LastClass;
            return box;
        }

        private Box GroupChar(MathRun run, Ctx ctx, int depth)
        {
            var size = Size(ctx);
            var top = !string.Equals(run.GroupChrPos, "bot", StringComparison.OrdinalIgnoreCase);
            var text = string.IsNullOrEmpty(run.GroupChr) ? "⏟" : run.GroupChr;

            // m:vertJc picks which element sits on the baseline; Word keeps the upper one there when the
            // document says nothing. When that is the character itself, the base hangs from it like a
            // limit, in script size (Word's look for an over-brace written without m:vertJc "bot").
            var justification = string.IsNullOrEmpty(run.GroupChrVerticalJustification) ? "top" : run.GroupChrVerticalJustification;
            var markOnBaseline = top == string.Equals(justification, "top", StringComparison.OrdinalIgnoreCase);
            if (markOnBaseline)
            {
                var limit = Slot(run.DecoratorBaseEquation, run.Base, ctx.ScriptStyle(cramped: top), depth);
                var stretched = Stretch(text.EnumerateRunes().First().Value, limit.Width, ctx, vertical: false);
                var limitBox = StackLimits(stretched, top ? null : limit, top ? limit : null, ctx, italic: 0);
                limitBox.Class = MathAtomClass.Op;
                return limitBox;
            }

            var baseBox = Slot(run.DecoratorBaseEquation, run.Base, ctx.Plain(), depth);
            var mark = Stretch(text.EnumerateRunes().First().Value, baseBox.Width, ctx, vertical: false);
            var width = Math.Max(baseBox.Width, mark.Width);
            var box = new Box();
            box.Add(baseBox, (width - baseBox.Width) / 2, 0);
            if (top)
                box.Add(mark, (width - mark.Width) / 2, baseBox.Ascent + (_k.StretchStackGapBelowMin * size) + mark.Descent);
            else
                box.Add(mark, (width - mark.Width) / 2, -(baseBox.Descent + (_k.StretchStackGapAboveMin * size) + mark.Ascent));
            box.Width = width;
            return box;
        }

        // ── delimiters ───────────────────────────────────────────────────────────────────────────

        private Box Delimiter(MathRun run, Ctx ctx, int depth)
        {
            var size = Size(ctx);
            var arguments = new List<Box> { Slot(run.DelimiterContentEquation, run.Base, ctx.Plain(), depth) };
            for (var i = 0; i < run.AdditionalDelimiterArguments.Count; i++)
            {
                var equation = i < run.AdditionalDelimiterContentEquations.Count ? run.AdditionalDelimiterContentEquations[i] : null;
                arguments.Add(Slot(equation, run.AdditionalDelimiterArguments[i], ctx.Plain(), depth));
            }

            var axis = _k.AxisHeight * size;
            var maxAscent = arguments.Max(argument => argument.Ascent);
            var maxDescent = arguments.Max(argument => argument.Descent);
            var extent = Math.Max(maxAscent - axis, maxDescent + axis);
            var target = 2 * extent * 0.95;

            // A bracketed matrix keeps a little air between its cells and the fences (a bare m:m has none).
            var matrixPadding = run.AdditionalDelimiterArguments.Count == 0
                && run.DelimiterContentEquation is { Runs: [{ Kind: MathRunKind.Matrix }] }
                    ? new Box { Width = 0.1 * size, IsSpace = true }
                    : null;

            var parts = new List<Box>();
            if (Fence(run.OpenChar, target, ctx, MathAtomClass.Open) is { } open)
                parts.Add(open);
            if (matrixPadding is not null)
                parts.Add(matrixPadding);
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i > 0 && Fence(run.DelimiterSeparator, target, ctx, null) is { } separator)
                    parts.Add(separator);
                parts.Add(arguments[i]);
            }

            if (matrixPadding is not null)
                parts.Add(matrixPadding);
            if (Fence(run.CloseChar, target, ctx, MathAtomClass.Close) is { } close)
                parts.Add(close);

            var row = Join(parts, ctx);
            row.Italic = 0;
            return row;
        }

        private Box? Fence(string? text, double target, Ctx ctx, MathAtomClass? atomClass)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var codePoint = MathText.NormalizeOperator(text.EnumerateRunes().First().Value);
            var characterClass = MathText.Classify(codePoint);
            var resolvedClass = atomClass ?? characterClass switch
            {
                MathAtomClass.Punct => MathAtomClass.Punct,
                MathAtomClass.Rel => MathAtomClass.Rel,
                _ => MathAtomClass.Ord
            };

            // Punctuation separators (",", ";") and multi-character fences are set as plain text.
            if (characterClass == MathAtomClass.Punct || text.EnumerateRunes().Count() != 1)
                return TextAtom(text, resolvedClass, ctx.Plain() with { Upright = true });

            // Fences grow with their content (Word's default m:grow) and are centred on the math axis.
            var size = Size(ctx);
            var glyph = Stretch(codePoint, target, ctx, vertical: true);
            var axis = _k.AxisHeight * size;
            var box = new Box();
            box.Add(glyph, 0, axis - ((glyph.Ascent - glyph.Descent) / 2));
            box.Width = glyph.Width;
            box.Class = resolvedClass;
            return box;
        }

        // ── matrices and equation arrays ─────────────────────────────────────────────────────────

        private Box Grid(MathMatrix matrix, Ctx ctx, int depth, bool equationArray)
        {
            var size = Size(ctx);
            var cellCtx = ctx.Plain();
            var rowCount = matrix.RowCount;
            var columnCount = matrix.ColumnCount;
            var cells = new Box[rowCount, columnCount];
            for (var r = 0; r < rowCount; r++)
                for (var c = 0; c < columnCount; c++)
                    cells[r, c] = Slot(matrix.CellEquationAt(r, c), matrix.CellTextAt(r, c), cellCtx, depth);

            var widths = new double[columnCount];
            for (var c = 0; c < columnCount; c++)
                for (var r = 0; r < rowCount; r++)
                    widths[c] = Math.Max(widths[c], cells[r, c].Width);

            var ascents = new double[rowCount];
            var descents = new double[rowCount];
            for (var r = 0; r < rowCount; r++)
            {
                // Every row is at least as tall as a plain line of digits, as in Word.
                ascents[r] = 0.65 * size;
                descents[r] = 0.2 * size;
                for (var c = 0; c < columnCount; c++)
                {
                    ascents[r] = Math.Max(ascents[r], cells[r, c].Ascent);
                    descents[r] = Math.Max(descents[r], cells[r, c].Descent);
                }
            }

            // Word's default matrix column gap is about one em; equation-array columns are alignment
            // pairs (right-aligned, then left-aligned) set without a gap.
            var columnGap = equationArray ? 0 : 1.0 * size;
            var rowGap = 0.25 * size;
            var baselines = new double[rowCount];
            double y = 0;
            for (var r = 0; r < rowCount; r++)
            {
                y = r == 0 ? ascents[0] : y + descents[r - 1] + rowGap + ascents[r];
                baselines[r] = y;
            }

            var totalHeight = rowCount == 0 ? 0 : baselines[^1] + descents[^1];
            var axis = _k.AxisHeight * size;
            var box = new Box();
            for (var r = 0; r < rowCount; r++)
            {
                double x = 0;
                for (var c = 0; c < columnCount; c++)
                {
                    var cell = cells[r, c];
                    double cellX;
                    if (equationArray && columnCount > 1)
                        cellX = c % 2 == 0 ? x + widths[c] - cell.Width : x;
                    else
                        cellX = x + ((widths[c] - cell.Width) / 2);
                    box.Add(cell, cellX, (totalHeight / 2) + axis - baselines[r]);
                    x += widths[c] + (c < columnCount - 1 ? columnGap : 0);
                }

                box.Width = Math.Max(box.Width, x);
            }

            box.Ascent = Math.Max(box.Ascent, (totalHeight / 2) + axis);
            box.Descent = Math.Max(box.Descent, (totalHeight / 2) - axis);
            return box;
        }

        // ── stretchy glyphs ──────────────────────────────────────────────────────────────────────

        private Box Stretch(int codePoint, double target, Ctx ctx, bool vertical, bool largeOperator = false)
        {
            var size = Size(ctx);
            codePoint = MathText.NormalizeOperator(codePoint);
            if (!font.TryGetGlyph(codePoint, out var glyph))
                return ScaledTextFallback(codePoint, target, ctx, vertical, largeOperator);

            var variants = font.GetVariants(glyph, vertical);
            var baseBox = GlyphBox(glyph, size);
            var natural = vertical ? baseBox.Height : baseBox.Width;
            if (variants.Count == 0)
                return baseBox;

            foreach (var variant in variants)
            {
                if (variant.AdvanceMeasurement * size >= target || (!largeOperator && variant.GlyphId == glyph && natural >= target))
                    return GlyphBox(variant.GlyphId, size);
            }

            if (font.GetAssembly(glyph, vertical) is { Parts.Count: > 0 } assembly)
                return Assemble(assembly, target, size, vertical);

            return GlyphBox(variants[^1].GlyphId, size);
        }

        private Box ScaledTextFallback(int codePoint, double target, Ctx ctx, bool vertical, bool largeOperator)
        {
            var text = char.ConvertFromUtf32(codePoint);
            var natural = TextBox(text, ctx.Plain() with { Upright = true });
            var extent = vertical ? natural.Height : natural.Width;
            if (extent <= 0 || extent >= target || (!vertical && !largeOperator))
                return natural;

            var scale = Math.Min(target / extent, 4.0);
            var size = Size(ctx) * scale;
            var metrics = font.MeasureText(text);
            var box = new Box
            {
                Width = metrics.Width * size,
                Ascent = metrics.Ascent * size,
                Descent = metrics.Descent * size,
                IsSingleCharacter = true
            };
            box.Items.Add(new MathTextDrawItem(text, 0, 0, size));
            return box;
        }

        /// <summary>Builds an extensible glyph assembly (OpenType algorithm: repeat extenders, share overlap).</summary>
        private Box Assemble(MathGlyphAssembly assembly, double target, double size, bool vertical)
        {
            var minOverlap = font.MinConnectorOverlap * size;
            List<MathGlyphPart> sequence = [];
            double overlap = 0;
            for (var repeats = 0; repeats < 64; repeats++)
            {
                sequence = [];
                foreach (var part in assembly.Parts)
                {
                    if (part.IsExtender)
                    {
                        for (var i = 0; i < repeats; i++)
                            sequence.Add(part);
                    }
                    else
                    {
                        sequence.Add(part);
                    }
                }

                if (sequence.Count == 0)
                    continue;

                var total = sequence.Sum(part => part.FullAdvance) * size;
                var maxOverlap = double.MaxValue;
                for (var i = 0; i + 1 < sequence.Count; i++)
                    maxOverlap = Math.Min(maxOverlap, Math.Min(sequence[i].EndConnectorLength, sequence[i + 1].StartConnectorLength) * size);
                if (sequence.Count == 1)
                    maxOverlap = 0;
                maxOverlap = Math.Max(maxOverlap, minOverlap);

                var longest = total - ((sequence.Count - 1) * minOverlap);
                if (longest >= target || !assembly.Parts.Any(part => part.IsExtender))
                {
                    overlap = sequence.Count > 1 ? Math.Clamp((total - target) / (sequence.Count - 1), minOverlap, maxOverlap) : 0;
                    break;
                }
            }

            var box = new Box { IsSingleCharacter = true, Italic = assembly.ItalicCorrection * size };
            double position = 0;
            foreach (var part in sequence)
            {
                var metrics = font.MeasureGlyph(part.GlyphId);
                if (vertical)
                {
                    // Parts run bottom to top: the part's ink bottom sits at the running position.
                    var piece = GlyphBox(part.GlyphId, size);
                    box.Add(piece, 0, position + (metrics.Descent * size));
                }
                else
                {
                    box.Add(GlyphBox(part.GlyphId, size), position, 0);
                }

                position += (part.FullAdvance * size) - overlap;
            }

            position += overlap;
            if (vertical)
            {
                box.Descent = Math.Max(box.Descent, 0);
                box.Ascent = Math.Max(box.Ascent, position);
            }
            else
            {
                box.Width = Math.Max(box.Width, position);
            }

            return box;
        }
    }
}
