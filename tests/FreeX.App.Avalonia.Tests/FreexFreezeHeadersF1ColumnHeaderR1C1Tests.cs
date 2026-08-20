using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for freex-freeze-headers F1 (src/FreeX.App.Avalonia/MainWindow.cs,
/// CreateColumnHeaderCell): the Avalonia shell's column header row never consulted
/// <c>UseR1C1ReferenceStyle</c>, so it always drew A/B/C... letters even after File ▸ Options ▸
/// Formulas ▸ "R1C1 reference style" was enabled -- unlike the WPF host, whose
/// GridView.Rendering.Headers.cs FormatColumnHeader switches to plain numeric column indexes under
/// the same option, and unlike this same Avalonia shell's own formula bar / Name Box, which already
/// read this property (see UseR1C1ReferenceStyle's other call sites in MainWindow.cs).
///
/// These tests drive the real rendering path (<c>RebuildSheetGridForTest</c> -&gt;
/// <c>BuildSheetGrid</c> -&gt; <c>CreateColumnHeaderCell</c> -&gt; <c>FormatColumnHeaderLabel</c>)
/// against the actual on-screen header Border, found by its "ColumnHeader_{letter}" automation id
/// (which deliberately stays letter-based regardless of the option -- it is test/selection hookup,
/// not the user-visible label), and read the label TextBlock inside it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreexFreezeHeadersF1ColumnHeaderR1C1Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ColumnHeader_UsesNumericLabel_WhenR1C1ReferenceStyleEnabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("CleanFixture");
                window.Session.SelectSheet(sheet.Id);

                window.UseR1C1ReferenceStyleForTest = true;

                var grid = window.RebuildSheetGridForTest();

                // Column C (index 3): under R1C1 reference style Excel/the WPF host show the plain
                // numeric column index ("3"), not the A1-style letter ("C"). Before this fix, the
                // Avalonia column header ignored the option entirely and always rendered "C" here.
                var headerLabel = GetColumnHeaderLabel(grid, "ColumnHeader_C");
                headerLabel.Should().Be("3",
                    "the column header must switch to numeric R1C1 style once the option is enabled, " +
                    "matching the WPF host's FormatColumnHeader and this shell's own formula bar/Name Box");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    // ── Sibling/no-regression: the default A1 letter style must still render exactly as before ──

    [Fact]
    public async Task ColumnHeader_UsesLetterLabel_WhenR1C1ReferenceStyleDisabled()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("CleanFixture");
                window.Session.SelectSheet(sheet.Id);

                // R1C1 reference style is off by default -- this must keep behaving exactly as
                // before the fix.
                window.UseR1C1ReferenceStyleForTest.Should().BeFalse(
                    "R1C1 reference style must default to off");

                var grid = window.RebuildSheetGridForTest();

                var headerLabel = GetColumnHeaderLabel(grid, "ColumnHeader_C");
                headerLabel.Should().Be("C",
                    "with the option off, the column header must keep showing the A1-style letter " +
                    "exactly as before this fix");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Resolves the real on-screen header Border by its "ColumnHeader_{letter}" automation id (which
    /// stays letter-based regardless of R1C1 reference style -- see the class doc) and reads the
    /// label TextBlock nested inside it.
    /// </summary>
    private static string? GetColumnHeaderLabel(Control root, string automationId)
    {
        var header = FindDescendants(root)
            .OfType<Border>()
            .Single(border => AutomationProperties.GetAutomationId(border) == automationId);

        return FindDescendants(header).OfType<TextBlock>().FirstOrDefault()?.Text;
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Border { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var c in panel.Children)
            {
                yield return c;
                foreach (var descendant in FindDescendants(c))
                    yield return descendant;
            }
        }
    }
}
