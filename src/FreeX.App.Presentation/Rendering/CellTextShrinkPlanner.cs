namespace FreeX.App.Presentation.Rendering;

public static class CellTextShrinkPlanner
{
    public static double ResolveFontSize(
        double requestedFontSize,
        double availableWidth,
        Func<double, double> measureTextWidth,
        double minimumFontSize)
    {
        ArgumentNullException.ThrowIfNull(measureTextWidth);

        if (requestedFontSize <= minimumFontSize || availableWidth <= 0)
            return Math.Min(requestedFontSize, minimumFontSize);

        var fontSize = requestedFontSize;
        while (fontSize > minimumFontSize && measureTextWidth(fontSize) > availableWidth)
            fontSize = Math.Max(minimumFontSize, fontSize - 1);

        return fontSize;
    }
}
