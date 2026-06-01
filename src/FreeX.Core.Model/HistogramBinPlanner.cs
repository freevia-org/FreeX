using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FreeX.Core.Model;

/// <summary>How a histogram chart decides its bin edges (Excel "Format Axis ▸ Bins").</summary>
public enum HistogramBinningMode
{
    /// <summary>Square-root rule: <c>ceil(sqrt(n))</c> equal-width bins over the data range.</summary>
    Automatic,

    /// <summary>Equal-width bins of <see cref="HistogramBinningModel.BinWidth"/>.</summary>
    BinWidth,

    /// <summary>Exactly <see cref="HistogramBinningModel.BinCount"/> equal-width bins.</summary>
    BinCount,
}

/// <summary>
/// Histogram binning settings (Excel "Format Axis" for a histogram value axis): the binning mode plus
/// optional overflow/underflow thresholds that collapse the tails into single bins. Immutable and
/// WPF-free so the binning math lives in <see cref="HistogramBinPlanner"/> and is unit-testable.
/// </summary>
public sealed record HistogramBinningModel(
    HistogramBinningMode Mode = HistogramBinningMode.Automatic,
    double? BinWidth = null,
    int? BinCount = null,
    double? OverflowThreshold = null,
    double? UnderflowThreshold = null);

/// <summary>Whether a computed bin is the underflow tail, a normal interior bin, or the overflow tail.</summary>
public enum HistogramBinKind
{
    Underflow,
    Normal,
    Overflow,
}

/// <summary>
/// A single computed histogram bin. <see cref="Min"/>/<see cref="Max"/> are the value-axis edges
/// (the underflow bin extends to -∞, the overflow bin to +∞); <see cref="Count"/> is how many input
/// values fell in it; <see cref="Label"/> is the category-axis caption.
/// </summary>
public readonly record struct HistogramBin(double Min, double Max, int Count, HistogramBinKind Kind, string Label);

/// <summary>
/// Pure, deterministic histogram binning. Turns a set of numeric values plus a
/// <see cref="HistogramBinningModel"/> into the ordered list of bins a histogram renderer draws.
/// No WPF dependency; the WPF renderer (<c>ChartRenderer.BuildHistogramModel</c>) consumes the result.
///
/// Binning convention: values at or below <see cref="HistogramBinningModel.UnderflowThreshold"/> go to a
/// single leading underflow bin; values strictly above <see cref="HistogramBinningModel.OverflowThreshold"/>
/// go to a single trailing overflow bin; the remaining values are partitioned into equal-width interior
/// bins spanning the in-range minimum/threshold up to the in-range maximum/threshold, with the maximum
/// value landing in the last interior bin.
/// </summary>
public static class HistogramBinPlanner
{
    public static IReadOnlyList<HistogramBin> Compute(IReadOnlyList<double> values, HistogramBinningModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (values is null || values.Count == 0)
            return [];

        var underflow = settings.UnderflowThreshold;
        var overflow = settings.OverflowThreshold;

        var normal = new List<double>(values.Count);
        var underflowCount = 0;
        var overflowCount = 0;
        foreach (var v in values)
        {
            if (underflow is { } u && v <= u)
                underflowCount++;
            else if (overflow is { } o && v > o)
                overflowCount++;
            else
                normal.Add(v);
        }

        var result = new List<HistogramBin>();

        if (normal.Count > 0)
        {
            var lo = underflow ?? normal.Min();
            var hi = overflow ?? normal.Max();
            var range = hi - lo;

            var (binCount, binWidth) = ResolveBinning(settings, range, normal.Count);

            var counts = new int[binCount];
            foreach (var v in normal)
            {
                var idx = range <= 0 ? 0 : (int)Math.Floor((v - lo) / binWidth);
                counts[Math.Clamp(idx, 0, binCount - 1)]++;
            }

            for (var i = 0; i < binCount; i++)
            {
                double binMin, binMax;
                if (range <= 0)
                {
                    binMin = lo;
                    binMax = hi;
                }
                else
                {
                    binMin = lo + (i * binWidth);
                    binMax = i == binCount - 1 ? Math.Max(hi, binMin) : lo + ((i + 1) * binWidth);
                }

                result.Add(new HistogramBin(
                    binMin, binMax, counts[i], HistogramBinKind.Normal,
                    $"{Format(binMin)}–{Format(binMax)}"));
            }
        }

        if (underflow is { } uu)
            result.Insert(0, new HistogramBin(
                double.NegativeInfinity, uu, underflowCount, HistogramBinKind.Underflow, $"≤{Format(uu)}"));

        if (overflow is { } oo)
            result.Add(new HistogramBin(
                oo, double.PositiveInfinity, overflowCount, HistogramBinKind.Overflow, $">{Format(oo)}"));

        return result;
    }

    private static (int BinCount, double BinWidth) ResolveBinning(
        HistogramBinningModel settings, double range, int normalCount)
    {
        if (range <= 0)
            return (1, 1);

        switch (settings.Mode)
        {
            case HistogramBinningMode.BinCount when settings.BinCount is { } count && count > 0:
                return (count, range / count);
            case HistogramBinningMode.BinWidth when settings.BinWidth is { } width && width > 0:
                return (Math.Max(1, (int)Math.Ceiling(range / width)), width);
            default:
                var automatic = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(normalCount)));
                return (automatic, range / automatic);
        }
    }

    private static string Format(double value) =>
        value.ToString("G4", CultureInfo.InvariantCulture);
}
