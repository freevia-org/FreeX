using System.Linq;

namespace FreeP.RenderCompare;

internal static class RenderCompareExitCodes
{
    internal static int Combine(params int[] exitCodes) =>
        exitCodes.Length == 0 ? 0 : exitCodes.Max();
}
