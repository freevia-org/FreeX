using System.Globalization;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
        return double.IsFinite(number) ? new NumberValue(ErfApprox(number)) : ErrorValue.Num;
    }

    private static ScalarValue ErfBetweenScalar(ScalarValue lower, ScalarValue upper)
    {
        if (lower is ErrorValue e0) return e0;
        if (upper is ErrorValue e1) return e1;
        var lowerNumber = ToNumber(lower);
        var upperNumber = ToNumber(upper);
        if (!double.IsFinite(lowerNumber) || !double.IsFinite(upperNumber)) return ErrorValue.Num;
        return NumberResult(ErfApprox(upperNumber) - ErfApprox(lowerNumber));
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
        return double.IsFinite(number) ? new NumberValue(1.0 - ErfApprox(number)) : ErrorValue.Num;
    }

    private static double ErfApprox(double x)
    {
        if (x == 0) return 0;

        var sign = Math.Sign(x);
        var ax = Math.Abs(x);
        const double p = 0.3275911;
        var t = 1.0 / (1.0 + p * ax);
        var y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-ax * ax);
        return sign * y;
    }

    private static ScalarValue ComplexFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var suffix = args.Count > 2 && args[2] is not BlankValue ? ToText(args[2]).ToLowerInvariant() : "i";
        if (suffix is not ("i" or "j")) return ErrorValue.Value;

        return TextResult(FormatComplex(ToNumber(args[0]), ToNumber(args[1]), suffix));
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
            : TextResult(FormatComplex(parsed.Real, -parsed.Imaginary, parsed.Suffix));
    }

    private static ScalarValue ImSum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double real = 0;
        double imaginary = 0;
        var suffix = "i";
        foreach (var value in FlattenComplexArguments(args))
        {
            var parsed = ParseComplexArgument(value);
            if (parsed.Error is not null) return parsed.Error;
            real += parsed.Real;
            imaginary += parsed.Imaginary;
            suffix = parsed.Suffix;
        }

        return TextResult(FormatComplex(real, imaginary, suffix));
    }

    private static ScalarValue ImSub(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var left = ParseComplexArgument(args[0]);
        if (left.Error is not null) return left.Error;
        var right = ParseComplexArgument(args[1]);
        if (right.Error is not null) return right.Error;

        return TextResult(FormatComplex(left.Real - right.Real, left.Imaginary - right.Imaginary, left.Suffix));
    }

    private static ScalarValue ImProduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double real = 1;
        double imaginary = 0;
        var suffix = "i";
        foreach (var value in FlattenComplexArguments(args))
        {
            var parsed = ParseComplexArgument(value);
            if (parsed.Error is not null) return parsed.Error;
            var nextReal = real * parsed.Real - imaginary * parsed.Imaginary;
            var nextImaginary = real * parsed.Imaginary + imaginary * parsed.Real;
            real = nextReal;
            imaginary = nextImaginary;
            suffix = parsed.Suffix;
        }

        return TextResult(FormatComplex(real, imaginary, suffix));
    }

    private static ScalarValue ImDiv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var left = ParseComplexArgument(args[0]);
        if (left.Error is not null) return left.Error;
        var right = ParseComplexArgument(args[1]);
        if (right.Error is not null) return right.Error;

        var denominator = right.Real * right.Real + right.Imaginary * right.Imaginary;
        if (denominator == 0) return ErrorValue.Num;

        var real = (left.Real * right.Real + left.Imaginary * right.Imaginary) / denominator;
        var imaginary = (left.Imaginary * right.Real - left.Real * right.Imaginary) / denominator;
        return TextResult(FormatComplex(real, imaginary, left.Suffix));
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
        return TextResult(FormatComplex(
            magnitude * Math.Cos(parsed.Imaginary),
            magnitude * Math.Sin(parsed.Imaginary),
            parsed.Suffix));
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
        return TextResult(FormatComplex(Math.Log(modulus) / divisor, angle / divisor, parsed.Suffix));
    }

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

    private static (double Real, double Imaginary, string Suffix, ErrorValue? Error) ParseComplexArgument(ScalarValue value)
    {
        if (value is ErrorValue e) return (0, 0, "i", e);
        if (value is BoolValue) return (0, 0, "i", ErrorValue.Value);
        if (TryCellNumber(value, out var number)) return (number, 0, "i", null);

        var text = ToText(value).Trim();
        if (text.Length == 0) return (0, 0, "i", ErrorValue.Num);

        var suffix = text[^1].ToString().ToLowerInvariant();
        if (suffix is not ("i" or "j"))
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var realOnly)
                ? (realOnly, 0, "i", null)
                : (0, 0, "i", ErrorValue.Num);
        }

        var body = text[..^1];
        if (!TrySplitComplexBody(body, out var realPart, out var imaginaryPart))
            return (0, 0, suffix, ErrorValue.Num);

        if (!TryParseComplexNumber(realPart, out var real) ||
            !TryParseImaginaryCoefficient(imaginaryPart, out var imaginary))
            return (0, 0, suffix, ErrorValue.Num);

        return (real, imaginary, suffix, null);
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
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

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

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatComplex(double real, double imaginary, string suffix)
    {
        if (Math.Abs(real) < 1e-14) real = 0;
        if (Math.Abs(imaginary) < 1e-14) imaginary = 0;
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
