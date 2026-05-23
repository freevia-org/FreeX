using System.Windows.Threading;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public partial class GridView
{
    private Dictionary<(uint Row, uint Col), GridRange> _mergeLookup = [];

    private void RebuildMergeLookup()
    {
        _mergeLookup.Clear();
        if (MergedRegions == null || Viewport == null) return;

        var visRows = new HashSet<uint>(Viewport.RowMetrics.Select(r => r.Row));
        var visCols = new HashSet<uint>(Viewport.ColMetrics.Select(c => c.Col));

        foreach (var merge in MergedRegions)
        {
            for (uint r = merge.Start.Row; r <= merge.End.Row; r++)
            {
                if (!visRows.Contains(r)) continue;
                for (uint c = merge.Start.Col; c <= merge.End.Col; c++)
                {
                    if (visCols.Contains(c))
                        _mergeLookup[(r, c)] = merge;
                }
            }
        }
    }

    private DispatcherTimer? _marchTimer;
    private double _marchOffset;

    private void StartMarchTimer()
    {
        if (_marchTimer != null) return;
        _marchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _marchTimer.Tick += (_, _) =>
        {
            _marchOffset = (_marchOffset + 1.5) % 8.0;
            InvalidateVisual();
        };
        _marchTimer.Start();
    }

    private void StopMarchTimer()
    {
        _marchTimer?.Stop();
        _marchTimer = null;
        _marchOffset = 0;
        InvalidateVisual();
    }
}
