namespace FreeX.Core.Formula;

internal struct NumericAggregateAccumulator
{
    private double _varianceMean;

    public long Count { get; private set; }
    public double Sum { get; private set; }
    public double Product { get; private set; }
    public double Min { get; private set; }
    public double Max { get; private set; }
    public double VarianceM2 { get; private set; }
    public double Average => Sum / Count;
    public double SampleVariance => VarianceM2 / (Count - 1);
    public double PopulationVariance => VarianceM2 / Count;

    public void Add(double value, int functionNumber)
    {
        Count++;
        switch (functionNumber)
        {
            case 1:
            case 9:
                Sum += value;
                break;
            case 4:
                Max = Count == 1 ? value : Math.Max(Max, value);
                break;
            case 5:
                Min = Count == 1 ? value : Math.Min(Min, value);
                break;
            case 6:
                Product = Count == 1 ? value : Product * value;
                break;
            case 7:
            case 8:
            case 10:
            case 11:
                var delta = value - _varianceMean;
                _varianceMean += delta / Count;
                VarianceM2 += delta * (value - _varianceMean);
                break;
        }
    }
}
