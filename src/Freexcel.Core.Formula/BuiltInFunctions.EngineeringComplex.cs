using System.Globalization;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
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
