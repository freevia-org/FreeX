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
            MathNode.Limit   l  => LayoutLimit(l, fontFamily, fontSizePt),
            MathNode.Func    fn => LayoutFunc(fn, fontFamily, fontSizePt),
            MathNode.Delim   d  => LayoutDelim(d, fontFamily, fontSizePt),
            MathNode.Acc     a  => LayoutAcc(a, fontFamily, fontSizePt),
            MathNode.Bar     b  => LayoutBar(b, fontFamily, fontSizePt),
            MathNode.GroupChr g => LayoutGroupChr(g, fontFamily, fontSizePt),
            MathNode.Matrix  m  => LayoutMatrix(m, fontFamily, fontSizePt),
            MathNode.EqArray e  => LayoutEqArray(e, fontFamily, fontSizePt),
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
        return frac.Type switch
        {
            MathNode.FracType.Linear => LayoutFracLinear(frac, fontFamily, fontSizePt),
            // HA6: a full skewed (diagonal) layout is not implemented; approximate
            // "skw" as the linear a/b form rather than rendering it as a bar fraction.
            MathNode.FracType.Skewed => LayoutFracLinear(frac, fontFamily, fontSizePt),
            MathNode.FracType.NoBar  => LayoutFracStacked(frac, fontFamily, fontSizePt, drawBar: false),
            _                        => LayoutFracStacked(frac, fontFamily, fontSizePt, drawBar: true),
        };
    }

    /// <summary>
    /// Stacked fraction layout used for both "bar" (default) and "noBar" (binomial)
    /// types: numerator over denominator, centered on the math axis. "bar" additionally
    /// draws the horizontal rule; "noBar" omits it but keeps identical spacing/centering.
    /// </summary>
    private static MathBox LayoutFracStacked(MathNode.Frac frac, string fontFamily, double fontSizePt, bool drawBar)
    {
        double em = Em(fontSizePt);
        double childSizePt = fontSizePt * 0.85; // numerator/denominator slightly smaller

        var numBox = LayoutNode(frac.Numerator, fontFamily, childSizePt);
        var denBox = LayoutNode(frac.Denominator, fontFamily, childSizePt);

        // Fraction bar is on the math axis = 0.45 em above baseline
        double mathAxisAboveBaseline = em * 0.45;
        double barThickness = em * 0.07;
        double gap = em * 0.10; // gap between bar (or bar's would-be position) and num/den

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
        //
        // "noBar" keeps this exact geometry (so num/den centering and spacing is
        // unchanged) — it only skips adding the HRule child.

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

        // Fraction bar (omitted for "noBar" — binomial-coefficient style)
        if (drawBar)
        {
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
        }

        // Denominator
        denBox.X = denX; denBox.Y = denY;
        container.Children.Add(denBox);

        return container;
    }

    /// <summary>
    /// Linear fraction layout ("lin", and the "skw" approximation): numerator,
    /// slash, denominator, all inline on the baseline — not stacked.
    /// </summary>
    private static MathBox LayoutFracLinear(MathNode.Frac frac, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double gap = em * 0.08;

        var numBox   = LayoutNode(frac.Numerator, fontFamily, fontSizePt);
        var slashBox = MakeGlyph("/", fontFamily, fontSizePt, isItalic: false);
        var denBox   = LayoutNode(frac.Denominator, fontFamily, fontSizePt);

        // Common baseline = max ascent across the three inline boxes
        double ascent = Math.Max(numBox.Metrics.Ascent, Math.Max(slashBox.Metrics.Ascent, denBox.Metrics.Ascent));

        double totalW = numBox.Metrics.Width + gap + slashBox.Metrics.Width + gap + denBox.Metrics.Width;
        double totalH = Math.Max(
            (ascent - numBox.Metrics.Ascent) + numBox.Metrics.Height,
            Math.Max(
                (ascent - slashBox.Metrics.Ascent) + slashBox.Metrics.Height,
                (ascent - denBox.Metrics.Ascent) + denBox.Metrics.Height));

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        double x = 0;
        numBox.X = x; numBox.Y = ascent - numBox.Metrics.Ascent;
        container.Children.Add(numBox);
        x += numBox.Metrics.Width + gap;

        slashBox.X = x; slashBox.Y = ascent - slashBox.Metrics.Ascent;
        container.Children.Add(slashBox);
        x += slashBox.Metrics.Width + gap;

        denBox.X = x; denBox.Y = ascent - denBox.Metrics.Ascent;
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

        // HB3: for a normal-size base, scriptY is negative (the superscript
        // rises above the base's own top). Rather than clamping it to 0 —
        // which draws the script too low and never grows the container's
        // ascent to contain it — shift the WHOLE box down by the deficit so
        // the baseline stays put and the container grows upward.
        double deficit = scriptY < 0 ? -scriptY : 0;
        double baseY = deficit;
        scriptY += deficit;

        double totalH = Math.Max(baseY + baseBox.Metrics.Height, scriptY + scriptBox.Metrics.Height);
        double totalW = baseBox.Metrics.Width + scriptBox.Metrics.Width + em * 0.03;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseBox.Metrics.Ascent + deficit;

        baseBox.X = 0; baseBox.Y = baseY;
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

        // HB3 (symmetric case): a deep subscript never needs to shift the
        // Ascent (the baseline never moves for a sub — only the descent
        // grows), so folding its bottom extent into totalH via Math.Max
        // already reports the correct (larger) Descent = totalH - Ascent.
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

        // Sub: top at baseline + 0.25em
        double subY = baseline + em * 0.25;

        // HB3: for a normal-size base, supY is negative (the superscript
        // rises above the base's own top). Shift the whole box down by the
        // deficit instead of clamping supY to 0, and grow the container's
        // Ascent by the same deficit so the baseline stays consistent.
        double deficit = supY < 0 ? -supY : 0;
        double baseY = deficit;
        supY += deficit;
        subY += deficit;

        double scriptX = baseBox.Metrics.Width + em * 0.03;
        double scriptW = Math.Max(subBox.Metrics.Width, supBox.Metrics.Width);

        double totalH = Math.Max(baseY + baseBox.Metrics.Height, Math.Max(
            supY + supBox.Metrics.Height,
            subY + subBox.Metrics.Height));
        double totalW = scriptX + scriptW;

        var container = new MathBox.Container();
        container.Metrics.Width  = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = baseline + deficit;

        baseBox.X = 0; baseBox.Y = baseY;
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

            // Math baseline: align operand's baseline with the op's baseline
            double opBaseline = opY + opBox.Metrics.Ascent;

            // Operand: vertically align its baseline with the operator's baseline
            double operandY = opBaseline - operandBox.Metrics.Ascent;
            double operandBottom = operandY + operandBox.Metrics.Height;
            double operandTop = Math.Min(0, operandY);

            // HB2: fold the operand's full extent (which may rise above the
            // top row or extend below the last limit row) into totalH so a
            // tall operand (e.g. a fraction after ? / ?) is never clipped.
            double stackH = supH + opBox.Metrics.Height + subH;
            double totalH = Math.Max(stackH, operandBottom) - operandTop;
            double totalW = opColW + em * 0.06 + operandBox.Metrics.Width;

            // opBaseline == opY + opBox.Metrics.Ascent == supH + opBox.Metrics.Ascent;
            // shift the ascent up by the same amount the operand rose above the top.
            double ascent = opBaseline - operandTop;

            var c = new MathBox.Container();
            c.Metrics.Width  = totalW;
            c.Metrics.Height = totalH;
            c.Metrics.Ascent = ascent;

            // If the operand rises above the stack's top (operandTop < 0),
            // shift every child down by -operandTop so all Y's stay >= 0
            // while the baseline (Ascent) already accounts for the shift.
            double shift = -operandTop;

            // Sup limit
            if (supLimBox is not null)
            {
                supLimBox.X = (opColW - supLimBox.Metrics.Width) / 2; supLimBox.Y = 0 + shift;
                c.Children.Add(supLimBox);
            }

            // Operator
            opBox.X = (opColW - opBox.Metrics.Width) / 2; opBox.Y = opY + shift;
            c.Children.Add(opBox);

            // Sub limit
            if (subLimBox is not null)
            {
                subLimBox.X = (opColW - subLimBox.Metrics.Width) / 2; subLimBox.Y = subY + shift;
                c.Children.Add(subLimBox);
            }

            operandBox.X = opColW + em * 0.06; operandBox.Y = operandY + shift;
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

            // Operand: vertically align its baseline with the operator's baseline.
            double operandY = baseline - operandBox.Metrics.Ascent;
            double operandBottom = operandY + operandBox.Metrics.Height;
            double operandTop = Math.Min(0, operandY);

            // HB1: fold the operand's full extent into totalH (it was previously
            // computed from only the operator + limit-script boxes) so a tall
            // operand (e.g. ? of a fraction) is fully contained, not clipped.
            double scriptStackH = Math.Max(colH, Math.Max(
                supLimBox is not null ? supY + supLimBox.Metrics.Height : 0,
                subLimBox is not null ? subY + subLimBox.Metrics.Height : 0));
            double totalH = Math.Max(scriptStackH, operandBottom) - operandTop;

            double ascent = baseline - operandTop;

            var c = new MathBox.Container();
            c.Metrics.Width  = totalW;
            c.Metrics.Height = totalH;
            c.Metrics.Ascent = ascent;

            // Shift every child down by -operandTop when the operand rises
            // above the operator/limit column's top (operandTop < 0).
            double shift = -operandTop;

            opBox.X = 0; opBox.Y = 0 + shift;
            c.Children.Add(opBox);

            if (supLimBox is not null)
            {
                supLimBox.X = scriptX; supLimBox.Y = supY + shift;
                c.Children.Add(supLimBox);
            }
            if (subLimBox is not null)
            {
                subLimBox.X = scriptX; subLimBox.Y = subY + shift;
                c.Children.Add(subLimBox);
            }

            operandBox.X = scriptX + scriptW + em * 0.06; operandBox.Y = operandY + shift;
            c.Children.Add(operandBox);

            return c;
        }
    }

    // ── Function layout ───────────────────────────────────────────────────

    private static MathBox LayoutLimit(MathNode.Limit limit, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double limitSizePt = fontSizePt * 0.70;
        double gap = em * 0.06;

        var baseBox = LayoutNode(limit.Base, fontFamily, fontSizePt);
        var limitBox = LayoutNode(limit.LimitValue, fontFamily, limitSizePt);

        double totalW = Math.Max(baseBox.Metrics.Width, limitBox.Metrics.Width);
        double totalH = baseBox.Metrics.Height + gap + limitBox.Metrics.Height;
        double ascent = limit.IsUpper
            ? limitBox.Metrics.Height + gap + baseBox.Metrics.Ascent
            : baseBox.Metrics.Ascent;

        var c = new MathBox.Container();
        c.Metrics.Width = totalW;
        c.Metrics.Height = totalH;
        c.Metrics.Ascent = ascent;

        baseBox.X = (totalW - baseBox.Metrics.Width) / 2.0;
        limitBox.X = (totalW - limitBox.Metrics.Width) / 2.0;

        if (limit.IsUpper)
        {
            limitBox.Y = 0;
            baseBox.Y = limitBox.Metrics.Height + gap;
            c.Children.Add(limitBox);
            c.Children.Add(baseBox);
        }
        else
        {
            baseBox.Y = 0;
            limitBox.Y = baseBox.Metrics.Height + gap;
            c.Children.Add(baseBox);
            c.Children.Add(limitBox);
        }

        return c;
    }

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

        // Per ECMA-376 §22.1.2.20 (CT_DPr), m:sepChr is only meaningful when there
        // are two or more m:e children; a single element never gets a separator,
        // and an explicit empty sepChr suppresses the glyph (elements still abut
        // with the same gap, just no visible character between them).
        bool hasSeparator = delim.Elements.Count > 1 && !string.IsNullOrEmpty(delim.SepChar);
        MathBox? MakeSep() => hasSeparator ? MakeGlyph(delim.SepChar, fontFamily, fontSizePt, isItalic: false) : null;

        // Layout inner elements separated by a small gap (matching the separator glyph, if any)
        double sepGap = em * 0.15;
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
        // Pre-measure separator glyphs (if any) so they can influence innerH/innerAscent
        // the same way the content boxes do (sized/baselined like the content).
        MathBox? sepSample = MakeSep();
        if (sepSample is not null)
        {
            innerH = Math.Max(innerH, sepSample.Metrics.Height);
            innerAscent = Math.Max(innerAscent, sepSample.Metrics.Ascent);
        }
        // Per-gap width between consecutive elements: a bare gap when there's no
        // separator glyph, or gap + glyph + gap when there is one (glyph flanked
        // symmetrically, matching the placement loop below exactly).
        double gapWidth = hasSeparator ? sepGap * 2 + sepSample!.Metrics.Width : sepGap;
        if (innerBoxes.Count > 1)
            innerW += gapWidth * (innerBoxes.Count - 1);

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

            // Separator glyph between elements (m:sepChr; default ",", suppressed when
            // there's only one element or the value is explicitly empty).
            if (i < innerBoxes.Count - 1)
            {
                x += sepGap;
                if (hasSeparator)
                {
                    var sep = MakeGlyph(delim.SepChar, fontFamily, fontSizePt, isItalic: false);
                    sep.X = x; sep.Y = innerTop + (innerAscent - sep.Metrics.Ascent);
                    c.Children.Add(sep);
                    x += sep.Metrics.Width + sepGap;
                }
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

    private static MathBox LayoutEqArray(MathNode.EqArray eqArray, string fontFamily, double fontSizePt)
    {
        double em = Em(fontSizePt);
        double rowGap = em * 0.20;

        if (eqArray.Rows.Count == 0)
            return MakeGlyph("", fontFamily, fontSizePt, false);

        var rows = new List<MathBox>(eqArray.Rows.Count);
        double totalW = 0;
        double totalH = 0;

        foreach (var row in eqArray.Rows)
        {
            var rowBox = LayoutNode(row, fontFamily, fontSizePt);
            rows.Add(rowBox);
            totalW = Math.Max(totalW, rowBox.Metrics.Width);
            totalH += rowBox.Metrics.Height;
        }

        totalH += rowGap * Math.Max(0, rows.Count - 1);

        // Like matrices, equation arrays are centered on the math axis. Clamp the
        // ascent to the container height so descent remains non-negative for short arrays.
        double ascent = Math.Min(totalH, totalH / 2.0 + em * 0.45);

        var container = new MathBox.Container();
        container.Metrics.Width = totalW;
        container.Metrics.Height = totalH;
        container.Metrics.Ascent = ascent;

        double y = 0;
        foreach (var rowBox in rows)
        {
            rowBox.X = (totalW - rowBox.Metrics.Width) / 2.0;
            rowBox.Y = y;
            container.Children.Add(rowBox);
            y += rowBox.Metrics.Height + rowGap;
        }

        return container;
    }

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

