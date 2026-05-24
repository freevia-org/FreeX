using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

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
        Add(UnitCategory.Weight, "stone", 6350.293);
        Add(UnitCategory.Weight, "ton", 907184.74);
        Add(UnitCategory.Weight, "uk_ton", 1016046.91);
        Add(UnitCategory.Weight, "mg", 0.001);
        Add(UnitCategory.Weight, "ug", 0.000001);
        Add(UnitCategory.Weight, "ng", 1e-9);
        Add(UnitCategory.Weight, "sg", 14593.903);
        Add(UnitCategory.Weight, "cwt", 45359.237);
        Add(UnitCategory.Weight, "uk_cwt", 50802.345);

        // Distance (base = meter)
        Add(UnitCategory.Distance, "m", 1);
        Add(UnitCategory.Distance, "km", 1000);
        Add(UnitCategory.Distance, "mi", 1609.344);
        Add(UnitCategory.Distance, "Nmi", 1852);
        Add(UnitCategory.Distance, "in", 0.0254);
        Add(UnitCategory.Distance, "ft", 0.3048);
        Add(UnitCategory.Distance, "yd", 0.9144);
        Add(UnitCategory.Distance, "ang", 1e-10);
        Add(UnitCategory.Distance, "Pica", 0.000423333);
        Add(UnitCategory.Distance, "cm", 0.01);
        Add(UnitCategory.Distance, "mm", 0.001);
        Add(UnitCategory.Distance, "um", 1e-6);
        Add(UnitCategory.Distance, "nm", 1e-9);
        Add(UnitCategory.Distance, "ly", 9.4607304725808e15);
        Add(UnitCategory.Distance, "au", 149597870700.0);
        Add(UnitCategory.Distance, "pc", 3.085677581491367e16);

        // Time (base = second)
        Add(UnitCategory.Time, "sec", 1);
        Add(UnitCategory.Time, "s", 1);
        Add(UnitCategory.Time, "min", 60);
        Add(UnitCategory.Time, "mn", 2629800);
        Add(UnitCategory.Time, "hr", 3600);
        Add(UnitCategory.Time, "day", 86400);
        Add(UnitCategory.Time, "yr", 31557600);

        // Pressure (base = Pa)
        Add(UnitCategory.Pressure, "Pa", 1);
        Add(UnitCategory.Pressure, "atm", 101325);
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
        ["G"] = 1e9, ["M"] = 1e6, ["k"] = 1e3, ["h"] = 1e2, ["e"] = 1e1,
        ["d"] = 1e-1, ["c"] = 1e-2, ["m"] = 1e-3, ["u"] = 1e-6, ["n"] = 1e-9,
        ["p"] = 1e-12, ["f"] = 1e-15, ["a"] = 1e-18, ["z"] = 1e-21, ["y"] = 1e-24
    };

    private static bool TryResolveUnit(string unit, out UnitCategory cat, out double factor)
    {
        if (ConvertUnits.TryGetValue(unit, out var entry))
        {
            cat = entry.Cat;
            factor = entry.Factor;
            return true;
        }
        // Try a SI prefix only when at least 2 chars remain — we don't want
        // single-letter prefixes (e.g. "m") to be re-interpreted when they
        // already exist as base units in the table above.
        if (unit.Length >= 2)
        {
            string p = unit[..1];
            string rest = unit[1..];
            if (ConvertPrefixes.TryGetValue(p, out double pFactor)
                && ConvertUnits.TryGetValue(rest, out var rEntry)
                && rEntry.Cat != UnitCategory.Temperature)
            {
                cat = rEntry.Cat;
                factor = rEntry.Factor * pFactor;
                return true;
            }
        }
        cat = default; factor = 0; return false;
    }

    private static ScalarValue Convert(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        string from = ToText(args[1]);
        string to = ToText(args[2]);
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, value => ConvertScalar(value, from, to));
        return ConvertScalar(args[0], from, to);
    }

    private static ScalarValue ConvertScalar(ScalarValue numberValue, string from, string to)
    {
        double n = ToNumber(numberValue);
        if (!double.IsFinite(n)) return ErrorValue.Value;

        if (!TryResolveUnit(from, out var fromCat, out var fromFactor)) return ErrorValue.NA;
        if (!TryResolveUnit(to, out var toCat, out var toFactor)) return ErrorValue.NA;
        if (fromCat != toCat) return ErrorValue.NA;

        if (fromCat == UnitCategory.Temperature)
        {
            // Convert input to Kelvin, then to target.
            double k = from switch
            {
                "C"    => n + 273.15,
                "F"    => (n - 32) * 5.0 / 9.0 + 273.15,
                "K"    => n,
                "Rank" => n * 5.0 / 9.0,
                "Reau" => n * 5.0 / 4.0 + 273.15,
                _      => double.NaN
            };
            if (!double.IsFinite(k)) return ErrorValue.NA;
            double r = to switch
            {
                "C"    => k - 273.15,
                "F"    => (k - 273.15) * 9.0 / 5.0 + 32,
                "K"    => k,
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
        return FormatBaseText(value, toBase, places, upper);
    }

    private static ScalarValue DecimalToBase(IReadOnlyList<ScalarValue> args, int toBase, long min, long max, long modulus, int negativeWidth, bool upper)
    {
        if (args[0] is ErrorValue error) return error;
        if (args.Count > 1 && args[1] is ErrorValue placesError) return placesError;
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
        if (!TryGetEngineeringTruncatedInteger(placesArg, out var places) || places < 0 || places > int.MaxValue) return ErrorValue.Num;
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

    private static long NegativeModulusForBase(int toBase) => toBase switch
    {
        2 => 1024L,
        8 => 1073741824L,
        16 => 1099511627776L,
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
        if (args[1] is RangeValue rightRange) return MapUnaryTextRange(rightRange, value => BitBinaryScalar(args[0], value, op));
        if (args[0] is RangeValue leftRange) return MapUnaryTextRange(leftRange, value => BitBinaryScalar(value, args[1], op));
        return BitBinaryScalar(args[0], args[1], op);
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
        if (args[1] is RangeValue shiftRange) return MapUnaryTextRange(shiftRange, value => BitShiftScalar(args[0], value, leftShift));
        if (args[0] is RangeValue numberRange) return MapUnaryTextRange(numberRange, value => BitShiftScalar(value, args[1], leftShift));
        return BitShiftScalar(args[0], args[1], leftShift);
    }

    private static ScalarValue BitShiftScalar(ScalarValue numberValue, ScalarValue shiftValue, bool leftShift)
    {
        if (!TryGetBitInteger(numberValue, out var number)) return ErrorValue.Num;
        if (!TryGetEngineeringTruncatedInteger(shiftValue, out var shift) || Math.Abs(shift) > 53) return ErrorValue.Num;

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

}
