using System;
using System.Collections.Generic;

namespace FreeP.App.Compositor.MathLayout;

// ── Math layout engine (Theme 27) ────────────────────────────────────────────
//
// Converts a MathNode tree into a MathBox tree of positioned primitives.
// All sizing is approximated from the base font size; no font-metric calls are
// made so the engine is framework-free.
//
// Standard math typesetting approximations used:
//   • em = fontSizePt * (96/72)  (DIP)
//   • Math axis (center of operators/fractions) at 0.45 em above baseline
//   • Fraction bar on the math axis; num/den stacked x0.1 em gap from bar
//   • Script size = 0.70 x parent size, shift-up (sup) = 0.40 em, shift-down (sub) = 0.25 em
//   • Radical sign width x 0.65 em; overline clearance = 0.10 em
//   • N-ary operator enlarged to 1.5 x base size; limits at 0.70 x base
//   • Delimiter brackets scale to content height * 1.10

public static class MathLayoutEngine
{
    // ── Public entry ──────────────────────────────────────────────────────

    /// <summary>
    /// Lay out <paramref name="node"/> at the given base font size and return
    /// a <see cref="MathBox.Container"/> with all children positioned.
    /// The container's (X,Y) is always (0,0); the caller translates it to
    /// the desired slide position.
    /// </summary>
    public static MathBox.Container Layout(MathNode node, string fontFamily, double fontSizePt)
    {
        var box = LayoutNode(node, fontFamily, fontSizePt);
        var root = new MathBox.Container();
        root.Children.Add(box);
        box.X = 0; box.Y = 0;
        root.Metrics.Width  = box.Metrics.Width;
        root.Metrics.Height = box.Metrics.Height;
        root.Metrics.Ascent = box.Metrics.Ascent;
        return root;
    }

    // ── Node dispatcher ───────────────────────────────────────────────────

    private static MathBox LayoutNode(MathNode node, string fontFamily, double fontSizePt)
    {
        return node switch
        {
            MathNode.Run     r  => LayoutRun(r, fontFamily, fontSizePt),
            MathNode.Frac    f  => LayoutFrac(f, fontFamily, fontSizePt),
            MathNode.Sup     s  => LayoutSup(s, fontFamily, fontSizePt),
            MathNode.Sub     s  => LayoutSub(s, fontFamily, fontSizePt),
            MathNode.SubSup  ss => LayoutSubSup(ss, fontFamily, fontSizePt),
            MathNode.Rad     r  => LayoutRad(r, fontFamily, fontSizePt),
            MathNode.Nary    n  => LayoutNary(n, fontFamily, fontSizePt),
            MathNode.Func    fn => LayoutFunc(fn, fontFamily, fontSizePt),
            MathNode.Delim   d  => LayoutDelim(d, fontFamily, fontSizePt),
            MathNode.Acc     a  => LayoutAcc(a, fontFamily, fontSizePt),
            MathNode.Bar     b  => LayoutBar(b, fontFamily, fontSizePt),
            MathNode.GroupChr g => LayoutGroupChr(g, fontFamily, fontSizePt),
            MathNode.Matrix  m  => LayoutMatrix(m, fontFamily, fontSizePt),
            MathNode.Row     rw => LayoutRow(rw.Children, fontFamily, fontSizePt),
            MathNode.Unknown u  => LayoutFallback(u.FallbackText, fontFamily, fontSizePt),
            _                   => LayoutFallback("?", fontFamily, fontSizePt)
        };
    }

    // ── Em conversion ────────────────────────────────────────────────────

    private static double Em(double fontSizePt) => fontSizePt * (96.0 / 72.0);

    // ── Run layout ────────────────────────────────────────────────────────

    private static MathBox LayoutRun(MathNode.Run run, string fontFamily, double fontSizePt)
    {
        return MakeGlyph(run.Text, fontFamily, fontSizePt, run.IsItalic);
    }

    // ── Fallback (unknown) ────────────────────────────────────────────────

    private static MathBox LayoutFallback(string text, string fontFamily, double fontSizePt)
    {
        return MakeGlyph(text, fontFamily, fontSizePt, isItalic: false);
    }

    // ── Fraction layout ───────────────────────────────────────────────────

    private static MathBox LayoutFrac(MathNode.Frac frac, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double childSizePt = fontSizePt * 0.85; // numerator/denominator slightly smaller

        var numBox = LayoutNode(frac.Numerator, fontFamily, childSizePt);
        var denBox = LayoutNode(frac.Denominator, fontFamily, childSizePt);

        // Fraction bar is on the math axis = 0.45 em above baseline
        double mathAxisAboveBaseline = em * 0.45;
        double barThickness = em * 0.07;
        double gap = em * 0.10; // gap between bar and num/den

        // Position numerator so its bottom sits gap above the bar
        // Bar center = mathAxisAboveBaseline above baseline
        // We measure from the TOP of the whole expression.
        // ascent of the whole frac = num.Height + gap + barThickness/2 + mathAxisAboveBaseline
        // but we'll compute layout as:
        //   numY = 0
        //   barY = numBox.Metrics.Height + gap
        //   denY = barY + barThickness + gap
        //   total height = denY + denBox.Metrics.Height
        //   baseline position from top = barY + barThickness / 2 + mathAxisAboveBaseline

        double barY  = numBox.Metrics.Height + gap;
        double denY  = barY + barThickness + gap;
        double totalH = denY + denBox.Metrics.Height;

        double totalW = Math.Max(numBox.Metrics.Width, denBox.Metrics.Width) + em * 0.10;

        // Center num and den horizontally
        double numX  = (totalW - numBox.Metrics.Width) / 2.0;
        double denX  = (totalW - denBox.Metrics.Width) / 2.0;

        // Baseline: the math axis of the fraction sits at the fraction bar center
        double ascent = barY + barThickness / 2.0 + mathAxisAboveBaseline;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        // Numerator
        numBox.X = numX; numBox.Y = 0;
        container.Children.Add(numBox);

        // Fraction bar
        var bar = new MathBox.HRule
        {
            X = 0, Y = barY,
            LineWidth = totalW,
            Thickness = barThickness
        };
        bar.Metrics.Width  = totalW;
        bar.Metrics.Height = barThickness;
        bar.Metrics.Ascent = 0;
        container.Children.Add(bar);

        // Denominator
        denBox.X = denX; denBox.Y = denY;
        container.Children.Add(denBox);

        return container;
    }

    // ── Superscript layout ────────────────────────────────────────────────

    private static MathBox LayoutSup(MathNode.Sup sup, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double scriptSizePt = fontSizePt * 0.70;

        var baseBox   = LayoutNode(sup.Base, fontFamily, fontSizePt);
        var scriptBox = LayoutNode(sup.Script, fontFamily, scriptSizePt);

        // Superscript raised: top of script = baseline - shiftUp
        // Shift up = 0.40 em
        double shiftUp = em * 0.40;
        double baselineFromTop = baseBox.Metrics.Ascent;
        // Script top sits at (baselineFromTop - shiftUp - scriptBox.Metrics.Ascent)
        // → script Y = baselineFromTop - shiftUp - scriptBox.Metrics.Ascent
        double scriptY = baselineFromTop - shiftUp - scriptBox.Metrics.Ascent;
        if (scriptY < 0) scriptY = 0;

        double totalH = Math.Max(baseBox.Metrics.Height, scriptY + scriptBox.Metrics.Height);
        double totalW = baseBox.Metrics.Width + scriptBox.Metrics.Width + em * 0.03;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseBox.Metrics.Ascent;

        baseBox.X = 0; baseBox.Y = 0;
        scriptBox.X = baseBox.Metrics.Width + em * 0.03;
        scriptBox.Y = scriptY;

        container.Children.Add(baseBox);
        container.Children.Add(scriptBox);
        return container;
    }

    // ── Subscript layout ──────────────────────────────────────────────────

    private static MathBox LayoutSub(MathNode.Sub sub, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double scriptSizePt = fontSizePt * 0.70;

        var baseBox   = LayoutNode(sub.Base, fontFamily, fontSizePt);
        var scriptBox = LayoutNode(sub.Script, fontFamily, scriptSizePt);

        // Subscript lowered: top of script at baseline + shiftDown
        double shiftDown = em * 0.25;
        double scriptY = baseBox.Metrics.Ascent + shiftDown;

        double totalH = Math.Max(baseBox.Metrics.Height, scriptY + scriptBox.Metrics.Height);
        double totalW = baseBox.Metrics.Width + scriptBox.Metrics.Width + em * 0.03;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseBox.Metrics.Ascent;

        baseBox.X = 0; baseBox.Y = 0;
        scriptBox.X = baseBox.Metrics.Width + em * 0.03;
        scriptBox.Y = scriptY;

        container.Children.Add(baseBox);
        container.Children.Add(scriptBox);
        return container;
    }

    // ── Sub+Sup layout ────────────────────────────────────────────────────

    private static MathBox LayoutSubSup(MathNode.SubSup ss, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double scriptSizePt = fontSizePt * 0.70;

        var baseBox = LayoutNode(ss.Base, fontFamily, fontSizePt);
        var subBox  = LayoutNode(ss.Sub,  fontFamily, scriptSizePt);
        var supBox  = LayoutNode(ss.Sup,  fontFamily, scriptSizePt);

        double baseline = baseBox.Metrics.Ascent;

        // Sup: top at baseline - 0.40em - supBox.Ascent
        double supY = baseline - em * 0.40 - supBox.Metrics.Ascent;
        if (supY < 0) supY = 0;

        // Sub: top at baseline + 0.25em
        double subY = baseline + em * 0.25;

        double scriptX = baseBox.Metrics.Width + em * 0.03;
        double scriptW = Math.Max(subBox.Metrics.Width, supBox.Metrics.Width);

        double totalH = Math.Max(baseBox.Metrics.Height, Math.Max(
            supY + supBox.Metrics.Height,
            subY + subBox.Metrics.Height));
        double totalW = scriptX + scriptW;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseline;

        baseBox.X = 0; baseBox.Y = 0;
        supBox.X = scriptX; supBox.Y = supY;
        subBox.X = scriptX; subBox.Y = subY;

        container.Children.Add(baseBox);
        container.Children.Add(supBox);
        container.Children.Add(subBox);
        return container;
    }

    // ── Radical layout ────────────────────────────────────────────────────

    private static MathBox LayoutRad(MathNode.Rad rad, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);

        var radicand = LayoutNode(rad.Radicand, fontFamily, fontSizePt);

        double overlineClearance = em * 0.10;
        double overlineThick = em * 0.07;
        double signWidth = em * 0.65;

        // Degree index (optional)
        MathBox? degBox = null;
        if (rad.Degree is not null)
        {
            degBox = LayoutNode(rad.Degree, fontFamily, fontSizePt * 0.65);
        }

        // The radical sign height = radicand height + clearance + overline
        double radHeight = radicand.Metrics.Height + overlineClearance + overlineThick;

        // Degree position: above the radical sign's check-mark
        double degWidth = 0;
        if (degBox is not null)
        {
            degWidth = degBox.Metrics.Width + em * 0.03;
        }

        double radX = degWidth;
        double radicandX = radX + signWidth;
        double overlineWidth = radicand.Metrics.Width;

        // Radicand Y: below the overline (clearance space)
        double radicandY = overlineThick + overlineClearance;

        double totalW = radicandX + radicand.Metrics.Width;
        double totalH = radHeight;

        // baseline: the radicand's baseline shifted down by the overline area
        double ascent = radicandY + radicand.Metrics.Ascent;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        // Radical sign box
        var radSign = new MathBox.Radical
        {
            X = radX, Y = 0,
            OverlineWidth  = overlineWidth,
            OverlineThick  = overlineThick,
            SignWidth      = signWidth
        };
        radSign.Metrics.Width  = signWidth + overlineWidth;
        radSign.Metrics.Height = radHeight;
        radSign.Metrics.Ascent = overlineThick;
        container.Children.Add(radSign);

        // Overline rendered via the radical box (renderer draws the overline at Y=0)

        // Radicand
        radicand.X = radicandX; radicand.Y = radicandY;
        container.Children.Add(radicand);

        // Degree index
        if (degBox is not null)
        {
            // Position above the check-mark
            degBox.X = 0;
            degBox.Y = Math.Max(0, radHeight * 0.05);
            container.Children.Add(degBox);
        }

        return container;
    }

    // ── N-ary (? ? ?) layout ─────────────────────────────────────────────

    private static MathBox LayoutNary(MathNode.Nary nary, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double opSizePt   = fontSizePt * 1.50;  // enlarged operator
        double limSizePt  = fontSizePt * 0.65;  // limit labels

        var opBox = MakeGlyph(nary.OperatorChar, fontFamily, opSizePt, isItalic: false);
        MathBox? subLimBox = nary.SubLimit is not null
            ? LayoutNode(nary.SubLimit, fontFamily, limSizePt) : null;
        MathBox? supLimBox = nary.SupLimit is not null
            ? LayoutNode(nary.SupLimit, fontFamily, limSizePt) : null;
        var operandBox = LayoutNode(nary.Operand, fontFamily, fontSizePt);

        double opColW = opBox.Metrics.Width;
        if (subLimBox is not null) opColW = Math.Max(opColW, subLimBox.Metrics.Width);
        if (supLimBox is not null) opColW = Math.Max(opColW, supLimBox.Metrics.Width);
        opColW += em * 0.05;

        double limGap = em * 0.06;

        if (nary.LimitsAboveBelow)
        {
            // Stack: sup | op | sub, then operand to the right
            double supH = supLimBox is not null ? supLimBox.Metrics.Height + limGap : 0;
            double subH = subLimBox is not null ? subLimBox.Metrics.Height + limGap : 0;

            double opY  = supH;
            double subY = supH + opBox.Metrics.Height + limGap;

            double totalH = supH + opBox.Metrics.Height + subH;
            double totalW = opColW + em * 0.06 + operandBox.Metrics.Width;

            // Math baseline: align operand's baseline with the op's baseline
            double opBaseline = opY + opBox.Metrics.Ascent;
            double ascent = Math.Max(opBaseline, supH + opBox.Metrics.Ascent);

            var c = new MathBox.Container();
            c.Metrics.Width  = totalW;
            c.Metrics.Height = totalH;
            c.Metrics.Ascent = opBaseline;

            // Sup limit
            if (supLimBox is not null)
            {
                supLimBox.X = (opColW - supLimBox.Metrics.Width) / 2; supLimBox.Y = 0;
                c.Children.Add(supLimBox);
            }

            // Operator
            opBox.X = (opColW - opBox.Metrics.Width) / 2; opBox.Y = opY;
            c.Children.Add(opBox);

            // Sub limit
            if (subLimBox is not null)
            {
                subLimBox.X = (opColW - subLimBox.Metrics.Width) / 2; subLimBox.Y = subY;
                c.Children.Add(subLimBox);
            }

            // Operand: vertically align its baseline with the operator's baseline
            double operandY = opBaseline - operandBox.Metrics.Ascent;
            operandBox.X = opColW + em * 0.06; operandBox.Y = operandY;
            c.Children.Add(operandBox);

            return c;
        }
        else
        {
            // Integral style: sub/sup as scripts to the right of the operator
            // Same as SubSup but with the enlarged operator as base
            MathNode fakeBase    = new MathNode.Unknown(nary.OperatorChar);
            MathNode? fakeSub    = nary.SubLimit;
            MathNode? fakeSup    = nary.SupLimit;

            // Build manually using already-laid-out boxes
            double baseline = opBox.Metrics.Ascent;
            double supY = 0;
            double subY = opBox.Metrics.Height - (subLimBox?.Metrics.Height ?? 0);

            double scriptX = opBox.Metrics.Width + em * 0.02;
            double scriptW = Math.Max(subLimBox?.Metrics.Width ?? 0, supLimBox?.Metrics.Width ?? 0);

            double colH = opBox.Metrics.Height;
            double totalW = scriptX + scriptW + em * 0.06 + operandBox.Metrics.Width;
            double totalH = Math.Max(colH, Math.Max(
                supLimBox is not null ? supY + supLimBox.Metrics.Height : 0,
                subLimBox is not null ? subY + subLimBox.Metrics.Height : 0));

            var c = new MathBox.Container();
            c.Metrics.Width  = totalW;
            c.Metrics.Height = totalH;
            c.Metrics.Ascent = baseline;

            opBox.X = 0; opBox.Y = 0;
            c.Children.Add(opBox);

            if (supLimBox is not null)
            {
                supLimBox.X = scriptX; supLimBox.Y = supY;
                c.Children.Add(supLimBox);
            }
            if (subLimBox is not null)
            {
                subLimBox.X = scriptX; subLimBox.Y = subY;
                c.Children.Add(subLimBox);
            }

            double operandY = baseline - operandBox.Metrics.Ascent;
            operandBox.X = scriptX + scriptW + em * 0.06; operandBox.Y = operandY;
            c.Children.Add(operandBox);

            return c;
        }
    }

    // ── Function layout ───────────────────────────────────────────────────

    private static MathBox LayoutFunc(MathNode.Func func, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        var nameBox = LayoutNode(func.FunctionName, fontFamily, fontSizePt);
        var argBox  = LayoutNode(func.Argument, fontFamily, fontSizePt);

        double gap = em * 0.08;
        double totalW = nameBox.Metrics.Width + gap + argBox.Metrics.Width;
        double totalH = Math.Max(nameBox.Metrics.Height, argBox.Metrics.Height);
        double ascent  = Math.Max(nameBox.Metrics.Ascent, argBox.Metrics.Ascent);

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        // Align baselines
        nameBox.X = 0;
        nameBox.Y = ascent - nameBox.Metrics.Ascent;
        argBox.X  = nameBox.Metrics.Width + gap;
        argBox.Y  = ascent - argBox.Metrics.Ascent;

        c.Children.Add(nameBox);
        c.Children.Add(argBox);
        return c;
    }

    // ── Delimiter layout ──────────────────────────────────────────────────

    private static MathBox LayoutDelim(MathNode.Delim delim, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);

        // Layout inner elements separated by small comma gap
        double commaGap = em * 0.15;
        var innerBoxes = new List<MathBox>();
        foreach (var el in delim.Elements)
            innerBoxes.Add(LayoutNode(el, fontFamily, fontSizePt));

        // Compute inner dimensions
        double innerW = 0, innerH = 0, innerAscent = 0;
        foreach (var b in innerBoxes)
        {
            innerW += b.Metrics.Width;
            innerH = Math.Max(innerH, b.Metrics.Height);
            innerAscent = Math.Max(innerAscent, b.Metrics.Ascent);
        }
        if (innerBoxes.Count > 1)
            innerW += commaGap * (innerBoxes.Count - 1);

        // Brackets scale to inner height * 1.10
        double bracketH = innerH * 1.10;
        double bracketW = em * 0.35; // fixed bracket width

        double totalW = bracketW + innerW + bracketW;
        double totalH = bracketH;
        double ascent = innerAscent + (bracketH - innerH) / 2.0;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        // Opening bracket
        if (!string.IsNullOrEmpty(delim.BegChar))
        {
            var beg = new MathBox.Bracket(delim.BegChar) { X = 0, Y = 0, ScaledHeight = bracketH };
            beg.Metrics.Width = bracketW; beg.Metrics.Height = bracketH; beg.Metrics.Ascent = ascent;
            c.Children.Add(beg);
        }

        // Inner elements
        double x = bracketW;
        double innerTop = (bracketH - innerH) / 2.0;
        for (int i = 0; i < innerBoxes.Count; i++)
        {
            var b = innerBoxes[i];
            b.X = x;
            b.Y = innerTop + (innerAscent - b.Metrics.Ascent);
            c.Children.Add(b);
            x += b.Metrics.Width;

            // Comma separator between elements
            if (i < innerBoxes.Count - 1)
            {
                var comma = MakeGlyph(",", fontFamily, fontSizePt, isItalic: false);
                comma.X = x; comma.Y = innerTop + (innerAscent - comma.Metrics.Ascent);
                c.Children.Add(comma);
                x += commaGap;
            }
        }

        // Closing bracket
        if (!string.IsNullOrEmpty(delim.EndChar))
        {
            var end = new MathBox.Bracket(delim.EndChar) { X = x, Y = 0, ScaledHeight = bracketH };
            end.Metrics.Width = bracketW; end.Metrics.Height = bracketH; end.Metrics.Ascent = ascent;
            c.Children.Add(end);
        }

        return c;
    }

    // ── Accent layout ─────────────────────────────────────────────────────

    private static MathBox LayoutAcc(MathNode.Acc acc, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        var baseBox = LayoutNode(acc.Base, fontFamily, fontSizePt);

        // Accent placed above: use HRule-style overline for bar (̄) or a glyph for others
        double accentH = em * 0.25;
        double gap = em * 0.05;
        double totalH = accentH + gap + baseBox.Metrics.Height;
        double totalW = baseBox.Metrics.Width;
        double ascent = accentH + gap + baseBox.Metrics.Ascent;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        // Draw the accent glyph above the base
        var accentGlyph = MakeGlyph(acc.AccentChar, fontFamily, fontSizePt * 0.75, isItalic: false);
        accentGlyph.X = (totalW - accentGlyph.Metrics.Width) / 2;
        accentGlyph.Y = 0;
        c.Children.Add(accentGlyph);

        baseBox.X = 0; baseBox.Y = accentH + gap;
        c.Children.Add(baseBox);

        return c;
    }

    // ── Bar layout ────────────────────────────────────────────────────────

    private static MathBox LayoutBar(MathNode.Bar bar, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        var baseBox = LayoutNode(bar.Base, fontFamily, fontSizePt);

        double barThick = em * 0.07;
        double gap = em * 0.05;

        double totalW = baseBox.Metrics.Width;
        double totalH = bar.IsOver
            ? barThick + gap + baseBox.Metrics.Height
            : baseBox.Metrics.Height + gap + barThick;

        double ascent = bar.IsOver
            ? barThick + gap + baseBox.Metrics.Ascent
            : baseBox.Metrics.Ascent;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        if (bar.IsOver)
        {
            // Overline at top
            var hrule = new MathBox.HRule { X = 0, Y = 0, LineWidth = totalW, Thickness = barThick };
            hrule.Metrics.Width = totalW; hrule.Metrics.Height = barThick; hrule.Metrics.Ascent = 0;
            c.Children.Add(hrule);
            baseBox.X = 0; baseBox.Y = barThick + gap;
        }
        else
        {
            baseBox.X = 0; baseBox.Y = 0;
            var hrule = new MathBox.HRule { X = 0, Y = baseBox.Metrics.Height + gap, LineWidth = totalW, Thickness = barThick };
            hrule.Metrics.Width = totalW; hrule.Metrics.Height = barThick; hrule.Metrics.Ascent = 0;
            c.Children.Add(hrule);
        }
        c.Children.Add(baseBox);

        return c;
    }

    // ── GroupChr layout ───────────────────────────────────────────────────

    private static MathBox LayoutGroupChr(MathNode.GroupChr gc, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        var baseBox = LayoutNode(gc.Base, fontFamily, fontSizePt);

        // Group char placed above or below
        double grpH = em * 0.25;
        double gap  = em * 0.05;

        var grpGlyph = MakeGlyph(gc.GrpChar, fontFamily, fontSizePt * 0.75, isItalic: false);

        double totalW = Math.Max(baseBox.Metrics.Width, grpGlyph.Metrics.Width);
        double totalH = baseBox.Metrics.Height + gap + grpGlyph.Metrics.Height;
        double ascent;

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;

        if (gc.IsAbove)
        {
            ascent = grpGlyph.Metrics.Height + gap + baseBox.Metrics.Ascent;
            grpGlyph.X = (totalW - grpGlyph.Metrics.Width) / 2; grpGlyph.Y = 0;
            baseBox.X  = (totalW - baseBox.Metrics.Width)  / 2; baseBox.Y  = grpGlyph.Metrics.Height + gap;
        }
        else
        {
            ascent = baseBox.Metrics.Ascent;
            baseBox.X  = (totalW - baseBox.Metrics.Width)  / 2; baseBox.Y  = 0;
            grpGlyph.X = (totalW - grpGlyph.Metrics.Width) / 2; grpGlyph.Y = baseBox.Metrics.Height + gap;
        }

        c.Metrics.Ascent = ascent;
        c.Children.Add(grpGlyph);
        c.Children.Add(baseBox);
        return c;
    }

    // ── Matrix layout ─────────────────────────────────────────────────────

    private static MathBox LayoutMatrix(MathNode.Matrix matrix, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double cellGapH = em * 0.25;
        double cellGapV = em * 0.20;

        if (matrix.Rows.Count == 0)
            return MakeGlyph("", fontFamily, fontSizePt, false);

        int rowCount = matrix.Rows.Count;
        int colCount = matrix.Rows[0].Count;

        // Layout all cells
        var cells = new MathBox[rowCount][];
        for (int r = 0; r < rowCount; r++)
        {
            cells[r] = new MathBox[colCount];
            for (int c = 0; c < colCount && c < matrix.Rows[r].Count; c++)
                cells[r][c] = LayoutNode(matrix.Rows[r][c], fontFamily, fontSizePt);
        }

        // Per-column width
        var colW = new double[colCount];
        for (int c = 0; c < colCount; c++)
            for (int r = 0; r < rowCount; r++)
                if (cells[r][c] is not null)
                    colW[c] = Math.Max(colW[c], cells[r][c].Metrics.Width);

        // Per-row ascent and descent
        var rowAsc = new double[rowCount];
        var rowDsc = new double[rowCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                if (cells[r][c] is not null)
                {
                    rowAsc[r] = Math.Max(rowAsc[r], cells[r][c].Metrics.Ascent);
                    rowDsc[r] = Math.Max(rowDsc[r], cells[r][c].Metrics.Descent);
                }

        double totalW = 0;
        for (int c = 0; c < colCount; c++) totalW += colW[c];
        totalW += cellGapH * Math.Max(0, colCount - 1);

        double totalH = 0;
        for (int r = 0; r < rowCount; r++) totalH += rowAsc[r] + rowDsc[r];
        totalH += cellGapV * Math.Max(0, rowCount - 1);

        // Baseline of the matrix = center of the matrix on the math axis
        double ascent = totalH / 2.0 + em * 0.45;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        double rowY = 0;
        for (int r = 0; r < rowCount; r++)
        {
            double colX = 0;
            for (int c = 0; c < colCount; c++)
            {
                var cell = cells[r][c];
                if (cell is not null)
                {
                    cell.X = colX + (colW[c] - cell.Metrics.Width) / 2;
                    cell.Y = rowY + rowAsc[r] - cell.Metrics.Ascent;
                    container.Children.Add(cell);
                }
                colX += colW[c] + cellGapH;
            }
            rowY += rowAsc[r] + rowDsc[r] + cellGapV;
        }

        return container;
    }

    // ── Row layout (horizontal sequence) ──────────────────────────────────

    private static MathBox LayoutRow(IReadOnlyList<MathNode> nodes, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);

        if (nodes.Count == 0)
            return MakeGlyph("", fontFamily, fontSizePt, false);

        var boxes = new List<MathBox>(nodes.Count);
        foreach (var node in nodes)
            boxes.Add(LayoutNode(node, fontFamily, fontSizePt));

        // Align all on a common baseline (max ascent)
        double ascent = 0;
        foreach (var b in boxes) ascent = Math.Max(ascent, b.Metrics.Ascent);

        double totalW = 0, totalH = 0;
        foreach (var b in boxes)
        {
            totalW += b.Metrics.Width;
            totalH = Math.Max(totalH, (ascent - b.Metrics.Ascent) + b.Metrics.Height);
        }

        var c = new MathBox.Container();
        c.Metrics.Width  = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        double x = 0;
        foreach (var b in boxes)
        {
            b.X = x;
            b.Y = ascent - b.Metrics.Ascent;
            c.Children.Add(b);
            x += b.Metrics.Width;
        }

        return c;
    }

    // ── Glyph factory ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MathBox.Glyph"/> with approximate metrics computed from the
    /// font size (no actual font metrics calls — keeps the engine framework-free).
    ///
    /// Approximations:
    ///   em   = fontSizePt * (96/72)
    ///   width x em * 0.60 * len  (proportional average; good for typical math chars)
    ///   ascent x em * 0.75
    ///   height x em * 1.0
    /// </summary>
    private static MathBox.Glyph MakeGlyph(string text, string fontFamily, double fontSizePt, bool isItalic)
    {
        double em = Em(fontSizePt);
        int len = System.Math.Max(1, text.Length);

        // Per-character width varies; use tighter estimate for single chars (operators)
        double charW = len == 1
            ? EstimateCharWidth(text[0], em)
            : em * 0.58 * len;

        double ascent  = em * 0.75;
        double descent = em * 0.25;
        double height  = ascent + descent;

        var g = new MathBox.Glyph(text, fontFamily, fontSizePt, isItalic);
        g.Metrics.Width  = charW;
        g.Metrics.Height = height;
        g.Metrics.Ascent = ascent;
        return g;
    }

    /// <summary>Estimate character width in DIP for a single character.</summary>
    private static double EstimateCharWidth(char ch, double em)
    {
        return ch switch
        {
            ',' or '.' or ':' or ';' or '!' or '\'' => em * 0.28,
            '|' or '|'                              => em * 0.22,
            '(' or ')' or '[' or ']' or '{' or '}'  => em * 0.35,
            '?' or '?' or '?'                       => em * 0.65,
            'v'                                     => em * 0.65,
            '±' or '×' or '÷' or '≤' or '≥' or '≠' or '≈' => em * 0.60,
            '+' or '-' or '-' or '='                => em * 0.60,
            'i' or 'j' or 'l' or 'I' or '1'         => em * 0.38,
            'm' or 'M' or 'W' or 'w'                => em * 0.82,
            ' '                                     => em * 0.28,
            _                                       => em * 0.58
        };
    }
}

