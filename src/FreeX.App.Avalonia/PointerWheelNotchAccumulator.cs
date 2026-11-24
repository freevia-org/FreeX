namespace FreeX.App.Avalonia;

/// <summary>
/// Keeps sub-notch trackpad motion until it adds up to a worksheet wheel step. Rounding every
/// event away from zero makes scrolling depend on how many packets the device sends.
/// </summary>
internal sealed class PointerWheelNotchAccumulator
{
    private double _remainder;

    public int Accumulate(double delta)
    {
        if (!double.IsFinite(delta) || delta == 0)
            return 0;

        // A new direction should respond without first paying off the previous direction's tail.
        if (Math.Sign(delta) != Math.Sign(_remainder))
            _remainder = 0;

        var total = _remainder + delta;
        // Decimal packet sizes such as ten 0.1 deltas should reach the same boundary as one
        // whole notch despite floating-point addition error.
        var nearest = Math.Round(total);
        if (Math.Abs(total - nearest) < 1e-9)
            total = nearest;
        var whole = Math.Truncate(total);
        if (whole >= int.MaxValue || whole <= -int.MaxValue)
        {
            _remainder = 0;
            return whole > 0 ? int.MaxValue : -int.MaxValue;
        }

        _remainder = total - whole;
        return (int)whole;
    }

    public void Reset() => _remainder = 0;
}
