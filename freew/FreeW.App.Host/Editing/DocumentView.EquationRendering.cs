using System.Windows;
using System.Windows.Documents;
using FreeW.App.Presentation.DocumentView.MathLayout;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Equation rendering for the continuous editor, print and XPS/PDF: every equation is laid out by the
/// shared, toolkit-neutral <see cref="EquationMathLayout"/> engine (Word's built-up "Professional" math with
/// Cambria Math's OpenType MATH metrics) and painted by <see cref="EquationMathVisual"/>.
/// </summary>
public sealed partial class DocumentView
{
    /// <summary>
    /// Renders an equation as an InlineUIContainer. The visual carries the model <see cref="Equation"/> on
    /// its Tag so CommitToModel can round-trip it, and reports its math baseline so the equation sits on the
    /// text baseline. Display equations (m:oMathPara) use display style; inline ones text style.
    /// </summary>
    private static InlineUIContainer BuildEquationRun(Equation equation, double? fontSizePt = null)
    {
        var element = BuildEquationMathVisual(equation, fontSizePt);
        return new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Baseline };
    }

    private static EquationMathVisual BuildEquationMathVisual(Equation equation, double? fontSizePt = null)
    {
        var sizePt = fontSizePt is > 0 ? fontSizePt.Value : DefaultFontSizePt;
        return new EquationMathVisual(equation, sizePt * PxPerPoint)
        {
            Tag = equation // carries the model equation so CommitToModel can round-trip it
        };
    }
}
