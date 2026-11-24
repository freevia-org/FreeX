namespace FreeW.Core.Model;

/// <summary>OOXML numbering-level restart rules: a null rule uses the preceding level; zero never restarts.</summary>
public static class ListLevelRestartPolicy
{
    /// <param name="level">The zero-based level whose counter may restart.</param>
    /// <param name="advancedLevel">The zero-based level of the paragraph just encountered.</param>
    /// <param name="restartAfterLevel">OOXML's one-based parent index, zero, or null when omitted.</param>
    public static bool ShouldRestart(int level, int advancedLevel, int? restartAfterLevel)
    {
        if (level is <= 0 or >= MultiLevelListFormat.LevelCount
            || advancedLevel < 0 || advancedLevel >= level || restartAfterLevel == 0)
        {
            return false;
        }

        // An invalid/non-parent index is ignored, so it retains the omitted-rule behavior.
        var trigger = restartAfterLevel is > 0 and <= 7 && restartAfterLevel <= level
            ? restartAfterLevel.Value - 1
            : level - 1;
        // Word also resets when any higher ancestor appears (MS-OI29500 §2.1.282),
        // rather than only when the exact level named by the ISO rule appears.
        return advancedLevel <= trigger;
    }
}
