using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // ════════════════════════════════════════════════════════════════════════
    // Phase A2 – CONVERT(number, from_unit, to_unit)
    // ════════════════════════════════════════════════════════════════════════

    private enum UnitCategory { Weight, Distance, Time, Pressure, Force, Energy, Power, Area, Volume, Speed, Information, Temperature }

    private static readonly Dictionary<string, (UnitCategory Cat, double Factor)> ConvertUnits = BuildConvertUnits();

    private static Dictionary<string, (UnitCategory Cat, double Factor)> BuildConvertUnits()
    {
        var d = new Dictionary<string, (UnitCategory, double)>(StringComparer.Ordinal);
        void Add(UnitCategory cat, string unit, double factor) => d[unit] = (cat, factor);

        // Weight (base = gram)
        Add(UnitCategory.Weight, "g", 1);
        Add(UnitCategory.Weight, "kg", 1000);
        Add(UnitCategory.Weight, "lbm", 453.59237);
        Add(UnitCategory.Weight, "ozm", 28.349523);
        Add(UnitCategory.Weight, "grain", 0.06479891);
        Add(UnitCategory.Weight, "stone", 6350.293);
        Add(UnitCategory.Weight, "ton", 907184.74);
        Add(UnitCategory.Weight, "uk_ton", 1016046.91);
        Add(UnitCategory.Weight, "mg", 0.001);
        Add(UnitCategory.Weight, "ug", 0.000001);
        Add(UnitCategory.Weight, "ng", 1e-9);
        Add(UnitCategory.Weight, "sg", 14593.903);
        Add(UnitCategory.Weight, "cwt", 45359.237);
        Add(UnitCategory.Weight, "uk_cwt", 50802.345);
        Add(UnitCategory.Weight, "u", 1.66053886e-24);

        // Distance (base = meter)
        Add(UnitCategory.Distance, "m", 1);
        Add(UnitCategory.Distance, "km", 1000);
        Add(UnitCategory.Distance, "mi", 1609.344);
        Add(UnitCategory.Distance, "survey_mi", 1609.347218694);
        Add(UnitCategory.Distance, "Nmi", 1852);
        Add(UnitCategory.Distance, "in", 0.0254);
        Add(UnitCategory.Distance, "ft", 0.3048);
        Add(UnitCategory.Distance, "yd", 0.9144);
        Add(UnitCategory.Distance, "ang", 1e-10);
        Add(UnitCategory.Distance, "ell", 1.143);
        Add(UnitCategory.Distance, "Pica", 0.00423333333);
        Add(UnitCategory.Distance, "Picapt", 0.000352777778);
        Add(UnitCategory.Distance, "pica", 0.00423333333);
        Add(UnitCategory.Distance, "cm", 0.01);
        Add(UnitCategory.Distance, "mm", 0.001);
        Add(UnitCategory.Distance, "um", 1e-6);
        Add(UnitCategory.Distance, "nm", 1e-9);
        Add(UnitCategory.Distance, "ly", 9.4607304725808e15);
        Add(UnitCategory.Distance, "au", 149597870700.0);
        Add(UnitCategory.Distance, "pc", 3.085677581491367e16);
        Add(UnitCategory.Distance, "parsec", 3.085677581491367e16);

        // Time (base = second)
        Add(UnitCategory.Time, "sec", 1);
        Add(UnitCategory.Time, "s", 1);
        Add(UnitCategory.Time, "min", 60);
        Add(UnitCategory.Time, "mn", 60);
        Add(UnitCategory.Time, "hr", 3600);
        Add(UnitCategory.Time, "day", 86400);
        Add(UnitCategory.Time, "d", 86400);
        Add(UnitCategory.Time, "yr", 31557600);

        // Pressure (base = Pa)
        Add(UnitCategory.Pressure, "Pa", 1);
        Add(UnitCategory.Pressure, "p", 1);
        Add(UnitCategory.Pressure, "atm", 101325);
        Add(UnitCategory.Pressure, "at", 101325);
        Add(UnitCategory.Pressure, "mmHg", 133.322);
        Add(UnitCategory.Pressure, "psi", 6894.757);
        Add(UnitCategory.Pressure, "Torr", 133.322);

        // Force (base = N)
        Add(UnitCategory.Force, "N", 1);
        Add(UnitCategory.Force, "dyn", 1e-5);
        Add(UnitCategory.Force, "lbf", 4.44822);
        Add(UnitCategory.Force, "pond", 0.00980665);

        // Energy (base = J)
        Add(UnitCategory.Energy, "J", 1);
        Add(UnitCategory.Energy, "kJ", 1000);
        Add(UnitCategory.Energy, "e", 1e-7);
        Add(UnitCategory.Energy, "c", 4.184);
        Add(UnitCategory.Energy, "cal", 4.184);
        Add(UnitCategory.Energy, "eV", 1.60218e-19);
        Add(UnitCategory.Energy, "HPh", 2684519.54);
        Add(UnitCategory.Energy, "Wh", 3600);
        Add(UnitCategory.Energy, "flb", 1.35582);
        Add(UnitCategory.Energy, "BTU", 1055.056);

        // Power (base = W)
        Add(UnitCategory.Power, "W", 1);
        Add(UnitCategory.Power, "kW", 1000);
        Add(UnitCategory.Power, "HP", 745.69987);
        Add(UnitCategory.Power, "PS", 735.49875);

        // Temperature (special — base = K, with offsets handled separately)
        Add(UnitCategory.Temperature, "C", double.NaN);
        Add(UnitCategory.Temperature, "F", double.NaN);
        Add(UnitCategory.Temperature, "K", double.NaN);
        Add(UnitCategory.Temperature, "Rank", double.NaN);
        Add(UnitCategory.Temperature, "Reau", double.NaN);
        // Excel also documents "cel"/"fah"/"kel" as alternate abbreviations for C/F/K.
        Add(UnitCategory.Temperature, "cel", double.NaN);
        Add(UnitCategory.Temperature, "fah", double.NaN);
        Add(UnitCategory.Temperature, "kel", double.NaN);

        // Area (base = m^2)
        Add(UnitCategory.Area, "m2", 1);
        Add(UnitCategory.Area, "m^2", 1);
        Add(UnitCategory.Area, "km2", 1e6);
        Add(UnitCategory.Area, "km^2", 1e6);
        Add(UnitCategory.Area, "mi2", 2589988.11);
        Add(UnitCategory.Area, "mi^2", 2589988.11);
        Add(UnitCategory.Area, "ft2", 0.092903);
        Add(UnitCategory.Area, "ft^2", 0.092903);
        Add(UnitCategory.Area, "in2", 0.000645);
        Add(UnitCategory.Area, "in^2", 0.000645);
        Add(UnitCategory.Area, "yd2", 0.836127);
        Add(UnitCategory.Area, "yd^2", 0.836127);
        Add(UnitCategory.Area, "ha", 10000);
        Add(UnitCategory.Area, "acre", 4046.856);

        // Volume (base = liter)
        Add(UnitCategory.Volume, "l", 1);
        Add(UnitCategory.Volume, "L", 1);
        Add(UnitCategory.Volume, "tsp", 0.00492892);
        Add(UnitCategory.Volume, "tbs", 0.0147868);
        Add(UnitCategory.Volume, "oz", 0.0295735);
        Add(UnitCategory.Volume, "cup", 0.236588);
        Add(UnitCategory.Volume, "pt", 0.473176);
        Add(UnitCategory.Volume, "qt", 0.946353);
        Add(UnitCategory.Volume, "gal", 3.785412);
        Add(UnitCategory.Volume, "m3", 1000);
        Add(UnitCategory.Volume, "m^3", 1000);
        Add(UnitCategory.Volume, "mi3", 4168181825441);
        Add(UnitCategory.Volume, "mi^3", 4168181825441);
        Add(UnitCategory.Volume, "ft3", 28.3168);
        Add(UnitCategory.Volume, "ft^3", 28.3168);
        Add(UnitCategory.Volume, "in3", 0.0163871);
        Add(UnitCategory.Volume, "in^3", 0.0163871);
        Add(UnitCategory.Volume, "yd3", 764.555);
        Add(UnitCategory.Volume, "yd^3", 764.555);
        Add(UnitCategory.Volume, "ml", 0.001);
        Add(UnitCategory.Volume, "cl", 0.01);
        Add(UnitCategory.Volume, "dl", 0.1);
        Add(UnitCategory.Volume, "Nmi3", 6352182208);
        Add(UnitCategory.Volume, "Nmi^3", 6352182208);

        // Speed (base = m/s)
        Add(UnitCategory.Speed, "m/s", 1);
        Add(UnitCategory.Speed, "m/h", 1.0 / 3600);
        Add(UnitCategory.Speed, "mph", 0.44704);
        Add(UnitCategory.Speed, "kn", 0.514444);

        // Information (base = bit)
        Add(UnitCategory.Information, "bit", 1);
        Add(UnitCategory.Information, "byte", 8);
        Add(UnitCategory.Information, "kbit", 1000);
        Add(UnitCategory.Information, "kbyte", 8000);
        Add(UnitCategory.Information, "Mbit", 1e6);
        Add(UnitCategory.Information, "Mbyte", 8e6);
        Add(UnitCategory.Information, "Gbit", 1e9);
        Add(UnitCategory.Information, "Gbyte", 8e9);
        Add(UnitCategory.Information, "Tbit", 1e12);
        Add(UnitCategory.Information, "Tbyte", 8e12);

        return d;
    }

    private static readonly Dictionary<string, double> ConvertPrefixes = new(StringComparer.Ordinal)
    {
        ["Y"] = 1e24, ["Z"] = 1e21, ["E"] = 1e18, ["P"] = 1e15, ["T"] = 1e12,
        ["G"] = 1e9, ["M"] = 1e6, ["k"] = 1e3, ["h"] = 1e2, ["da"] = 1e1, ["e"] = 1e1,
        ["d"] = 1e-1, ["c"] = 1e-2, ["m"] = 1e-3, ["u"] = 1e-6, ["n"] = 1e-9,
        ["p"] = 1e-12, ["f"] = 1e-15, ["a"] = 1e-18, ["z"] = 1e-21, ["y"] = 1e-24
    };

    private static readonly Dictionary<string, double> ConvertBinaryPrefixes = new(StringComparer.Ordinal)
    {
        ["Yi"] = Math.Pow(2, 80), ["Zi"] = Math.Pow(2, 70), ["Ei"] = Math.Pow(2, 60), ["Pi"] = Math.Pow(2, 50),
        ["Ti"] = Math.Pow(2, 40), ["Gi"] = Math.Pow(2, 30), ["Mi"] = Math.Pow(2, 20), ["ki"] = Math.Pow(2, 10)
    };

    private static bool TryResolveUnit(string unit, out UnitCategory cat, out double factor)
    {
        if (ConvertUnits.TryGetValue(unit, out var entry))
        {
            cat = entry.Cat;
            factor = entry.Factor;
            return true;
        }
        if (TryResolveBinaryPrefixedUnit(unit, out cat, out factor)) return true;

        // Try a SI prefix only when at least 2 chars remain — we don't want
        // single-letter prefixes (e.g. "m") to be re-interpreted when they
        // already exist as base units in the table above.
        if (TryResolvePrefixedUnit(unit, 2, out cat, out factor)) return true;
        if (TryResolvePrefixedUnit(unit, 1, out cat, out factor)) return true;

        cat = default; factor = 0; return false;
    }

    private static bool TryResolveBinaryPrefixedUnit(string unit, out UnitCategory cat, out double factor)
    {
        if (unit.Length > 2)
        {
            string p = unit[..2];
            string rest = unit[2..];
            if (ConvertBinaryPrefixes.TryGetValue(p, out double pFactor)
                && ConvertUnits.TryGetValue(rest, out var rEntry)
                && rEntry.Cat == UnitCategory.Information)
            {
                cat = rEntry.Cat;
                factor = rEntry.Factor * pFactor;
                return true;
            }
        }

        cat = default;
        factor = 0;
        return false;
    }

    private static bool TryResolvePrefixedUnit(string unit, int prefixLength, out UnitCategory cat, out double factor)
    {
        if (unit.Length > prefixLength)
        {
            string p = unit[..prefixLength];
            string rest = unit[prefixLength..];
            if (ConvertPrefixes.TryGetValue(p, out double pFactor)
                && ConvertUnits.TryGetValue(rest, out var rEntry)
                && rEntry.Cat != UnitCategory.Temperature)
            {
                cat = rEntry.Cat;
                factor = rEntry.Factor * pFactor;
                return true;
            }
        }

        cat = default;
        factor = 0;
        return false;
    }

    private static ScalarValue Convert(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], args[2], ConvertScalarWithUnits);
    }

    private static ScalarValue ConvertScalarWithUnits(ScalarValue numberValue, ScalarValue fromValue, ScalarValue toValue)
    {
        if (numberValue is ErrorValue numberError) return numberError;
        if (fromValue is ErrorValue fromError) return fromError;
        if (toValue is ErrorValue toError) return toError;
        return ConvertScalar(numberValue, ToText(fromValue), ToText(toValue));
    }

    private static ScalarValue ConvertScalar(ScalarValue numberValue, string from, string to)
    {
        double n = ToNumber(numberValue);
        if (!double.IsFinite(n)) return ErrorValue.Num;

        if (!TryResolveUnit(from, out var fromCat, out var fromFactor)) return ErrorValue.NA;
        if (!TryResolveUnit(to, out var toCat, out var toFactor)) return ErrorValue.NA;
        if (fromCat != toCat) return ErrorValue.NA;

        if (fromCat == UnitCategory.Temperature)
        {
            // Convert input to Kelvin, then to target.
            double k = from switch
            {
                "C" or "cel" => n + 273.15,
                "F" or "fah" => (n - 32) * 5.0 / 9.0 + 273.15,
                "K" or "kel" => n,
                "Rank" => n * 5.0 / 9.0,
                "Reau" => n * 5.0 / 4.0 + 273.15,
                _      => double.NaN
            };
            if (!double.IsFinite(k)) return ErrorValue.NA;
            double r = to switch
            {
                "C" or "cel" => k - 273.15,
                "F" or "fah" => (k - 273.15) * 9.0 / 5.0 + 32,
                "K" or "kel" => k,
                "Rank" => k * 9.0 / 5.0,
                "Reau" => (k - 273.15) * 4.0 / 5.0,
                _      => double.NaN
            };
            return double.IsFinite(r) ? NumberResult(r) : ErrorValue.NA;
        }

        return NumberResult(n * fromFactor / toFactor);
    }

    private static ScalarValue Bin2Dec(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToDecimal(args[0], 2, 10, 512L, 1024L);

    private static ScalarValue Bin2Hex(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 2, 10, 512L, 1024L, 16, upper: true);

    private static ScalarValue Bin2Oct(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 2, 10, 512L, 1024L, 8, upper: false);

    private static ScalarValue Dec2Bin(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DecimalToBase(args, 2, -512L, 511L, 1024L, 10, upper: false);

    private static ScalarValue Dec2Hex(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DecimalToBase(args, 16, -549755813888L, 549755813887L, 1099511627776L, 10, upper: true);

    private static ScalarValue Dec2Oct(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DecimalToBase(args, 8, -536870912L, 536870911L, 1073741824L, 10, upper: false);

    private static ScalarValue Hex2Bin(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 16, 10, 549755813888L, 1099511627776L, 2, upper: false);

    private static ScalarValue Hex2Dec(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToDecimal(args[0], 16, 10, 549755813888L, 1099511627776L);

    private static ScalarValue Hex2Oct(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 16, 10, 549755813888L, 1099511627776L, 8, upper: false);

    private static ScalarValue Oct2Bin(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 8, 10, 536870912L, 1073741824L, 2, upper: false);

    private static ScalarValue Oct2Dec(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToDecimal(args[0], 8, 10, 536870912L, 1073741824L);

    private static ScalarValue Oct2Hex(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BaseToBase(args, 8, 10, 536870912L, 1073741824L, 16, upper: true);

    private static ScalarValue BaseFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var minLength = args.Count > 2 ? args[2] : new BlankValue();
        if (minLength is ErrorValue e2) return e2;
        return MapTernaryTextArgs(args[0], args[1], minLength, BaseScalar);
    }

    private static ScalarValue BaseScalar(ScalarValue numberValue, ScalarValue radixValue, ScalarValue minLengthValue)
    {
        if (numberValue is ErrorValue e0) return e0;
        if (radixValue is ErrorValue e1) return e1;
        if (minLengthValue is ErrorValue e2) return e2;

        if (!TryGetEngineeringTruncatedInteger(numberValue, out var number)) return ErrorValue.Num;
        if (!TryGetEngineeringTruncatedInteger(radixValue, out var radix)) return ErrorValue.Num;
        if (number < 0 || number >= TwoToThe53 || radix is < 2 or > 36) return ErrorValue.Num;

        var converted = FormatUnsignedBase(number, (int)radix);
        if (minLengthValue is BlankValue) return new TextValue(converted);
        if (!TryGetEngineeringTruncatedInteger(minLengthValue, out var minLength) || minLength < 0 || minLength > 255) return ErrorValue.Num;
        return new TextValue(converted.PadLeft((int)Math.Max(minLength, converted.Length), '0'));
    }

    private static ScalarValue DecimalFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], DecimalScalar);
    }

    private static ScalarValue DecimalScalar(ScalarValue textValue, ScalarValue radixValue)
    {
        if (textValue is ErrorValue e0) return e0;
        if (radixValue is ErrorValue e1) return e1;
        if (!TryGetEngineeringTruncatedInteger(radixValue, out var radix) || radix is < 2 or > 36) return ErrorValue.Num;

        var text = ToText(textValue).Trim();
        if (text.Length == 0 || text.Length > 255) return ErrorValue.Num;

        double result = 0;
        foreach (var ch in text)
        {
            var digit = Base36DigitValue(ch);
            if (digit < 0 || digit >= radix) return ErrorValue.Num;
            result = result * radix + digit;
            if (result >= TwoToThe53) return ErrorValue.Num;
        }

        return new NumberValue(result);
    }

    private static ScalarValue BaseToDecimal(ScalarValue arg, int fromBase, int maxDigits, long signThreshold, long modulus)
    {
        if (arg is ErrorValue error) return error;
        if (arg is RangeValue range)
            return MapUnaryTextRange(range, value => BaseToDecimal(value, fromBase, maxDigits, signThreshold, modulus));
        return TryParseBaseNumber(arg, fromBase, maxDigits, signThreshold, modulus, out var value)
            ? new NumberValue(value)
            : ErrorValue.Num;
    }

    private static ScalarValue BaseToBase(IReadOnlyList<ScalarValue> args, int fromBase, int maxDigits, long signThreshold, long modulus, int toBase, bool upper)
    {
        if (args[0] is ErrorValue error) return error;
        if (args.Count > 1 && args[1] is ErrorValue placesError) return placesError;
        if (args.Count > 1 && args[0] is RangeValue && args[1] is RangeValue)
            return MapBinaryMathArgs(args[0], args[1], (number, places) => BaseToBaseScalar(number, places, fromBase, maxDigits, signThreshold, modulus, toBase, upper));
        if (args.Count > 1 && args[1] is RangeValue placesRange)
            return MapUnaryTextRange(placesRange, value => BaseToBaseScalar(args[0], value, fromBase, maxDigits, signThreshold, modulus, toBase, upper));
        if (args[0] is RangeValue range)
            return MapUnaryTextRange(range, value => BaseToBaseScalar(value, args.Count > 1 ? args[1] : null, fromBase, maxDigits, signThreshold, modulus, toBase, upper));
        return BaseToBaseScalar(args[0], args.Count > 1 ? args[1] : null, fromBase, maxDigits, signThreshold, modulus, toBase, upper);
    }

    private static ScalarValue BaseToBaseScalar(ScalarValue number, ScalarValue? places, int fromBase, int maxDigits, long signThreshold, long modulus, int toBase, bool upper)
    {
        if (number is ErrorValue error) return error;
        if (!TryParseBaseNumber(number, fromBase, maxDigits, signThreshold, modulus, out var value)) return ErrorValue.Num;
        if (value < 0) return DecimalToBaseText(value, toBase, NegativeModulusForBase(toBase), 10, upper);
        if (value > MaxPositiveValueForBase(toBase)) return ErrorValue.Num;
        return FormatBaseText(value, toBase, places, upper);
    }

    private static ScalarValue DecimalToBase(IReadOnlyList<ScalarValue> args, int toBase, long min, long max, long modulus, int negativeWidth, bool upper)
    {
        if (args[0] is ErrorValue error) return error;
        if (args.Count > 1 && args[1] is ErrorValue placesError) return placesError;
        if (args.Count > 1 && args[0] is RangeValue && args[1] is RangeValue)
            return MapBinaryMathArgs(args[0], args[1], (number, places) => DecimalToBaseScalar(number, places, toBase, min, max, modulus, negativeWidth, upper));
        if (args.Count > 1 && args[1] is RangeValue placesRange)
            return MapUnaryTextRange(placesRange, value => DecimalToBaseScalar(args[0], value, toBase, min, max, modulus, negativeWidth, upper));
        if (args[0] is RangeValue range)
            return MapUnaryTextRange(range, value => DecimalToBaseScalar(value, args.Count > 1 ? args[1] : null, toBase, min, max, modulus, negativeWidth, upper));
        return DecimalToBaseScalar(args[0], args.Count > 1 ? args[1] : null, toBase, min, max, modulus, negativeWidth, upper);
    }

    private static ScalarValue DecimalToBaseScalar(ScalarValue number, ScalarValue? places, int toBase, long min, long max, long modulus, int negativeWidth, bool upper)
    {
        if (number is ErrorValue error) return error;
        if (!TryGetEngineeringTruncatedInteger(number, out var value)) return ErrorValue.Num;
        if (value < min || value > max) return ErrorValue.Num;
        if (value < 0) return DecimalToBaseText(value, toBase, modulus, negativeWidth, upper);
        return FormatBaseText(value, toBase, places, upper);
    }

    private static ScalarValue DecimalToBaseText(long value, int toBase, long modulus, int width, bool upper)
    {
        string converted = System.Convert.ToString(value < 0 ? modulus + value : value, toBase);
        if (upper) converted = converted.ToUpperInvariant();
        return new TextValue(converted.PadLeft(width, '0'));
    }

    private static ScalarValue FormatBaseText(long value, int toBase, ScalarValue? placesArg, bool upper)
    {
        string converted = System.Convert.ToString(value, toBase);
        if (upper) converted = converted.ToUpperInvariant();
        if (placesArg is null or BlankValue) return new TextValue(converted);
        if (placesArg is ErrorValue error) return error;
        if (!TryGetEngineeringTruncatedInteger(placesArg, out var places) || places < 0 || places > 255) return ErrorValue.Num;
        if (places < converted.Length) return ErrorValue.Num;
        return new TextValue(converted.PadLeft((int)places, '0'));
    }

    private static bool TryParseBaseNumber(ScalarValue arg, int fromBase, int maxDigits, long signThreshold, long modulus, out long value)
    {
        value = 0;
        string text = ToText(arg).Trim();
        if (text.Length == 0 || text.Length > maxDigits) return false;

        foreach (char ch in text)
        {
            int digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'A' and <= 'F' => ch - 'A' + 10,
                >= 'a' and <= 'f' => ch - 'a' + 10,
                _ => -1
            };
            if (digit < 0 || digit >= fromBase) return false;
            value = value * fromBase + digit;
        }

        if (text.Length == maxDigits && value >= signThreshold) value -= modulus;
        return true;
    }

    private const long TwoToThe53 = 9007199254740992L;

    private static string FormatUnsignedBase(long value, int radix)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (value == 0) return "0";

        Span<char> buffer = stackalloc char[64];
        var index = buffer.Length;
        var current = value;
        while (current > 0)
        {
            buffer[--index] = digits[(int)(current % radix)];
            current /= radix;
        }

        return new string(buffer[index..]);
    }

    private static int Base36DigitValue(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'A' and <= 'Z' => ch - 'A' + 10,
        >= 'a' and <= 'z' => ch - 'a' + 10,
        _ => -1
    };

    private static long NegativeModulusForBase(int toBase) => toBase switch
    {
        2 => 1024L,
        8 => 1073741824L,
        16 => 1099511627776L,
        _ => throw new ArgumentOutOfRangeException(nameof(toBase), toBase, null)
    };

    private static long MaxPositiveValueForBase(int toBase) => toBase switch
    {
        2 => 511L,
        8 => 536870911L,
        16 => 549755813887L,
        _ => throw new ArgumentOutOfRangeException(nameof(toBase), toBase, null)
    };

    private static bool TryGetEngineeringInteger(ScalarValue arg, out long value)
    {
        value = 0;
        if (arg is ErrorValue) return false;
        double number = ToNumber(arg);
        if (!double.IsFinite(number) || Math.Truncate(number) != number) return false;
        if (number < long.MinValue || number > long.MaxValue) return false;
        value = (long)number;
        return true;
    }

    private const long MaxBitFunctionValue = 281474976710655L;

    private static ScalarValue BitAnd(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitBinary(args, (left, right) => left & right);

    private static ScalarValue BitOr(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitBinary(args, (left, right) => left | right);

    private static ScalarValue BitXor(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitBinary(args, (left, right) => left ^ right);

    private static ScalarValue BitBinary(IReadOnlyList<ScalarValue> args, Func<long, long, long> op)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (left, right) => BitBinaryScalar(left, right, op));
    }

    private static ScalarValue BitBinaryScalar(ScalarValue leftValue, ScalarValue rightValue, Func<long, long, long> op)
    {
        if (!TryGetBitInteger(leftValue, out var left)) return ErrorValue.Num;
        if (!TryGetBitInteger(rightValue, out var right)) return ErrorValue.Num;
        return new NumberValue(op(left, right));
    }

    private static ScalarValue BitLShift(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitShift(args, leftShift: true);

    private static ScalarValue BitRShift(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        BitShift(args, leftShift: false);

    private static ScalarValue BitShift(IReadOnlyList<ScalarValue> args, bool leftShift)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (number, shift) => BitShiftScalar(number, shift, leftShift));
    }

    private static ScalarValue BitShiftScalar(ScalarValue numberValue, ScalarValue shiftValue, bool leftShift)
    {
        if (!TryGetBitInteger(numberValue, out var number)) return ErrorValue.Num;
        if (!TryGetEngineeringInteger(shiftValue, out var shift) || Math.Abs(shift) > 53) return ErrorValue.Num;

        bool effectiveLeft = leftShift ? shift >= 0 : shift < 0;
        int bits = (int)Math.Abs(shift);
        if (effectiveLeft && bits > 0 && number > (MaxBitFunctionValue >> bits))
            return ErrorValue.Num;

        long result = effectiveLeft ? number << bits : number >> bits;
        return result > MaxBitFunctionValue ? ErrorValue.Num : new NumberValue(result);
    }

    private static bool TryGetBitInteger(ScalarValue arg, out long value)
    {
        if (!TryGetEngineeringInteger(arg, out value)) return false;
        return value >= 0 && value <= MaxBitFunctionValue;
    }

    private static bool TryGetEngineeringTruncatedInteger(ScalarValue arg, out long value)
    {
        value = 0;
        if (arg is ErrorValue) return false;
        double number = ToNumber(arg);
        if (!double.IsFinite(number)) return false;
        double truncated = Math.Truncate(number);
        if (truncated < long.MinValue || truncated > long.MaxValue) return false;
        value = (long)truncated;
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    // BESSELI/BESSELJ/BESSELK/BESSELY — registered here (rather than in the
    // Functions dictionary literal in BuiltInFunctions.cs, which is out of
    // scope for this fix) via a static constructor on the shared partial
    // class. Field initializers across all partial declarations run before
    // any explicit static constructor body, so `Functions` is guaranteed to
    // already contain its literal entries by the time this body executes.
    // ════════════════════════════════════════════════════════════════════════

    static BuiltInFunctions()
    {
        Functions["BESSELJ"] = (BesselJFunc, 2, 2);
        Functions["BESSELI"] = (BesselIFunc, 2, 2);
        Functions["BESSELY"] = (BesselYFunc, 2, 2);
        Functions["BESSELK"] = (BesselKFunc, 2, 2);
    }

    // Guard against pathological orders that would make the O(n) recurrences below
    // spin for an unreasonable amount of time (Excel itself overflows to #NUM! long
    // before n reaches values like this).
    private const long MaxBesselOrder = 100_000;

    private static ScalarValue BesselJFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (x, n) => BesselScalar(x, n, requirePositiveX: false, BesselJ));
    }

    private static ScalarValue BesselIFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (x, n) => BesselScalar(x, n, requirePositiveX: false, BesselI));
    }

    private static ScalarValue BesselYFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (x, n) => BesselScalar(x, n, requirePositiveX: true, BesselY));
    }

    private static ScalarValue BesselKFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return MapBinaryMathArgs(args[0], args[1], (x, n) => BesselScalar(x, n, requirePositiveX: true, BesselK));
    }

    private static ScalarValue BesselScalar(ScalarValue xValue, ScalarValue nValue, bool requirePositiveX, Func<int, double, double> fn)
    {
        if (xValue is ErrorValue e0) return e0;
        if (nValue is ErrorValue e1) return e1;

        double x = ToNumber(xValue);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        if (!TryGetEngineeringTruncatedInteger(nValue, out var n) || n < 0 || n > MaxBesselOrder) return ErrorValue.Num;
        if (requirePositiveX && x <= 0) return ErrorValue.Num;

        double result = fn((int)n, x);
        return double.IsFinite(result) ? NumberResult(result) : ErrorValue.Num;
    }

    // ── Bessel function of the first kind, J_n(x) ───────────────────────────
    // Standard published rational-approximation / recurrence algorithms
    // (Abramowitz & Stegun 9.4; the same formulas underlie the widely used
    // "Numerical Recipes" bessj0/bessj1/bessj routines).

    private static double BesselJ(int n, double x) => n switch
    {
        0 => BesselJ0(x),
        1 => BesselJ1(x),
        _ => BesselJN(n, x)
    };

    private static double BesselJ0(double x)
    {
        double ax = Math.Abs(x);
        if (ax < 8.0)
        {
            double y = x * x;
            double ans1 = 57568490574.0 + y * (-13362590354.0 + y * (651619640.7
                + y * (-11214424.18 + y * (77392.33017 + y * -184.9052456))));
            double ans2 = 57568490411.0 + y * (1029532985.0 + y * (9494680.718
                + y * (59272.64853 + y * (267.8532712 + y))));
            return ans1 / ans2;
        }
        else
        {
            double z = 8.0 / ax;
            double y = z * z;
            double xx = ax - 0.785398164;
            double ans1 = 1.0 + y * (-0.1098628627e-2 + y * (0.2734510407e-4
                + y * (-0.2073370639e-5 + y * 0.2093887211e-6)));
            double ans2 = -0.1562499995e-1 + y * (0.1430488765e-3
                + y * (-0.6911147651e-5 + y * (0.7621095161e-6 - y * 0.934935152e-7)));
            return Math.Sqrt(0.636619772 / ax) * (Math.Cos(xx) * ans1 - z * Math.Sin(xx) * ans2);
        }
    }

    private static double BesselJ1(double x)
    {
        double ax = Math.Abs(x);
        double ans;
        if (ax < 8.0)
        {
            double y = x * x;
            double ans1 = x * (72362614232.0 + y * (-7895059235.0 + y * (242396853.1
                + y * (-2972611.439 + y * (15704.48260 + y * -30.16036606)))));
            double ans2 = 144725228442.0 + y * (2300535178.0 + y * (18583304.74
                + y * (99447.43394 + y * (376.9991397 + y))));
            ans = ans1 / ans2;
        }
        else
        {
            double z = 8.0 / ax;
            double y = z * z;
            double xx = ax - 2.356194491;
            double ans1 = 1.0 + y * (0.183105e-2 + y * (-0.3516396496e-4
                + y * (0.2457520174e-5 + y * -0.240337019e-6)));
            double ans2 = 0.04687499995 + y * (-0.2002690873e-3
                + y * (0.8449199096e-5 + y * (-0.88228987e-6 + y * 0.105787412e-6)));
            ans = Math.Sqrt(0.636619772 / ax) * (Math.Cos(xx) * ans1 - z * Math.Sin(xx) * ans2);
            if (x < 0.0) ans = -ans;
        }
        return ans;
    }

    private static double BesselJN(int n, double x)
    {
        const double Acc = 40.0;
        const double BigNo = 1.0e10;
        const double BigNi = 1.0e-10;

        double ax = Math.Abs(x);
        if (ax == 0.0) return 0.0;

        double ans;
        if (ax > n)
        {
            double tox = 2.0 / ax;
            double bjm = BesselJ0(ax);
            double bj = BesselJ1(ax);
            for (int j = 1; j < n; j++)
            {
                double bjp = j * tox * bj - bjm;
                bjm = bj;
                bj = bjp;
            }
            ans = bj;
        }
        else
        {
            double tox = 2.0 / ax;
            int m = 2 * ((n + (int)Math.Sqrt(Acc * n)) / 2);
            bool jsum = false;
            double bjpAns = 0.0, sum = 0.0, bjp = 0.0, bj = 1.0;
            for (int j = m; j > 0; j--)
            {
                double bjm = j * tox * bj - bjp;
                bjp = bj;
                bj = bjm;
                if (Math.Abs(bj) > BigNo)
                {
                    bj *= BigNi;
                    bjp *= BigNi;
                    bjpAns *= BigNi;
                    sum *= BigNi;
                }
                if (jsum) sum += bj;
                jsum = !jsum;
                if (j == n) bjpAns = bjp;
            }
            sum = 2.0 * sum - bj;
            ans = bjpAns / sum;
        }

        return x < 0.0 && (n & 1) != 0 ? -ans : ans;
    }

    // ── Bessel function of the second kind, Y_n(x) — requires x > 0 ────────

    private static double BesselY(int n, double x) => n switch
    {
        0 => BesselY0(x),
        1 => BesselY1(x),
        _ => BesselYN(n, x)
    };

    private static double BesselY0(double x)
    {
        if (x < 8.0)
        {
            double y = x * x;
            double ans1 = -2957821389.0 + y * (7062834065.0 + y * (-512359803.6
                + y * (10879881.29 + y * (-86327.92757 + y * 228.4622733))));
            double ans2 = 40076544269.0 + y * (745249964.8 + y * (7189466.438
                + y * (47447.26470 + y * (226.1030244 + y))));
            return (ans1 / ans2) + 0.636619772 * BesselJ0(x) * Math.Log(x);
        }
        else
        {
            double z = 8.0 / x;
            double y = z * z;
            double xx = x - 0.785398164;
            double ans1 = 1.0 + y * (-0.1098628627e-2 + y * (0.2734510407e-4
                + y * (-0.2073370639e-5 + y * 0.2093887211e-6)));
            double ans2 = -0.1562499995e-1 + y * (0.1430488765e-3
                + y * (-0.6911147651e-5 + y * (0.7621095161e-6 + y * -0.934945152e-7)));
            return Math.Sqrt(0.636619772 / x) * (Math.Sin(xx) * ans1 + z * Math.Cos(xx) * ans2);
        }
    }

    private static double BesselY1(double x)
    {
        if (x < 8.0)
        {
            double y = x * x;
            double ans1 = x * (-4.900604943e13 + y * (1.275274390e13
                + y * (-5.153438139e11 + y * (7.349264551e9
                + y * (-4.237922726e7 + y * 8.511937935e4)))));
            double ans2 = 2.499580570e14 + y * (4.244419664e12
                + y * (3.733650367e10 + y * (2.245904002e8
                + y * (1.020426050e6 + y * (3.549632885e3 + y)))));
            return (ans1 / ans2) + 0.636619772 * (BesselJ1(x) * Math.Log(x) - 1.0 / x);
        }
        else
        {
            double z = 8.0 / x;
            double y = z * z;
            double xx = x - 2.356194491;
            double ans1 = 1.0 + y * (0.183105e-2 + y * (-0.3516396496e-4
                + y * (0.2457520174e-5 + y * -0.240337019e-6)));
            double ans2 = 0.04687499995 + y * (-0.2002690873e-3
                + y * (0.8449199096e-5 + y * (-0.88228987e-6 + y * 0.105787412e-6)));
            return Math.Sqrt(0.636619772 / x) * (Math.Sin(xx) * ans1 + z * Math.Cos(xx) * ans2);
        }
    }

    private static double BesselYN(int n, double x)
    {
        double tox = 2.0 / x;
        double by = BesselY1(x);
        double bym = BesselY0(x);
        for (int j = 1; j < n; j++)
        {
            double byp = j * tox * by - bym;
            bym = by;
            by = byp;
        }
        return by;
    }

    // ── Modified Bessel function of the first kind, I_n(x) ──────────────────

    private static double BesselI(int n, double x) => n switch
    {
        0 => BesselI0(x),
        1 => BesselI1(x),
        _ => BesselIN(n, x)
    };

    private static double BesselI0(double x)
    {
        double ax = Math.Abs(x);
        if (ax < 3.75)
        {
            double y = x / 3.75;
            y *= y;
            return 1.0 + y * (3.5156229 + y * (3.0899424 + y * (1.2067492
                + y * (0.2659732 + y * (0.360768e-1 + y * 0.45813e-2)))));
        }
        else
        {
            double y = 3.75 / ax;
            return (Math.Exp(ax) / Math.Sqrt(ax)) * (0.39894228 + y * (0.1328592e-1
                + y * (0.225319e-2 + y * (-0.157565e-2 + y * (0.916281e-2
                + y * (-0.2057706e-1 + y * (0.2635537e-1 + y * (-0.1647633e-1
                + y * 0.392377e-2))))))));
        }
    }

    private static double BesselI1(double x)
    {
        double ax = Math.Abs(x);
        double ans;
        if (ax < 3.75)
        {
            double y = x / 3.75;
            y *= y;
            ans = ax * (0.5 + y * (0.87890594 + y * (0.51498869 + y * (0.15084934
                + y * (0.2658733e-1 + y * (0.301532e-2 + y * 0.32411e-3))))));
        }
        else
        {
            double y = 3.75 / ax;
            double poly = 0.2282967e-1 + y * (-0.2895312e-1 + y * (0.1787654e-1 - y * 0.420059e-2));
            poly = 0.39894228 + y * (-0.3988024e-1 + y * (-0.362018e-2
                + y * (0.163801e-2 + y * (-0.1031555e-1 + y * poly))));
            ans = poly * (Math.Exp(ax) / Math.Sqrt(ax));
        }
        return x < 0.0 ? -ans : ans;
    }

    private static double BesselIN(int n, double x)
    {
        const double Acc = 40.0;
        const double BigNo = 1.0e10;
        const double BigNi = 1.0e-10;

        if (x == 0.0) return 0.0;

        double tox = 2.0 / Math.Abs(x);
        double bip = 0.0, ans = 0.0, bi = 1.0;
        for (int j = 2 * (n + (int)Math.Sqrt(Acc * n)); j > 0; j--)
        {
            double bim = bip + j * tox * bi;
            bip = bi;
            bi = bim;
            if (Math.Abs(bi) > BigNo)
            {
                ans *= BigNi;
                bi *= BigNi;
                bip *= BigNi;
            }
            if (j == n) ans = bip;
        }
        ans *= BesselI0(x) / bi;
        return x < 0.0 && (n & 1) != 0 ? -ans : ans;
    }

    // ── Modified Bessel function of the second kind, K_n(x) — requires x > 0 ─

    private static double BesselK(int n, double x) => n switch
    {
        0 => BesselK0(x),
        1 => BesselK1(x),
        _ => BesselKN(n, x)
    };

    private static double BesselK0(double x)
    {
        if (x <= 2.0)
        {
            double y = x * x / 4.0;
            return (-Math.Log(x / 2.0) * BesselI0(x)) + (-0.57721566 + y * (0.42278420
                + y * (0.23069756 + y * (0.3488590e-1 + y * (0.262698e-2
                + y * (0.10750e-3 + y * 0.74e-5))))));
        }
        else
        {
            double y = 2.0 / x;
            return (Math.Exp(-x) / Math.Sqrt(x)) * (1.25331414 + y * (-0.7832358e-1
                + y * (0.2189568e-1 + y * (-0.1062446e-1 + y * (0.587872e-2
                + y * (-0.251540e-2 + y * 0.53208e-3))))));
        }
    }

    private static double BesselK1(double x)
    {
        if (x <= 2.0)
        {
            double y = x * x / 4.0;
            return (Math.Log(x / 2.0) * BesselI1(x)) + (1.0 / x) * (1.0 + y * (0.15443144
                + y * (-0.67278579 + y * (-0.18156897 + y * (-0.1919402e-1
                + y * (-0.110404e-2 + y * -0.4686e-4))))));
        }
        else
        {
            double y = 2.0 / x;
            return (Math.Exp(-x) / Math.Sqrt(x)) * (1.25331414 + y * (0.23498619
                + y * (-0.3655620e-1 + y * (0.1504268e-1 + y * (-0.780353e-2
                + y * (0.325614e-2 + y * -0.68245e-3))))));
        }
    }

    private static double BesselKN(int n, double x)
    {
        double tox = 2.0 / x;
        double bkm = BesselK0(x);
        double bk = BesselK1(x);
        for (int j = 1; j < n; j++)
        {
            double bkp = bkm + j * tox * bk;
            bkm = bk;
            bk = bkp;
        }
        return bk;
    }

}
