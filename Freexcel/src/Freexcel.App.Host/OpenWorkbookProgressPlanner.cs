namespace Freexcel.App.Host;

public static class OpenWorkbookProgressPlanner
{
    public static double CalculateStageProgress(
        double stageStartPercent,
        double stageEndPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration)
    {
        if (stageEndPercent <= stageStartPercent)
            return stageStartPercent;

        var duration = expectedDuration.TotalMilliseconds <= 0
            ? 1
            : expectedDuration.TotalMilliseconds;
        var ratio = Math.Clamp(elapsed.TotalMilliseconds / duration, 0, 1);
        var maxWhileRunning = stageEndPercent - 0.5;
        return Math.Min(maxWhileRunning, stageStartPercent + (stageEndPercent - stageStartPercent) * ratio);
    }

    public static string FormatLoadingFileDetail(string phase, TimeSpan elapsed)
    {
        var normalizedPhase = string.IsNullOrWhiteSpace(phase)
            ? "working"
            : phase.Trim();
        string[] messages = normalizedPhase.ToLowerInvariant() switch
        {
            "reading" =>
            [
                "Loading file (reading)",
                "Loading file (reading bytes)",
                "Loading file (checking package)"
            ],
            "inspecting" =>
            [
                "Loading file (inspecting)",
                "Loading file (checking workbook parts)",
                "Loading file (detecting features)"
            ],
            "parsing" =>
            [
                "Loading file (parsing)",
                "Loading file (reading worksheets)",
                "Loading file (building workbook)",
                "Loading file (loading styles)"
            ],
            "calculating" =>
            [
                "Loading file (calculating)",
                "Loading file (evaluating formulas)",
                "Loading file (refreshing values)"
            ],
            "preparing view" =>
            [
                "Loading file (preparing view)",
                "Loading file (laying out worksheet)",
                "Loading file (restoring selection)"
            ],
            _ => [$"Loading file ({normalizedPhase})"]
        };

        var index = (int)Math.Floor(Math.Max(0, elapsed.TotalSeconds) / 3.0) % messages.Length;
        return messages[index];
    }
}
