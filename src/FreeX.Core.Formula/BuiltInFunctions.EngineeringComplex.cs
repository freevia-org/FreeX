using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue Delta(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var second = args.Count > 1 ? args[1] : new NumberValue(0);
        if (args[0] is ErrorValue e0) return e0;
        if (second is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], second, DeltaScalar);
    }

    private static ScalarValue DeltaScalar(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue e0) return e0;
        if (right is ErrorValue e1) return e1;
        var leftNumber = ToNumber(left);
        var rightNumber = ToNumber(right);
        if (!double.IsFinite(leftNumber) || !double.IsFinite(rightNumber)) return ErrorValue.Num;
        return new NumberValue(leftNumber == rightNumber ? 1 : 0);
    }

    private static ScalarValue Gestep(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var step = args.Count > 1 ? args[1] : new NumberValue(0);
        if (args[0] is ErrorValue e0) return e0;
        if (step is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], step, GestepScalar);
    }

    private static ScalarValue GestepScalar(ScalarValue value, ScalarValue step)
    {
        if (value is ErrorValue e0) return e0;
        if (step is ErrorValue e1) return e1;
        var valueNumber = ToNumber(value);
        var stepNumber = ToNumber(step);
        if (!double.IsFinite(valueNumber) || !double.IsFinite(stepNumber)) return ErrorValue.Num;
        return new NumberValue(valueNumber >= stepNumber ? 1 : 0);
    }

    private static ScalarValue ErfFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count == 1)
        {
            if (args[0] is RangeValue range) return MapUnaryTextRange(range, ErfScalar);
            return ErfScalar(args[0]);
        }

        return MapBinaryMathArgs(args[0], args[1], ErfBetweenScalar);
    }

    private static ScalarValue ErfScalar(ScalarValue value)
    {
        if (value is ErrorValue e) return e;
        var number = ToNumber(value);
        // Use the shared high-precision Erf (built on the cancellation-free Erfc Chebyshev
        // approximation) instead of a coarse ~1.5e-7-accurate rational approximation, so ERF/
        // ERF.PRECISE match Excel to full double precision (e.g. ERF(2) = 0.9953222650189527).
        return double.IsFinite(number) ? new NumberValue(Erf(number)) : ErrorValue.Num;
    }

    private static ScalarValue ErfBetweenScalar(ScalarValue lower, ScalarValue upper)
    {
        if (lower is ErrorValue e0) return e0;
        if (upper is ErrorValue e1) return e1;
        var lowerNumber = ToNumber(lower);
        var upperNumber = ToNumber(upper);
        if (!double.IsFinite(lowerNumber) || !double.IsFinite(upperNumber)) return ErrorValue.Num;
        return NumberResult(Erf(upperNumber) - Erf(lowerNumber));
    }

    private static ScalarValue ErfcFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ErfcScalar);
        return ErfcScalar(args[0]);
    }

    private static ScalarValue ErfcScalar(ScalarValue value)
    {
        if (value is ErrorValue e) return e;
        var number = ToNumber(value);
        // Use the cancellation-free complementary error function (shared with NORMSDIST/NORMSCDF)
        // instead of 1-erf(x), which loses all precision (and eventually rounds to exactly 0) for
        // large x due to catastrophic cancellation.
        return double.IsFinite(number) ? new NumberValue(Erfc(number)) : ErrorValue.Num;
    }

    private static ScalarValue ComplexFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        // The suffix default of "i" only applies when the argument slot is truly omitted
        // (args.Count <= 2). When the slot is present but evaluates to a blank cell (e.g.
        // COMPLEX(3,4,A1) with A1 empty), Excel treats the blank as "" — neither "i" nor
        // "j" — and returns #VALUE!, rather than silently falling back to the default.
        if (args.Count > 2 && args[2] is BlankValue) return ErrorValue.Value;
        var suffix = args.Count > 2 ? ToText(args[2]) : "i";
        if (suffix is not ("i" or "j")) return ErrorValue.Value;

        return MapBinaryMathArgs(args[0], args[1], (realValue, imaginaryValue) => ComplexScalar(realValue, imaginaryValue, suffix));
    }

    private static ScalarValue ComplexScalar(ScalarValue realValue, ScalarValue imaginaryValue, string suffix)
    {
        if (realValue is ErrorValue e0) return e0;
        if (imaginaryValue is ErrorValue e1) return e1;

        var real = ToNumber(realValue);
        var imaginary = ToNumber(imaginaryValue);
        if (!double.IsFinite(real) || !double.IsFinite(imaginary)) return ErrorValue.Num;

        // COMPLEX just formats its literal inputs (no trigonometric computation), so unlike the
        // Im*/trig-derived complex results below, tiny user-entered components must not be
        // snapped to zero.
        return ComplexTextResult(real, imaginary, suffix, snapNearZero: false);
    }

    private static ScalarValue ImReal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImRealScalar);
        return ImRealScalar(args[0]);
    }

    private static ScalarValue ImRealScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        return parsed.Error is not null ? parsed.Error : new NumberValue(parsed.Real);
    }

    private static ScalarValue Imaginary(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImaginaryScalar);
        return ImaginaryScalar(args[0]);
    }

    private static ScalarValue ImaginaryScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        return parsed.Error is not null ? parsed.Error : new NumberValue(parsed.Imaginary);
    }

    private static ScalarValue ImAbs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImAbsScalar);
        return ImAbsScalar(args[0]);
    }

    private static ScalarValue ImAbsScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        return parsed.Error is not null
            ? parsed.Error
            : NumberResult(Math.Sqrt(parsed.Real * parsed.Real + parsed.Imaginary * parsed.Imaginary));
    }

    private static ScalarValue ImArgument(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImArgumentScalar);
        return ImArgumentScalar(args[0]);
    }

    private static ScalarValue ImArgumentScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;
        if (parsed.Real == 0 && parsed.Imaginary == 0) return ErrorValue.DivByZero;

        return NumberResult(Math.Atan2(parsed.Imaginary, parsed.Real));
    }

    private static ScalarValue ImConjugate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImConjugateScalar);
        return ImConjugateScalar(args[0]);
    }

    private static ScalarValue ImConjugateScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        return parsed.Error is not null
            ? parsed.Error
            : ComplexTextResult(parsed.Real, -parsed.Imaginary, parsed.Suffix);
    }

    private static ScalarValue ImSum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double real = 0;
        double imaginary = 0;
        var suffix = "i";
        string? explicitSuffix = null;
        foreach (var value in FlattenComplexArguments(args))
        {
            var parsed = ParseComplexArgument(value);
            if (parsed.Error is not null) return parsed.Error;
            if (parsed.ExplicitSuffix is not null)
            {
                // Excel rejects mixing "i" and "j" notation across arguments, even when one
                // side's explicit suffix carries a zero coefficient (e.g. "3+0j").
                if (explicitSuffix is not null && explicitSuffix != parsed.ExplicitSuffix) return ErrorValue.Num;
                explicitSuffix = parsed.ExplicitSuffix;
            }

            real += parsed.Real;
            imaginary += parsed.Imaginary;
            suffix = parsed.Suffix;
        }

        return ComplexTextResult(real, imaginary, explicitSuffix ?? suffix);
    }

    private static ScalarValue ImSub(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], ImSubScalar);
    }

    private static ScalarValue ImSubScalar(ScalarValue leftValue, ScalarValue rightValue)
    {
        var left = ParseComplexArgument(leftValue);
        if (left.Error is not null) return left.Error;
        var right = ParseComplexArgument(rightValue);
        if (right.Error is not null) return right.Error;

        // Excel rejects mixing "i" and "j" notation across arguments. The check compares the
        // EXPLICIT suffix recorded from the source text (present even for a zero coefficient
        // like "3+0j"), not just whether Imaginary != 0, so "3+0j" vs "5+2i" is still caught.
        // A truly bare real (no suffix at all, e.g. "3") never carries an explicit suffix, so
        // it never conflicts with the other operand's notation.
        if (left.ExplicitSuffix is not null && right.ExplicitSuffix is not null && left.ExplicitSuffix != right.ExplicitSuffix) return ErrorValue.Num;

        var suffix = left.Imaginary != 0 ? left.Suffix : right.Suffix;
        return ComplexTextResult(left.Real - right.Real, left.Imaginary - right.Imaginary, suffix);
    }

    private static ScalarValue ImProduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double real = 1;
        double imaginary = 0;
        var suffix = "i";
        string? explicitSuffix = null;
        foreach (var value in FlattenComplexArguments(args))
        {
            var parsed = ParseComplexArgument(value);
            if (parsed.Error is not null) return parsed.Error;
            if (parsed.ExplicitSuffix is not null)
            {
                // Excel rejects mixing "i" and "j" notation across arguments, even when one
                // side's explicit suffix carries a zero coefficient (e.g. "3+0j").
                if (explicitSuffix is not null && explicitSuffix != parsed.ExplicitSuffix) return ErrorValue.Num;
                explicitSuffix = parsed.ExplicitSuffix;
            }

            var nextReal = real * parsed.Real - imaginary * parsed.Imaginary;
            var nextImaginary = real * parsed.Imaginary + imaginary * parsed.Real;
            real = nextReal;
            imaginary = nextImaginary;
            suffix = parsed.Suffix;
        }

        return ComplexTextResult(real, imaginary, explicitSuffix ?? suffix);
    }

    private static ScalarValue ImPower(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], ImPowerScalar);
    }

    private static ScalarValue ImPowerScalar(ScalarValue value, ScalarValue exponentValue)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;
        if (exponentValue is ErrorValue error) return error;

        double exponent = ToNumber(exponentValue);
        if (!double.IsFinite(exponent)) return ErrorValue.Num;

        double modulus = Math.Sqrt(parsed.Real * parsed.Real + parsed.Imaginary * parsed.Imaginary);
        if (modulus == 0 && exponent <= 0) return ErrorValue.Num;

        double magnitude = Math.Pow(modulus, exponent);
        double angle = Math.Atan2(parsed.Imaginary, parsed.Real) * exponent;
        if (!double.IsFinite(magnitude) || !double.IsFinite(angle)) return ErrorValue.Num;

        return ComplexTextResult(
            magnitude * Math.Cos(angle),
            magnitude * Math.Sin(angle),
            parsed.Suffix);
    }

    private static ScalarValue ImDiv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], ImDivScalar);
    }

    private static ScalarValue ImDivScalar(ScalarValue leftValue, ScalarValue rightValue)
    {
        var left = ParseComplexArgument(leftValue);
        if (left.Error is not null) return left.Error;
        var right = ParseComplexArgument(rightValue);
        if (right.Error is not null) return right.Error;

        // Excel rejects mixing "i" and "j" notation across arguments. The check compares the
        // EXPLICIT suffix recorded from the source text (present even for a zero coefficient
        // like "3+0j"), not just whether Imaginary != 0, so "3+0j" vs "5+2i" is still caught.
        // A truly bare real (no suffix at all, e.g. "3") never carries an explicit suffix, so
        // it never conflicts with the other operand's notation.
        if (left.ExplicitSuffix is not null && right.ExplicitSuffix is not null && left.ExplicitSuffix != right.ExplicitSuffix) return ErrorValue.Num;

        var denominator = right.Real * right.Real + right.Imaginary * right.Imaginary;
        if (denominator == 0) return ErrorValue.Num;

        var real = (left.Real * right.Real + left.Imaginary * right.Imaginary) / denominator;
        var imaginary = (left.Imaginary * right.Real - left.Real * right.Imaginary) / denominator;
        var suffix = left.Imaginary != 0 ? left.Suffix : right.Suffix;
        return ComplexTextResult(real, imaginary, suffix);
    }

    private static ScalarValue ImCos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImCosScalar);
        return ImCosScalar(args[0]);
    }

    private static ScalarValue ImCosScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return ComplexTextResult(
            Math.Cos(parsed.Real) * Math.Cosh(parsed.Imaginary),
            -Math.Sin(parsed.Real) * Math.Sinh(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImCot(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImCotScalar);
        return ImCotScalar(args[0]);
    }

    private static ScalarValue ImCotScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        double tanDenominator = Math.Cos(2.0 * parsed.Real) + Math.Cosh(2.0 * parsed.Imaginary);
        if (tanDenominator == 0) return ErrorValue.Num;

        double tanReal = Math.Sin(2.0 * parsed.Real) / tanDenominator;
        double tanImaginary = Math.Sinh(2.0 * parsed.Imaginary) / tanDenominator;
        return FormatReciprocalComplex(tanReal, tanImaginary, parsed.Suffix);
    }

    private static ScalarValue ImCosh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImCoshScalar);
        return ImCoshScalar(args[0]);
    }

    private static ScalarValue ImCoshScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return ComplexTextResult(
            Math.Cosh(parsed.Real) * Math.Cos(parsed.Imaginary),
            Math.Sinh(parsed.Real) * Math.Sin(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImCsc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImCscScalar);
        return ImCscScalar(args[0]);
    }

    private static ScalarValue ImCscScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return FormatReciprocalComplex(
            Math.Sin(parsed.Real) * Math.Cosh(parsed.Imaginary),
            Math.Cos(parsed.Real) * Math.Sinh(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImCsch(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImCschScalar);
        return ImCschScalar(args[0]);
    }

    private static ScalarValue ImCschScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return FormatReciprocalComplex(
            Math.Sinh(parsed.Real) * Math.Cos(parsed.Imaginary),
            Math.Cosh(parsed.Real) * Math.Sin(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImExp(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImExpScalar);
        return ImExpScalar(args[0]);
    }

    private static ScalarValue ImExpScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        double magnitude = Math.Exp(parsed.Real);
        return ComplexTextResult(
            magnitude * Math.Cos(parsed.Imaginary),
            magnitude * Math.Sin(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImSec(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImSecScalar);
        return ImSecScalar(args[0]);
    }

    private static ScalarValue ImSecScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return FormatReciprocalComplex(
            Math.Cos(parsed.Real) * Math.Cosh(parsed.Imaginary),
            -Math.Sin(parsed.Real) * Math.Sinh(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImSech(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImSechScalar);
        return ImSechScalar(args[0]);
    }

    private static ScalarValue ImSechScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return FormatReciprocalComplex(
            Math.Cosh(parsed.Real) * Math.Cos(parsed.Imaginary),
            Math.Sinh(parsed.Real) * Math.Sin(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImLn(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImLnScalar);
        return ImLnScalar(args[0]);
    }

    private static ScalarValue ImLnScalar(ScalarValue value) =>
        ImLogScalar(value, 1.0);

    private static ScalarValue ImLog10(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImLog10Scalar);
        return ImLog10Scalar(args[0]);
    }

    private static ScalarValue ImLog10Scalar(ScalarValue value) =>
        ImLogScalar(value, Math.Log(10.0));

    private static ScalarValue ImLog2(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImLog2Scalar);
        return ImLog2Scalar(args[0]);
    }

    private static ScalarValue ImLog2Scalar(ScalarValue value) =>
        ImLogScalar(value, Math.Log(2.0));

    private static ScalarValue ImLogScalar(ScalarValue value, double divisor)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        double modulus = Math.Sqrt(parsed.Real * parsed.Real + parsed.Imaginary * parsed.Imaginary);
        if (modulus == 0) return ErrorValue.Num;

        double angle = Math.Atan2(parsed.Imaginary, parsed.Real);
        return ComplexTextResult(Math.Log(modulus) / divisor, angle / divisor, parsed.Suffix);
    }

    private static ScalarValue ImSin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImSinScalar);
        return ImSinScalar(args[0]);
    }

    private static ScalarValue ImSinScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return ComplexTextResult(
            Math.Sin(parsed.Real) * Math.Cosh(parsed.Imaginary),
            Math.Cos(parsed.Real) * Math.Sinh(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImSinh(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImSinhScalar);
        return ImSinhScalar(args[0]);
    }

    private static ScalarValue ImSinhScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        return ComplexTextResult(
            Math.Sinh(parsed.Real) * Math.Cos(parsed.Imaginary),
            Math.Cosh(parsed.Real) * Math.Sin(parsed.Imaginary),
            parsed.Suffix);
    }

    private static ScalarValue ImSqrt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImSqrtScalar);
        return ImSqrtScalar(args[0]);
    }

    private static ScalarValue ImSqrtScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        double modulus = Math.Sqrt(parsed.Real * parsed.Real + parsed.Imaginary * parsed.Imaginary);
        double real = Math.Sqrt((modulus + parsed.Real) / 2.0);
        double imaginary = Math.CopySign(Math.Sqrt(Math.Max(0.0, (modulus - parsed.Real) / 2.0)), parsed.Imaginary);
        return ComplexTextResult(real, imaginary, parsed.Suffix);
    }

    private static ScalarValue ImTan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ImTanScalar);
        return ImTanScalar(args[0]);
    }

    private static ScalarValue ImTanScalar(ScalarValue value)
    {
        var parsed = ParseComplexArgument(value);
        if (parsed.Error is not null) return parsed.Error;

        double denominator = Math.Cos(2.0 * parsed.Real) + Math.Cosh(2.0 * parsed.Imaginary);
        if (denominator == 0) return ErrorValue.Num;

        double real = Math.Sin(2.0 * parsed.Real) / denominator;
        double imaginary = Math.Sinh(2.0 * parsed.Imaginary) / denominator;
        return ComplexTextResult(real, imaginary, parsed.Suffix);
    }

    private static ScalarValue FormatReciprocalComplex(double real, double imaginary, string suffix)
    {
        double denominator = real * real + imaginary * imaginary;
        if (denominator == 0) return ErrorValue.Num;

        return ComplexTextResult(real / denominator, -imaginary / denominator, suffix);
    }

    private static ScalarValue ComplexTextResult(double real, double imaginary, string suffix, bool snapNearZero = true) =>
        double.IsFinite(real) && double.IsFinite(imaginary)
            ? TextResult(FormatComplex(real, imaginary, suffix, snapNearZero))
            : ErrorValue.Num;

    private static IEnumerable<ScalarValue> FlattenComplexArguments(IReadOnlyList<ScalarValue> args)
    {
        foreach (var arg in args)
        {
            if (arg is RangeValue range)
            {
                foreach (var cell in range.Flatten())
                    yield return cell;
            }
            else
            {
                yield return arg;
            }
        }
    }

    private static (double Real, double Imaginary, string Suffix, string? ExplicitSuffix, ErrorValue? Error) ParseComplexArgument(ScalarValue value)
    {
        if (value is ErrorValue e) return (0, 0, "i", null, e);
        if (value is BoolValue) return (0, 0, "i", null, ErrorValue.Value);
        // A blank cell flowing in through a range argument (e.g. IMSUM(A1:A3) with a gap)
        // is treated as the complex number 0, matching Excel's usual blank-in-range handling
        // for numeric-aggregate functions rather than erroring out the whole computation.
        if (value is BlankValue) return (0, 0, "i", null, null);
        if (TryCellNumber(value, out var number))
            return double.IsFinite(number)
                ? (number, 0, "i", null, null)
                : (0, 0, "i", null, ErrorValue.Num);

        var text = ToText(value).Trim();
        if (text.Length == 0) return (0, 0, "i", null, ErrorValue.Num);

        var suffix = text[^1].ToString();
        if (suffix is not ("i" or "j"))
        {
            return TryParseComplexNumber(text, out var realOnly)
                ? (realOnly, 0, "i", null, null)
                : (0, 0, "i", null, ErrorValue.Num);
        }

        var body = text[..^1];
        if (!TrySplitComplexBody(body, out var realPart, out var imaginaryPart))
            return (0, 0, suffix, suffix, ErrorValue.Num);

        if (!TryParseComplexNumber(realPart, out var real) ||
            !TryParseImaginaryCoefficient(imaginaryPart, out var imaginary))
            return (0, 0, suffix, suffix, ErrorValue.Num);

        // The i/j suffix was explicitly present in the source text (e.g. "3+0j"), even
        // though the parsed imaginary coefficient may be zero. Record it separately from
        // the display Suffix so mixed-notation mismatch checks (IMSUB/IMSUM/IMPRODUCT/
        // IMDIV) catch cases like "3+0j" vs "5+2i" that a plain Imaginary != 0 test misses,
        // while a truly bare real (no suffix at all) never triggers a mismatch.
        return (real, imaginary, suffix, suffix, null);
    }

    private static bool TrySplitComplexBody(string body, out string realPart, out string imaginaryPart)
    {
        realPart = "0";
        imaginaryPart = body;
        if (body.Length == 0 || body is "+" or "-") return true;

        for (int i = body.Length - 1; i > 0; i--)
        {
            if ((body[i] == '+' || body[i] == '-') && body[i - 1] is not ('e' or 'E'))
            {
                realPart = body[..i];
                imaginaryPart = body[i..];
                return true;
            }
        }

        return true;
    }

    private static bool TryParseComplexNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static bool TryParseImaginaryCoefficient(string text, out double value)
    {
        if (text.Length == 0 || text == "+")
        {
            value = 1;
            return true;
        }

        if (text == "-")
        {
            value = -1;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    private static string FormatComplex(double real, double imaginary, string suffix, bool snapNearZero = true)
    {
        if (snapNearZero)
        {
            if (Math.Abs(real) < 1e-14) real = 0;
            if (Math.Abs(imaginary) < 1e-14) imaginary = 0;
        }

        if (real == 0 && imaginary == 0) return "0";
        if (imaginary == 0) return FormatComplexNumber(real);

        var coefficient = Math.Abs(imaginary) == 1 ? "" : FormatComplexNumber(Math.Abs(imaginary));
        var imaginaryText = coefficient + suffix;
        if (real == 0) return imaginary < 0 ? "-" + imaginaryText : imaginaryText;

        return FormatComplexNumber(real) + (imaginary < 0 ? "-" : "+") + imaginaryText;
    }

    private static string FormatComplexNumber(double value) =>
        value.ToString("G15", CultureInfo.InvariantCulture);
}
