using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- C2: Depreciation functions ---------------------------------------

    private static ScalarValue Sln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        return MapScalarArgs([args[0], args[1], args[2]], values => SlnScalar(values[0], values[1], values[2]));
    }

    private static ScalarValue SlnScalar(ScalarValue costValue, ScalarValue salvageValue, ScalarValue lifeValue)
    {
        double cost = ToNumber(costValue);
        double salvage = ToNumber(salvageValue);
        return SlnScalar(cost, salvage, lifeValue);
    }

    private static ScalarValue SlnScalar(double cost, double salvage, ScalarValue lifeValue)
    {
        double life = ToNumber(lifeValue);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life))
            return ErrorValue.Num;
        if (life == 0) return ErrorValue.DivByZero;
        return NumberResult((cost - salvage) / life);
    }

    private static ScalarValue Db(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var monthArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], monthArg], values => DbScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue DbScalar(ScalarValue costValue, ScalarValue salvageValue, ScalarValue lifeValue, ScalarValue periodValue, ScalarValue monthValue)
    {
        double cost = ToNumber(costValue);
        double salvage = ToNumber(salvageValue);
        double life = ToNumber(lifeValue);
        double month = monthValue is BlankValue ? 12 : ToNumber(monthValue);
        return DbScalar(cost, salvage, life, periodValue, month);
    }

    private static ScalarValue DbScalar(double cost, double salvage, double life, ScalarValue periodValue, double month)
    {
        double period = ToNumber(periodValue);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(period) || !double.IsFinite(month))
            return ErrorValue.Num;
        int ilife = (int)Math.Truncate(life), iper = (int)Math.Truncate(period);
        int imonth = (int)Math.Truncate(month);
        if (cost <= 0 || salvage < 0 || ilife <= 0 || iper <= 0 || iper > ilife + 1) return ErrorValue.Num;
        if (imonth < 1 || imonth > 12) return ErrorValue.Num;
        if (salvage >= cost) return NumberResult(0);
        // Rate rounded to 3 decimal places.
        double rate = Math.Round(1 - Math.Pow(salvage / cost, 1.0 / ilife), 3);
        double accumulated = 0;
        double dep = 0;
        for (int p = 1; p <= iper; p++)
        {
            if (p == 1)
                dep = cost * rate * imonth / 12.0;
            else if (p <= ilife)
                dep = (cost - accumulated) * rate;
            else
                dep = (cost - accumulated) * rate * (12 - imonth + 1) / 12.0;
            if (p < iper) accumulated += dep;
        }
        return NumberResult(dep);
    }

    private static ScalarValue Ddb(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var factorArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], factorArg], values => DdbScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue DdbScalar(ScalarValue costValue, ScalarValue salvageValue, ScalarValue lifeValue, ScalarValue periodValue, ScalarValue factorValue)
    {
        double cost = ToNumber(costValue);
        double salvage = ToNumber(salvageValue);
        double life = ToNumber(lifeValue);
        double factor = factorValue is BlankValue ? 2.0 : ToNumber(factorValue);
        return DdbScalar(cost, salvage, life, periodValue, factor);
    }

    private static ScalarValue DdbScalar(double cost, double salvage, double life, ScalarValue periodValue, double factor)
    {
        double period = ToNumber(periodValue);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(period) || !double.IsFinite(factor))
            return ErrorValue.Num;
        if (cost < 0 || salvage < 0 || life <= 0 || period <= 0 || factor <= 0) return ErrorValue.Num;
        double bookValue = cost;
        int ip = (int)Math.Truncate(period);
        for (int p = 1; p <= ip; p++)
        {
            double dep = Math.Min(bookValue - salvage, bookValue * factor / life);
            dep = Math.Max(dep, 0);
            if (p < ip) { bookValue -= dep; }
            else return NumberResult(dep);
        }
        return NumberResult(0);
    }

    private static ScalarValue Vdb(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var factorArg = args.Count > 5 ? args[5] : BlankValue.Instance;
        var noSwitchArg = args.Count > 6 ? args[6] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], factorArg, noSwitchArg], values => VdbScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6]));
    }

    private static ScalarValue VdbScalar(ScalarValue costValue, ScalarValue salvageValue, ScalarValue lifeValue, ScalarValue startPeriodValue, ScalarValue endPeriodValue, ScalarValue factorValue, ScalarValue noSwitchValue)
    {
        double cost = ToNumber(costValue);
        double salvage = ToNumber(salvageValue);
        double life = ToNumber(lifeValue);
        double startPeriod = ToNumber(startPeriodValue);
        double factor = factorValue is BlankValue ? 2.0 : ToNumber(factorValue);
        bool noSwitch = noSwitchValue is not BlankValue && ToBool(noSwitchValue);
        return VdbScalar(cost, salvage, life, startPeriod, endPeriodValue, factor, noSwitch);
    }

    private static ScalarValue VdbScalar(double cost, double salvage, double life, double startPeriod, ScalarValue endPeriodValue, double factor, bool noSwitch)
    {
        double endPeriod = ToNumber(endPeriodValue);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(startPeriod) || !double.IsFinite(endPeriod) || !double.IsFinite(factor))
            return ErrorValue.Num;
        if (cost < 0 || salvage < 0 || life <= 0 || startPeriod < 0 || endPeriod < startPeriod || factor <= 0)
            return ErrorValue.Num;
        if (endPeriod > life) return ErrorValue.Num;

        double totalDep = 0;
        double bookValue = cost;
        double currentPeriod = startPeriod;
        while (currentPeriod < endPeriod)
        {
            double periodEnd = Math.Min(Math.Ceiling(currentPeriod + 1e-10), endPeriod);
            double fraction = periodEnd - currentPeriod;
            int p = (int)Math.Floor(currentPeriod + 1e-10);
            double ddbDep = bookValue * factor / life;
            double slnDep = (bookValue - salvage) / (life - p);
            double dep = !noSwitch && slnDep > ddbDep ? slnDep : ddbDep;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            double partialDep = dep * fraction;
            totalDep += partialDep;
            bookValue -= partialDep;
            currentPeriod = periodEnd;
        }
        return NumberResult(totalDep);
    }

    private static ScalarValue Syd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapScalarArgs([args[0], args[1], args[2], args[3]], values => SydScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue SydScalar(ScalarValue costValue, ScalarValue salvageValue, ScalarValue lifeValue, ScalarValue periodValue)
    {
        double cost = ToNumber(costValue);
        double salvage = ToNumber(salvageValue);
        double life = ToNumber(lifeValue);
        return SydScalar(cost, salvage, life, periodValue);
    }

    private static ScalarValue SydScalar(double cost, double salvage, double life, ScalarValue periodValue)
    {
        double per = ToNumber(periodValue);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) || !double.IsFinite(per))
            return ErrorValue.Num;
        if (life <= 0 || per <= 0 || per > life) return ErrorValue.Num;
        double result = (cost - salvage) * (life - per + 1) / (life * (life + 1) / 2.0);
        return NumberResult(result);
    }

    private static ScalarValue Amordegrc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 6 ? args[6] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], basisArg], values => AmordegrcScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6]));
    }

    private static ScalarValue AmordegrcScalar(ScalarValue costValue, ScalarValue datePurchasedValue, ScalarValue firstPeriodValue, ScalarValue salvageValue, ScalarValue periodValue, ScalarValue rateValue, ScalarValue basisValue)
    {
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        double cost = ToNumber(costValue);
        double datePurchased = ToNumber(datePurchasedValue);
        double firstPeriod = ToNumber(firstPeriodValue);
        double salvage = ToNumber(salvageValue);
        double rate = ToNumber(rateValue);
        return AmordegrcScalar(cost, datePurchased, firstPeriod, salvage, periodValue, rate, basis);
    }

    private static ScalarValue AmordegrcScalar(double cost, double datePurchased, double firstPeriod, double salvage, ScalarValue periodValue, double rate, int basis)
    {
        double period = ToNumber(periodValue);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(rate) || !double.IsFinite(period))
            return ErrorValue.Num;
        if (cost <= 0 || salvage < 0 || rate <= 0) return ErrorValue.Num;

        double life = 1.0 / rate;
        double coeff;
        if (life < 3) coeff = 1.0;
        else if (life < 5) coeff = 1.5;
        else if (life <= 6) coeff = 2.0;
        else coeff = 2.5;

        double depRate = rate * coeff;
        if (!TryGetFinancialDate(datePurchased, out DateTime dp) ||
            !TryGetFinancialDate(firstPeriod, out DateTime fp)) return ErrorValue.Num;
        double firstFrac = DayCountFraction(dp, fp, basis);
        double bookValue = cost;
        int iper = (int)Math.Truncate(period);
        for (int p = 0; p <= iper; p++)
        {
            double dep = p == 0 ? bookValue * depRate * firstFrac : bookValue * depRate;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            if (p < iper)
                bookValue -= dep;
            else
                return NumberResult(dep);
        }
        return NumberResult(0);
    }

    private static ScalarValue Amorlinc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 6 ? args[6] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], basisArg], values => AmorlincScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6]));
    }

    private static ScalarValue AmorlincScalar(ScalarValue costValue, ScalarValue datePurchasedValue, ScalarValue firstPeriodValue, ScalarValue salvageValue, ScalarValue periodValue, ScalarValue rateValue, ScalarValue basisValue)
    {
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        double cost = ToNumber(costValue);
        double datePurchased = ToNumber(datePurchasedValue);
        double firstPeriod = ToNumber(firstPeriodValue);
        double salvage = ToNumber(salvageValue);
        double rate = ToNumber(rateValue);
        return AmorlincScalar(cost, datePurchased, firstPeriod, salvage, periodValue, rate, basis);
    }

    private static ScalarValue AmorlincScalar(double cost, double datePurchased, double firstPeriod, double salvage, ScalarValue periodValue, double rate, int basis)
    {
        double period = ToNumber(periodValue);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(rate) || !double.IsFinite(period))
            return ErrorValue.Num;
        if (cost <= 0 || salvage < 0 || rate <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(datePurchased, out DateTime dp) ||
            !TryGetFinancialDate(firstPeriod, out DateTime fp)) return ErrorValue.Num;
        double firstFrac = DayCountFraction(dp, fp, basis);
        double annualDep = cost * rate;
        double bookValue = cost;
        int iper = (int)Math.Truncate(period);
        for (int p = 0; p <= iper; p++)
        {
            double dep = p == 0 ? annualDep * firstFrac : annualDep;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            if (p < iper)
                bookValue -= dep;
            else
                return NumberResult(dep);
        }
        return NumberResult(0);
    }
}
