namespace FreeX.App.Presentation.Charts;

/// <summary>
/// R90-render-chart-axis-titles-5-2: Excel's Format Axis &gt; Labels &gt; "Interval between labels"
/// (<c>&lt;c:tickLblSkip&gt;</c>) and "Interval between tick marks" (<c>&lt;c:tickMarkSkip&gt;</c>) on a
/// category axis, round-tripped through <see cref="FreeX.Core.Model.ChartModel.XAxisLabelSkip"/> and
/// <see cref="FreeX.Core.Model.ChartModel.XAxisTickMarkSkip"/>.
/// <para>
/// Zero-vs-one convention: the model stores 0 for "unspecified" -- that is what
/// <c>XlsxChartAxisReader</c> falls back to when the element is absent, what
/// <c>NativeJsonAdapter.ChartSanitization</c> clamps to, and what <c>XlsxChartXmlWriter.Axes</c>'s
/// <c>ToUnsignedAxisValueXml</c> treats as "omit the element". ECMA-376's CT_Skip defaults to 1, and
/// Excel's own default-is-every-label is written as either an absent element or val="1", so 0 and 1
/// are the same thing to a renderer: draw everything. Only values &gt;= 2 actually thin anything out.
/// </para>
/// </summary>
public static class ChartCategoryAxisSkip
{
    /// <summary>
    /// Normalises a raw model skip value to the interval Excel draws at: 0 (unspecified) and 1 (the
    /// CT_Skip default) both mean "every category", N means "every Nth category".
    /// </summary>
    public static int ResolveInterval(int skip) => skip <= 1 ? 1 : skip;

    /// <summary>
    /// True when the category at <paramref name="categoryIndex"/> is one of the ones Excel keeps at
    /// the given interval. Excel anchors the kept set on the first category, so with an interval of 5
    /// the labels drawn are categories 0, 5, 10, ... (the 1st, 6th, 11th in the sheet).
    /// </summary>
    public static bool IsShown(int categoryIndex, int interval) =>
        interval <= 1 || categoryIndex % interval == 0;
}
