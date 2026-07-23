using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R76-render-freeze-scroll-4-2 (MainWindow.Viewport.cs's
/// SheetGrid_MouseWheel): the mouse-wheel step was hardcoded to 3 rows/cols per notch, ignoring
/// the OS "Number of lines to scroll" setting (SystemParameters.WheelScrollLines) that Excel
/// honors. NormalizeWheelScrollLines is the pure, testable resolution of that OS value into an
/// actual per-notch step (used for BOTH the vertical wheel and the Shift+wheel horizontal path,
/// since SheetGrid_MouseWheel calls it identically for both axes); GetSystemWheelScrollLines
/// (exercised here through its _wheelScrollLinesTestOverride reflection seam) is the thin OS-read
/// wrapper SheetGrid_MouseWheel actually calls before passing the result through
/// NormalizeWheelScrollLines.
/// </summary>
public sealed class R76_WheelScrollLinesTests
{
    [Fact]
    public void NormalizeWheelScrollLines_WithOsValueOne_ReturnsOneLinePerNotch()
    {
        MainWindow.NormalizeWheelScrollLines(1, visibleSpan: 20).Should().Be(1,
            "the OS 'Number of lines to scroll' setting of 1 must produce exactly a 1-row/col step per notch");
    }

    [Fact]
    public void NormalizeWheelScrollLines_WithOsValueTen_ReturnsTenLinesPerNotch()
    {
        MainWindow.NormalizeWheelScrollLines(10, visibleSpan: 20).Should().Be(10,
            "the OS 'Number of lines to scroll' setting of 10 must produce exactly a 10-row/col step per notch");
    }

    [Fact]
    public void NormalizeWheelScrollLines_WithOsValueThree_ReturnsThreeLinesPerNotch_MatchingThePreFixDefault()
    {
        // Sibling no-regression: the previously hardcoded literal (3) must remain the observed
        // behavior for a machine whose real OS setting also happens to be the common default of 3.
        MainWindow.NormalizeWheelScrollLines(3, visibleSpan: 20).Should().Be(3);
    }

    [Fact]
    public void NormalizeWheelScrollLines_WithOsValueZeroOrUnreadable_FallsBackToDefaultOfThree()
    {
        MainWindow.NormalizeWheelScrollLines(0, visibleSpan: 20).Should().Be(
            MainWindow.DefaultWheelScrollLinesPerNotch);
        MainWindow.DefaultWheelScrollLinesPerNotch.Should().Be(3,
            "the documented fallback for an invalid/unavailable OS setting must stay the previous hardcoded default");
    }

    [Fact]
    public void NormalizeWheelScrollLines_WithOnePageSentinel_FallsBackToVisibleSpanClamped()
    {
        // Windows' "-1 = scroll one screen at a time" sentinel maps to the visible span itself
        // rather than a negative/nonsensical step.
        MainWindow.NormalizeWheelScrollLines(-1, visibleSpan: 25).Should().Be(25);
    }

    [Fact]
    public void NormalizeWheelScrollLines_ClampsAnAbsurdlyLargeOsValue()
    {
        MainWindow.NormalizeWheelScrollLines(int.MaxValue, visibleSpan: 20).Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void GetSystemWheelScrollLines_HonorsTestOverride_ForOne_Ten_AndDefault()
    {
        var overrideField = typeof(MainWindow).GetField(
            "_wheelScrollLinesTestOverride", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_wheelScrollLinesTestOverride");
        var getSystemWheelScrollLines = typeof(MainWindow).GetMethod(
            "GetSystemWheelScrollLines", BindingFlags.Static | BindingFlags.NonPublic, [])
            ?? throw new MissingMethodException(nameof(MainWindow), "GetSystemWheelScrollLines");

        try
        {
            overrideField.SetValue(null, 1);
            ((int)getSystemWheelScrollLines.Invoke(null, [])!).Should().Be(1);

            overrideField.SetValue(null, 10);
            ((int)getSystemWheelScrollLines.Invoke(null, [])!).Should().Be(10);

            // Sibling no-regression: with no override, the real SystemParameters read (or its
            // exception fallback) must still resolve to SOME usable, in-range value -- never throw.
            overrideField.SetValue(null, null);
            var fallbackValue = (int)getSystemWheelScrollLines.Invoke(null, [])!;
            MainWindow.NormalizeWheelScrollLines(fallbackValue, visibleSpan: 20).Should().BeInRange(1, 100);
        }
        finally
        {
            overrideField.SetValue(null, null);
        }
    }
}
